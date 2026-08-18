namespace QbPotDoubanAi;

internal enum WorkerJobType
{
    ReviewDelete,
    ReviewSave,
    OfficialReviewRead,
    Search
}

internal sealed record WorkerJobDescriptor(
    string JobId,
    WorkerJobType JobType,
    int Priority,
    string SubjectId,
    string RequestId,
    string TargetUrl,
    DateTimeOffset CreatedAt,
    bool NonCancelableOnceStarted = false);

/// <summary>
/// Single-consumer, priority-aware dispatcher for the navigation-only Douban WebView2.
/// All job delegates are invoked on the owning WinForms UI thread because WebView2 is
/// thread-affine. Lower numeric priority runs first.
/// BuildFix12 R8 adds browser-restart fencing so a dead WebView2 cannot block the
/// queue for its normal navigation timeout.
/// </summary>
internal sealed class WorkerJobQueue : IDisposable
{
    private sealed class QueuedJob
    {
        internal required WorkerJobDescriptor Descriptor { get; init; }
        internal required Func<CancellationToken, Task<object?>> Work { get; init; }
        internal required TaskCompletionSource<object?> Completion { get; init; }
        internal required CancellationTokenSource Cancellation { get; init; }
        internal string DedupeKey { get; init; } = "";
    }

    private readonly Control _owner;
    private readonly object _gate = new();
    private readonly PriorityQueue<QueuedJob, (int Priority, long Sequence)> _pending = new();
    private readonly Dictionary<string, QueuedJob> _dedupe = new(StringComparer.Ordinal);
    private long _sequence;
    private bool _pumpScheduled;
    private bool _disposed;
    private bool _pausedForBrowserRecovery;
    private QueuedJob? _running;

    internal WorkerJobQueue(Control owner) => _owner = owner;

    internal static string NewJobId(WorkerJobType type) =>
        $"{type.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";

    internal static int PriorityFor(WorkerJobType type) => type switch
    {
        WorkerJobType.ReviewDelete => 0,
        WorkerJobType.ReviewSave => 0,
        WorkerJobType.OfficialReviewRead => 1,
        _ => 4
    };

    internal Task<T> EnqueueAsync<T>(
        WorkerJobDescriptor descriptor,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WorkerJobQueue));
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dedupeKey = DedupeKeyFor(descriptor);
        var queued = new QueuedJob
        {
            Descriptor = descriptor,
            Cancellation = linked,
            Completion = completion,
            DedupeKey = dedupeKey,
            Work = async token => await work(token).ConfigureAwait(true)
        };

        lock (_gate)
        {
            if (dedupeKey.Length > 0 && _dedupe.TryGetValue(dedupeKey, out var existing))
            {
                linked.Dispose();
                DiagnosticLogger.Write($"Worker queue coalesced; WebView=Worker; JobId={descriptor.JobId}; ExistingJobId={existing.Descriptor.JobId}; JobType={descriptor.JobType}; TargetUrl={descriptor.TargetUrl}; Reason=duplicate-{dedupeKey.Split('|')[0]}-read");
                return AwaitResultAsync<T>(existing.Completion.Task);
            }

            if (descriptor.Priority <= 1)
            {
                CancelPendingUnsafe(job => job.Descriptor.Priority > descriptor.Priority,
                    $"preempted-by-{descriptor.JobType}");
                if (_running is { } running && running.Descriptor.Priority > descriptor.Priority &&
                    !running.Descriptor.NonCancelableOnceStarted)
                {
                    DiagnosticLogger.Write($"Worker queue cancellation requested; WebView=Worker; JobId={running.Descriptor.JobId}; JobType={running.Descriptor.JobType}; Reason=preempted-by-{descriptor.JobType}");
                    running.Cancellation.Cancel();
                }
            }

            _pending.Enqueue(queued, (descriptor.Priority, Interlocked.Increment(ref _sequence)));
            if (dedupeKey.Length > 0) _dedupe[dedupeKey] = queued;
            DiagnosticLogger.Write($"Worker queue enqueued; WebView=Worker; JobId={descriptor.JobId}; JobType={descriptor.JobType}; Priority={descriptor.Priority}; SubjectId={descriptor.SubjectId}; RequestId={descriptor.RequestId}; TargetUrl={descriptor.TargetUrl}; CreatedAt={descriptor.CreatedAt:O}; PausedForBrowserRecovery={_pausedForBrowserRecovery}");
            SchedulePumpUnsafe();
        }

        return AwaitResultAsync<T>(completion.Task);
    }

    internal void PauseForBrowserRecovery(string reason)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pausedForBrowserRecovery = true;
            CancelPendingUnsafe(_ => true, reason);
            if (_running is { } running)
            {
                RemoveDedupeUnsafe(running);
                try { running.Cancellation.Cancel(); } catch { }
                DiagnosticLogger.Write($"Worker queue browser-restart abort requested; WebView=Worker; JobId={running.Descriptor.JobId}; JobType={running.Descriptor.JobType}; NonCancelableOnceStarted={running.Descriptor.NonCancelableOnceStarted}; Reason={reason}");
            }
            DiagnosticLogger.Write($"Worker queue paused; WebView=Worker; Reason={reason}");
        }
    }

    internal void ResumeAfterBrowserRecovery(string reason)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pausedForBrowserRecovery = false;
            DiagnosticLogger.Write($"Worker queue resumed; WebView=Worker; Reason={reason}");
            SchedulePumpUnsafe();
        }
    }

    internal void CancelPending(Func<WorkerJobDescriptor, bool> predicate, string reason)
    {
        lock (_gate)
        {
            CancelPendingUnsafe(job => predicate(job.Descriptor), reason);
            if (_running is { } running && predicate(running.Descriptor) && !running.Descriptor.NonCancelableOnceStarted)
            {
                DiagnosticLogger.Write($"Worker queue cancellation requested; WebView=Worker; JobId={running.Descriptor.JobId}; JobType={running.Descriptor.JobType}; Reason={reason}");
                running.Cancellation.Cancel();
            }
        }
    }

    private static async Task<T> AwaitResultAsync<T>(Task<object?> task)
    {
        var value = await task.ConfigureAwait(true);
        return value is T typed ? typed : value is null && default(T) is null
            ? default!
            : throw new InvalidCastException($"Worker job result cannot be converted to {typeof(T).FullName}.");
    }

    private static string DedupeKeyFor(WorkerJobDescriptor descriptor)
    {
        return "";
    }

    private void RemoveDedupeUnsafe(QueuedJob job)
    {
        if (job.DedupeKey.Length == 0) return;
        if (_dedupe.TryGetValue(job.DedupeKey, out var current) && ReferenceEquals(current, job))
            _dedupe.Remove(job.DedupeKey);
    }

    private void CancelPendingUnsafe(Func<QueuedJob, bool> predicate, string reason)
    {
        if (_pending.Count == 0) return;
        var retained = new List<(QueuedJob Job, (int Priority, long Sequence) Key)>();
        while (_pending.TryDequeue(out var job, out var key))
        {
            if (!predicate(job))
            {
                retained.Add((job, key));
                continue;
            }

            RemoveDedupeUnsafe(job);
            job.Cancellation.Cancel();
            job.Completion.TrySetCanceled(job.Cancellation.Token);
            DiagnosticLogger.Write($"Worker queue cancelled; WebView=Worker; JobId={job.Descriptor.JobId}; JobType={job.Descriptor.JobType}; SubjectId={job.Descriptor.SubjectId}; RequestId={job.Descriptor.RequestId}; CancellationReason={reason}; CancelledAt={DateTimeOffset.UtcNow:O}");
            job.Cancellation.Dispose();
        }
        foreach (var item in retained) _pending.Enqueue(item.Job, item.Key);
    }

    private void SchedulePumpUnsafe()
    {
        if (_pumpScheduled || _disposed || _pausedForBrowserRecovery) return;
        _pumpScheduled = true;
        try
        {
            if (_owner.IsHandleCreated) _owner.BeginInvoke((Action)(async () => await PumpAsync().ConfigureAwait(true)));
            else _owner.HandleCreated += OwnerHandleCreated;
        }
        catch (InvalidOperationException)
        {
            _pumpScheduled = false;
        }
    }

    private void OwnerHandleCreated(object? sender, EventArgs e)
    {
        _owner.HandleCreated -= OwnerHandleCreated;
        lock (_gate)
        {
            _pumpScheduled = false;
            SchedulePumpUnsafe();
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            QueuedJob? job;
            lock (_gate)
            {
                if (_disposed || _pausedForBrowserRecovery || !_pending.TryDequeue(out job, out _))
                {
                    _pumpScheduled = false;
                    return;
                }
                _running = job;
            }

            var startedAt = DateTimeOffset.UtcNow;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            DiagnosticLogger.Write($"Worker queue started; WebView=Worker; JobId={job.Descriptor.JobId}; JobType={job.Descriptor.JobType}; Priority={job.Descriptor.Priority}; SubjectId={job.Descriptor.SubjectId}; RequestId={job.Descriptor.RequestId}; TargetUrl={job.Descriptor.TargetUrl}; StartedAt={startedAt:O}");
            var stopPumpAfterJob = false;
            try
            {
                var token = job.Descriptor.NonCancelableOnceStarted ? CancellationToken.None : job.Cancellation.Token;
                token.ThrowIfCancellationRequested();
                var result = await job.Work(token).ConfigureAwait(true);
                timer.Stop();
                job.Completion.TrySetResult(result);
                DiagnosticLogger.Write($"Worker queue completed; WebView=Worker; JobId={job.Descriptor.JobId}; JobType={job.Descriptor.JobType}; SubjectId={job.Descriptor.SubjectId}; RequestId={job.Descriptor.RequestId}; StartedAt={startedAt:O}; CompletedAt={DateTimeOffset.UtcNow:O}; TotalElapsedMs={timer.Elapsed.TotalMilliseconds:F0}");
            }
            catch (OperationCanceledException)
            {
                timer.Stop();
                job.Completion.TrySetCanceled(job.Cancellation.Token);
                DiagnosticLogger.Write($"Worker queue cancelled; WebView=Worker; JobId={job.Descriptor.JobId}; JobType={job.Descriptor.JobType}; SubjectId={job.Descriptor.SubjectId}; RequestId={job.Descriptor.RequestId}; CancellationReason=token-cancelled; CancelledAt={DateTimeOffset.UtcNow:O}; TotalElapsedMs={timer.Elapsed.TotalMilliseconds:F0}");
            }
            catch (Exception ex)
            {
                timer.Stop();
                job.Completion.TrySetException(ex);
                DiagnosticLogger.Write($"Worker queue failed; WebView=Worker; JobId={job.Descriptor.JobId}; JobType={job.Descriptor.JobType}; SubjectId={job.Descriptor.SubjectId}; RequestId={job.Descriptor.RequestId}; TotalElapsedMs={timer.Elapsed.TotalMilliseconds:F0}; Error={ex}");
            }
            finally
            {
                job.Cancellation.Dispose();
                lock (_gate)
                {
                    RemoveDedupeUnsafe(job);
                    _running = null;
                    if (_pausedForBrowserRecovery)
                    {
                        _pumpScheduled = false;
                        stopPumpAfterJob = true;
                    }
                }
            }

            if (stopPumpAfterJob)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelPendingUnsafe(_ => true, "queue-disposed");
            if (_running is { } running)
            {
                RemoveDedupeUnsafe(running);
                try { running.Cancellation.Cancel(); } catch { }
            }
        }
    }
}

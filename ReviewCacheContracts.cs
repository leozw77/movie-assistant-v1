using System.Threading;
using System.Threading.Tasks;

namespace QbPotDoubanAi;

/// <summary>
/// Minimal local-cache contract retained for the authoritative official-review write pipeline.
/// The current connector uses a deferred in-memory writer; the AI review layer will provide the
/// real local mirror writer when that layer is rebuilt.
/// </summary>
internal interface IOfficialReviewCacheWriter
{
    Task<bool> OverwriteFromOfficialAsync(
        string subjectId,
        string subjectUrl,
        OfficialReviewSnapshot official,
        CancellationToken cancellationToken);
}

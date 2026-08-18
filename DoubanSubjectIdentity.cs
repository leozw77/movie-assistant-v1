using System.Text.RegularExpressions;

namespace QbPotDoubanAi;

internal static class DoubanSubjectIdentity
{
    private static readonly Regex SubjectPath = new(@"/subject/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string ExtractSubjectId(string? url) => SubjectPath.Match(url ?? string.Empty).Groups[1].Value;

    internal static void Validate(string payloadSubjectId, string subjectUrl, string context, DoubanHistoryRecord? record = null)
    {
        var urlSubjectId = ExtractSubjectId(subjectUrl);
        if (string.IsNullOrWhiteSpace(payloadSubjectId) || !string.Equals(payloadSubjectId, urlSubjectId, StringComparison.Ordinal))
            Fail(context, payloadSubjectId, urlSubjectId, record, "Payload SubjectId 与 SubjectUrl 中的 ID 不一致");

        if (record is null) return;
        var recordUrlSubjectId = ExtractSubjectId(record.SubjectUrl);
        if (!string.IsNullOrWhiteSpace(record.SubjectId) && !string.Equals(record.SubjectId, payloadSubjectId, StringComparison.Ordinal))
            Fail(context, payloadSubjectId, urlSubjectId, record, "record.SubjectId 与请求 ID 不一致");
        if (!string.IsNullOrWhiteSpace(recordUrlSubjectId) && !string.Equals(recordUrlSubjectId, payloadSubjectId, StringComparison.Ordinal))
            Fail(context, payloadSubjectId, urlSubjectId, record, "record.SubjectUrl 中的 ID 与请求 ID 不一致");
    }

    private static void Fail(string context, string payloadSubjectId, string urlSubjectId, DoubanHistoryRecord? record, string reason)
    {
        DiagnosticLogger.Write($"Subject identity BLOCKED; Context={context}; Reason={reason}; PayloadSubjectId={payloadSubjectId}; SubjectUrlSubjectId={urlSubjectId}; RecordSubjectId={record?.SubjectId ?? ""}; RecordSubjectUrlSubjectId={ExtractSubjectId(record?.SubjectUrl)}; SubjectUrl={record?.SubjectUrl ?? ""}");
        throw new InvalidDataException("豆瓣影片身份校验失败，已阻止读取或写入。" + reason + "。");
    }
}

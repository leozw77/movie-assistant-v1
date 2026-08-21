namespace QbPotDoubanAi;

internal static class DoubanStatusMapper
{
    public static string ToFrodo(string status) => status switch
    {
        "collect" => "done",
        "wish" => "mark",
        "do" => "doing",
        _ => throw new InvalidDataException($"不支持的豆瓣个人状态：{status}")
    };

    public static string ToShell(string status) => status switch
    {
        "done" => "collect",
        "mark" => "wish",
        "doing" => "do",
        _ => throw new InvalidDataException($"不支持的 Frodo 个人状态：{status}")
    };

    public static string Label(string shellStatus) => shellStatus switch
    {
        "collect" => "看过",
        "wish" => "想看",
        "do" => "在看",
        _ => shellStatus
    };
}

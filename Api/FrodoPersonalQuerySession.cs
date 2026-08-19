namespace QbPotDoubanAi;

internal sealed class FrodoPersonalQuerySession
{
    private const int PageSize = 20;
    private readonly List<FrodoPersonalItem> _items = [];
    private int _cursor;

    internal string ProfileId { get; private set; } = "";
    internal string Status { get; private set; } = "";
    internal FrodoPersonalFilterCriteria Criteria { get; private set; } = new();
    internal int Total => _items.Count;
    internal int Shown => _cursor;
    internal bool HasMore => _cursor < _items.Count;
    internal bool IsActive => ProfileId.Length > 0 && Status.Length > 0;

    internal bool IsActiveFor(string profileId, string status) =>
        IsActive &&
        ProfileId.Equals(profileId, StringComparison.Ordinal) &&
        Status.Equals(status, StringComparison.Ordinal);

    internal void Start(
        string profileId,
        string status,
        FrodoPersonalFilterCriteria criteria,
        IReadOnlyList<FrodoPersonalItem> items)
    {
        Reset();
        ProfileId = profileId;
        Status = status;
        Criteria = criteria;
        _items.AddRange(items);
    }

    internal IReadOnlyList<FrodoPersonalItem> TakeInitial()
    {
        _cursor = 0;
        return TakeNext();
    }

    internal IReadOnlyList<FrodoPersonalItem> TakeNext()
    {
        if (_cursor >= _items.Count) return Array.Empty<FrodoPersonalItem>();
        var count = Math.Min(PageSize, _items.Count - _cursor);
        var page = _items.GetRange(_cursor, count);
        _cursor += count;
        return page;
    }

    internal void Reset()
    {
        ProfileId = "";
        Status = "";
        Criteria = new FrodoPersonalFilterCriteria();
        _items.Clear();
        _cursor = 0;
    }
}

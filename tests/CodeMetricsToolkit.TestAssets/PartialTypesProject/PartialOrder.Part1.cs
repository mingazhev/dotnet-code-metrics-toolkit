namespace PartialTypesProject;

public sealed partial class PartialOrder
{
    private readonly List<string> _events = new();

    public PartialOrder(string id)
    {
        Id = id;
        Record("created");
    }

    public string Id { get; }

    public IReadOnlyList<string> Events => _events;

    partial void OnStatusChanged(string status);

    public void MarkPaid()
    {
        Status = "paid";
        OnStatusChanged(Status);
    }

    private void Record(string eventName)
    {
        _events.Add($"{Id}:{eventName}");
    }
}

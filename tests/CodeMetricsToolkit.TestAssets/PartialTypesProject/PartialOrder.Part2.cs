namespace PartialTypesProject;

public sealed partial class PartialOrder
{
    public string Status { get; private set; } = "new";

    public decimal Total { get; private set; }

    public void AddLine(decimal amount)
    {
        Total += amount;
        Record("line-added");
    }

    partial void OnStatusChanged(string status)
    {
        Record($"status:{status}");
    }
}

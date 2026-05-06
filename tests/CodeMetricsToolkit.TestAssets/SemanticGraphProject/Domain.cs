namespace SemanticGraphProject;

public sealed record OrderRequest(string CustomerId, decimal Amount);

public sealed record Receipt(string OrderId, DateTimeOffset CreatedAt);

public sealed class Order
{
    public Order(string id, string customerId, decimal amount)
    {
        Id = id;
        CustomerId = customerId;
        Amount = amount;
    }

    public string Id { get; }

    public string CustomerId { get; }

    public decimal Amount { get; }
}

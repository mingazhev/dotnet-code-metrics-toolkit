namespace SemanticGraphProject;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IOrderRepository
{
    void Save(Order order);
}

public interface IOrderService
{
    Receipt Place(OrderRequest request);
}

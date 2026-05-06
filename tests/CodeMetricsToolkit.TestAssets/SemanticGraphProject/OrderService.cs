namespace SemanticGraphProject;

public abstract class ServiceBase
{
    protected static string CreateId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }
}

public sealed class OrderService : ServiceBase, IOrderService
{
    private readonly IClock _clock;
    private readonly IOrderRepository _repository;

    public OrderService(IClock clock, IOrderRepository repository)
    {
        _clock = clock;
        _repository = repository;
    }

    public Receipt Place(OrderRequest request)
    {
        Order order = CreateOrder(request);
        _repository.Save(order);

        return new Receipt(order.Id, _clock.UtcNow);
    }

    private static Order CreateOrder(OrderRequest request)
    {
        return new Order(CreateId("order"), request.CustomerId, request.Amount);
    }
}

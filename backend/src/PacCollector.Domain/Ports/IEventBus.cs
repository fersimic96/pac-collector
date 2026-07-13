namespace PacCollector.Domain.Ports;

public interface IEventBus
{
    void Publish(DomainEvent evt);
}

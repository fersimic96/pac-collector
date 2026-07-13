using System.Collections.Concurrent;
using System.Threading.Channels;
using PacCollector.Domain.Ports;

namespace PacCollector.Infrastructure.EventBus;

// implementacion del IEventBus para fanout a multiples consumidores (WebSocket).
// cada Subscribe() crea un Channel<DomainEvent> dedicado. Publish() escribe a todos.
// si un consumidor lee lento se descartan eventos viejos para no bloquear el productor.
public sealed class ChannelEventBus : IEventBus
{
    private const int PerSubscriberCapacity = 256;
    private readonly ConcurrentDictionary<Guid, Channel<DomainEvent>> _subscribers = new();

    public void Publish(DomainEvent evt)
    {
        foreach (var (_, ch) in _subscribers)
        {
            // DropOldest: si el canal esta lleno se tira el evento mas viejo
            ch.Writer.TryWrite(evt);
        }
    }

    public IDisposable Subscribe(out ChannelReader<DomainEvent> reader)
    {
        var ch = Channel.CreateBounded<DomainEvent>(new BoundedChannelOptions(PerSubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = ch;
        reader = ch.Reader;
        return new Subscription(this, id, ch);
    }

    public int SubscriberCount => _subscribers.Count;

    private sealed class Subscription : IDisposable
    {
        private readonly ChannelEventBus _owner;
        private readonly Guid _id;
        private readonly Channel<DomainEvent> _channel;
        private bool _disposed;

        public Subscription(ChannelEventBus owner, Guid id, Channel<DomainEvent> channel)
        {
            _owner = owner;
            _id = id;
            _channel = channel;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._subscribers.TryRemove(_id, out _);
            _channel.Writer.TryComplete();
        }
    }
}

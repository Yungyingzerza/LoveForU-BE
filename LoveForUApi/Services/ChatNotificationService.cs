using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Channels;

namespace LoveForUApi.Services;

public sealed record ChatNotification(Guid ThreadId, Guid MessageId, string SenderId, string Event = "message");

public interface IChatNotificationService
{
    ChatSubscription Subscribe(string userId);
    ValueTask PublishAsync(IEnumerable<string> userIds, ChatNotification notification, CancellationToken cancellationToken = default);
}

public sealed class ChatSubscription : IDisposable
{
    private readonly string _userId;
    private readonly Channel<ChatNotification> _channel;
    private readonly Action<string, Channel<ChatNotification>> _unsubscribe;
    private bool _disposed;

    internal ChatSubscription(string userId, Channel<ChatNotification> channel, Action<string, Channel<ChatNotification>> unsubscribe)
    {
        _userId = userId;
        _channel = channel;
        _unsubscribe = unsubscribe;
    }

    public ChannelReader<ChatNotification> Reader => _channel.Reader;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unsubscribe(_userId, _channel);
    }
}

internal sealed class ChatNotificationService : IChatNotificationService
{
    private readonly ConcurrentDictionary<string, List<Channel<ChatNotification>>> _connections = new(StringComparer.Ordinal);

    public ChatSubscription Subscribe(string userId)
    {
        var channel = Channel.CreateUnbounded<ChatNotification>();
        var channels = _connections.GetOrAdd(userId, _ => new List<Channel<ChatNotification>>());

        lock (channels)
        {
            channels.Add(channel);
        }

        return new ChatSubscription(userId, channel, RemoveSubscription);
    }

    public ValueTask PublishAsync(IEnumerable<string> userIds, ChatNotification notification, CancellationToken cancellationToken = default)
    {
        foreach (var userId in userIds.Distinct(StringComparer.Ordinal))
        {
            if (!_connections.TryGetValue(userId, out var channels))
            {
                continue;
            }

            Channel<ChatNotification>[] snapshot;
            lock (channels)
            {
                snapshot = channels.ToArray();
            }

            foreach (var channel in snapshot)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled(cancellationToken);
                }

                if (!channel.Writer.TryWrite(notification))
                {
                    RemoveSubscription(userId, channel);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private void RemoveSubscription(string userId, Channel<ChatNotification> channel)
    {
        if (_connections.TryGetValue(userId, out var channels))
        {
            lock (channels)
            {
                channels.Remove(channel);
                if (channels.Count == 0)
                {
                    _connections.TryRemove(userId, out _);
                }
            }
        }

        channel.Writer.TryComplete();
    }
}

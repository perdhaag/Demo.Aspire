using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Demo.Aspire.Contracts;
using StackExchange.Redis;

namespace Demo.Aspire.Gateway;

/// <summary>
/// The one reader of the bus tape every other service writes to (see
/// <c>Demo.Aspire.Platform.Messaging.BusTap</c>). This lives in the gateway rather than
/// referencing Platform, because Platform pulls in EF Core and MassTransit for the
/// services that actually run the choreography &mdash; dependencies a reverse proxy has
/// no other reason to carry. <see cref="BusTapEntry"/> and the Redis key names are
/// shared through <c>Demo.Aspire.Contracts</c> instead, which both sides already
/// reference.
/// </summary>
internal static class BusTapeReader
{
    /// <summary>Reads the capped history, oldest first, for a client that just connected.</summary>
    public static async Task<IReadOnlyList<BusTapEntry>> ReadRecentAsync(
        IConnectionMultiplexer redis,
        int take,
        CancellationToken cancellationToken)
    {
        var entries = await redis.GetDatabase()
            .ListRangeAsync(BusTapKeys.FeedKey, 0, Math.Clamp(take, 1, 200) - 1);

        List<BusTapEntry> parsed =
        [
            .. entries
                .Select(value => JsonSerializer.Deserialize<BusTapEntry>((string)value!, JsonSerializerOptions.Web))
                .OfType<BusTapEntry>(),
        ];

        parsed.Reverse();
        return parsed;
    }

    /// <summary>
    /// Bridges the Redis pub/sub channel every service publishes new entries on into an
    /// async stream, for <c>TypedResults.ServerSentEvents</c> to forward to the browser.
    /// </summary>
    public static async IAsyncEnumerable<BusTapEntry> StreamAsync(
        IConnectionMultiplexer redis,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<BusTapEntry>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var subscriber = redis.GetSubscriber();

        void OnMessage(RedisChannel _, RedisValue message)
        {
            var entry = JsonSerializer.Deserialize<BusTapEntry>((string)message!, JsonSerializerOptions.Web);

            if (entry is not null)
            {
                channel.Writer.TryWrite(entry);
            }
        }

        await subscriber.SubscribeAsync(RedisChannel.Literal(BusTapKeys.ChannelName), OnMessage);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }
        finally
        {
            await subscriber.UnsubscribeAsync(RedisChannel.Literal(BusTapKeys.ChannelName), OnMessage);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Aspire.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Demo.Aspire.Platform.Messaging;

[JsonSerializable(typeof(BusTapEntry))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BusTapJsonContext : JsonSerializerContext;

/// <summary>
/// Tees every message this service publishes or consumes onto a capped Redis list and a
/// live pub/sub channel, so the gateway can stream the choreography to the browser as
/// it happens (see <c>GET /api/events</c> in <c>Demo.Aspire.Gateway</c>, which is the
/// only reader) instead of the page inferring it from polling a read model.
/// </summary>
/// <remarks>
/// The tape describes the flow; it must never become part of it. Every write here
/// swallows its own exception, so a Redis hiccup can degrade the demo's rail to nothing
/// but can never fail, delay, or retry the business message it is reporting on.
/// </remarks>
public sealed class BusTap(IConnectionMultiplexer redis, ILogger<BusTap> logger)
{
    private const int FeedLength = 200;

    public async Task RecordAsync(BusTapEntry entry)
    {
        try
        {
            var payload = JsonSerializer.Serialize(entry, BusTapJsonContext.Default.BusTapEntry);
            var database = redis.GetDatabase();

            await database.ListLeftPushAsync(BusTapKeys.FeedKey, payload);
            await database.ListTrimAsync(BusTapKeys.FeedKey, 0, FeedLength - 1);
            await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(BusTapKeys.ChannelName), payload);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not record a bus tape entry for {Event}.", entry.Event);
        }
    }
}

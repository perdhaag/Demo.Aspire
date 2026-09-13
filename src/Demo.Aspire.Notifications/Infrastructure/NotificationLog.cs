using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace Demo.Aspire.Notifications.Infrastructure;

public sealed record NotificationEntry(
    Guid MessageId,
    Guid BookingId,
    string Recipient,
    string Subject,
    string Outcome,
    DateTimeOffset SentAtUtc);

[JsonSerializable(typeof(NotificationEntry))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class NotificationJsonContext : JsonSerializerContext;

/// <summary>
/// This service has no database, which makes Redis do two jobs that would otherwise be
/// the database's. It is the de-duplication ledger that keeps a redelivered message from
/// sending a second e-mail, and it is the capped feed the dashboard reads.
/// </summary>
public sealed class NotificationLog(IConnectionMultiplexer redis, TimeProvider clock)
{
    private const string FeedKey = "notifications:recent";

    private const int FeedLength = 100;

    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromHours(6);

    /// <summary>
    /// Claims the right to send one message. <c>SET NX</c> is atomic across every
    /// instance of this service, so exactly one consumer wins a redelivery race.
    /// </summary>
    public Task<bool> TryClaimAsync(Guid messageId) =>
        redis.GetDatabase().StringSetAsync(
            $"notifications:sent:{messageId}",
            clock.GetUtcNow().ToString("O"),
            DeduplicationWindow,
            When.NotExists);

    /// <summary>Releases a claim so a failed send can be retried by the broker.</summary>
    public Task ReleaseClaimAsync(Guid messageId) =>
        redis.GetDatabase().KeyDeleteAsync($"notifications:sent:{messageId}");

    public async Task RecordAsync(NotificationEntry entry)
    {
        var database = redis.GetDatabase();
        var payload = JsonSerializer.Serialize(entry, NotificationJsonContext.Default.NotificationEntry);

        await database.ListLeftPushAsync(FeedKey, payload);
        await database.ListTrimAsync(FeedKey, 0, FeedLength - 1);
    }

    public async Task<IReadOnlyList<NotificationEntry>> ReadRecentAsync(int take)
    {
        var entries = await redis.GetDatabase().ListRangeAsync(FeedKey, 0, Math.Clamp(take, 1, FeedLength) - 1);

        return
        [
            .. entries
                .Select(value => JsonSerializer.Deserialize((string)value!, NotificationJsonContext.Default.NotificationEntry))
                .OfType<NotificationEntry>(),
        ];
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Demo.Aspire.Contracts;
using MassTransit;

namespace Demo.Aspire.Platform.Messaging;

/// <summary>
/// Writes a "Published" row for every integration event that actually leaves this service.
///
/// A send observer rather than a publish one, and that is not an implementation detail: with
/// <c>UseBusOutbox()</c> a <c>Publish</c> never reaches the broker: it writes a row in the
/// same transaction as the aggregate change, and the delivery service moves that row onto
/// RabbitMQ afterwards with a send. <see cref="IPublishObserver"/> therefore never fires for
/// anything in this system, which is why the tape used to carry consumes only and the trace
/// waterfall &mdash; which needs a publish to measure a hop from &mdash; drew nothing at all.
///
/// The row is dated <c>OccurredAtUtc</c>, when the service decided, not when the outbox got
/// round to it. That keeps every timestamp on the page measured from the same thing, and it
/// means the hairline above the waterfall's lanes covers the whole wait from one service
/// deciding to the next one acting &mdash; the outbox delay included, because it is real.
/// </summary>
internal sealed class BusTapSendObserver(BusTap tap, string serviceName) : ISendObserver
{
    /// <summary>The URN prefix MassTransit gives every type in the published language.</summary>
    private static readonly string ContractsUrn = $"urn:message:{typeof(IIntegrationEvent).Namespace}:";

    public Task PreSend<T>(SendContext<T> context) where T : class => Task.CompletedTask;

    public Task PostSend<T>(SendContext<T> context) where T : class
    {
        // A service publishing without an outbox in front of it still has its own record in
        // hand. Nothing in this demo does — Notifications is the only database-less context
        // and it only consumes — but it is the exact case, so it goes first.
        if (context.Message is IIntegrationEvent integrationEvent)
        {
            return tap.RecordAsync(new BusTapEntry(
                context.MessageId ?? Guid.Empty,
                integrationEvent.CorrelationId,
                typeof(T).Name,
                serviceName,
                BusTapKind.Published,
                integrationEvent.OccurredAtUtc,
                DurationMs: null,
                Activity.Current?.TraceId.ToString(),
                Detail: null));
        }

        var urn = context.SupportedMessageTypes
            .FirstOrDefault(type => type.StartsWith(ContractsUrn, StringComparison.Ordinal));

        // Everything MassTransit sends for its own purposes goes past here too, and none of
        // it is part of the story the tape tells.
        return urn is null
            ? Task.CompletedTask
            : tap.RecordAsync(Describe(context, urn[ContractsUrn.Length..]));
    }

    public Task SendFault<T>(SendContext<T> context, Exception exception) where T : class =>
        Task.CompletedTask;

    /// <summary>
    /// Reads the envelope the outbox is about to put on the wire, because by this point the
    /// record itself is gone: the delivery service re-sends a stored body, and
    /// <c>context.Message</c> is a marker type carrying nothing at all. The two fields worth
    /// recovering are the ones every integration event carries — which booking this belongs
    /// to, and when the service decided it.
    /// </summary>
    private BusTapEntry Describe<T>(SendContext<T> context, string eventName)
        where T : class
    {
        var correlationId = Guid.Empty;
        var occurredAtUtc = context.SentTime ?? DateTimeOffset.UtcNow;

        try
        {
            using var envelope = JsonDocument.Parse(context.Serializer.GetMessageBody(context).GetString());

            if (envelope.RootElement.TryGetProperty("message", out var message))
            {
                if (Read(message, "correlationId") is { } correlation)
                {
                    Guid.TryParse(correlation, out correlationId);
                }

                if (Read(message, "occurredAtUtc") is { } occurred
                    && DateTimeOffset.TryParse(occurred, CultureInfo.InvariantCulture, out var at))
                {
                    occurredAtUtc = at;
                }
            }
        }
        catch (JsonException)
        {
            // The tape is a demo aid and never load-bearing: a body we cannot read costs one
            // row its correlation, not a message its delivery.
        }

        return new BusTapEntry(
            context.MessageId ?? Guid.Empty,
            correlationId,
            eventName,
            serviceName,
            BusTapKind.Published,
            occurredAtUtc,
            DurationMs: null,
            Activity.Current?.TraceId.ToString(),
            Detail: null);
    }

    private static string? Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// The same row for a service that publishes without an outbox in front of it. Nothing in
/// the demo does today &mdash; Notifications is the only database-less context and it only
/// consumes &mdash; but a publish that skipped the outbox would otherwise leave the tape
/// silent, which is the failure this pair exists to make impossible.
/// </summary>
internal sealed class BusTapPublishObserver(BusTap tap, string serviceName) : IPublishObserver
{
    public Task PrePublish<T>(PublishContext<T> context) where T : class => Task.CompletedTask;

    public Task PostPublish<T>(PublishContext<T> context) where T : class
    {
        if (context.Message is not IIntegrationEvent integrationEvent)
        {
            return Task.CompletedTask;
        }

        return tap.RecordAsync(new BusTapEntry(
            context.MessageId ?? Guid.Empty,
            integrationEvent.CorrelationId,
            typeof(T).Name,
            serviceName,
            BusTapKind.Published,
            integrationEvent.OccurredAtUtc,
            DurationMs: null,
            Activity.Current?.TraceId.ToString(),
            Detail: null));
    }

    public Task PublishFault<T>(PublishContext<T> context, Exception exception) where T : class => Task.CompletedTask;
}

/// <summary>Writes a "Consumed" or "Faulted" row for every integration event this service handles.</summary>
internal sealed class BusTapConsumeObserver(BusTap tap, string serviceName, TimeProvider clock) : IConsumeObserver
{
    public Task PreConsume<T>(ConsumeContext<T> context) where T : class => Task.CompletedTask;

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        if (context.Message is not IIntegrationEvent integrationEvent)
        {
            return Task.CompletedTask;
        }

        return tap.RecordAsync(new BusTapEntry(
            context.MessageId ?? Guid.Empty,
            integrationEvent.CorrelationId,
            typeof(T).Name,
            serviceName,
            BusTapKind.Consumed,
            clock.GetUtcNow(),
            context.ReceiveContext.ElapsedTime.TotalMilliseconds,
            Activity.Current?.TraceId.ToString(),
            Detail: null));
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        if (context.Message is not IIntegrationEvent integrationEvent)
        {
            return Task.CompletedTask;
        }

        var attempt = context.GetRetryAttempt();

        return tap.RecordAsync(new BusTapEntry(
            context.MessageId ?? Guid.Empty,
            integrationEvent.CorrelationId,
            typeof(T).Name,
            serviceName,
            BusTapKind.Faulted,
            clock.GetUtcNow(),
            context.ReceiveContext.ElapsedTime.TotalMilliseconds,
            Activity.Current?.TraceId.ToString(),
            $"{exception.GetType().Name}: {exception.Message} (attempt {attempt + 1})"));
    }
}

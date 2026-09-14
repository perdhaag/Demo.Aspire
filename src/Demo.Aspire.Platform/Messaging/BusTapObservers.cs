using System.Diagnostics;
using Demo.Aspire.Contracts;
using MassTransit;

namespace Demo.Aspire.Platform.Messaging;

/// <summary>Writes a "Published" row for every integration event this service publishes.</summary>
internal sealed class BusTapPublishObserver(BusTap tap, string serviceName) : IPublishObserver
{
    public Task PrePublish<T>(PublishContext<T> context) where T : class => Task.CompletedTask;

    public Task PostPublish<T>(PublishContext<T> context) where T : class
    {
        // Only the published language is worth a row — MassTransit's own protocol
        // messages (fault reports, and so on) would just be noise on the tape.
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

        // MassTransit retries in-process before this ever reaches the broker's error
        // queue (see MessagingExtensions.UseMessageRetry), so a faulted row followed a
        // few seconds later by a successful "Consumed" row is the retry working, not a
        // second attempt going unnoticed.
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

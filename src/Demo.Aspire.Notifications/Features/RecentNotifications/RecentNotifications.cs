using Demo.Aspire.Notifications.Infrastructure;
using Demo.Aspire.Platform.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Notifications.Features.RecentNotifications;

public sealed class RecentNotificationsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes
        .MapGet("/notifications", async (NotificationLog log, int take = 25) =>
            TypedResults.Ok(await log.ReadRecentAsync(take)))
        .WithName("ListRecentNotifications")
        .WithSummary("Reads the capped feed of sent mail straight out of Redis.")
        .WithTags("Notifications");
}

using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Platform.Endpoints;

/// <summary>
/// One HTTP entry point into one vertical slice. Each feature folder owns its own
/// implementation, so adding a feature never means editing a shared startup file.
/// </summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder routes);
}

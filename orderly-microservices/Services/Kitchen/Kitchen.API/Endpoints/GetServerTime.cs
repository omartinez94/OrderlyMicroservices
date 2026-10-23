namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>GET /v1/kitchen/time</c> — returns current UTC server time.
/// Used by client-side elapsed timers to measure and eliminate clock skew.
/// </summary>
public class GetServerTime : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/kitchen/time", () =>
        {
            Instant now = SystemClock.Instance.GetCurrentInstant();
            return Results.Ok(new ServerTimeResponse(now, now.ToUnixTimeMilliseconds()));
        })
        .WithTags("Kitchen")
        .AllowAnonymous();
    }
}

public record ServerTimeResponse(Instant ServerTime, long UnixEpochMilliseconds);

namespace E2E.Tests.Abstractions;

/// <summary>
/// Per-service base URL. The E2E docker-compose override exposes
/// every service on its own published port so a failing test points
/// at one container rather than blurred through the gateway. The
/// gateway-port variant is intentionally not exposed — the
/// per-service choice is what the production-readiness audit
/// recommended for cross-service flows.
/// </summary>
public static class ServiceEndpoints
{
    /// <summary>Catalog.API base URL.</summary>
    public const string Catalog = "http://localhost:5100";

    /// <summary>Basket.API base URL.</summary>
    public const string Basket = "http://localhost:5200";

    /// <summary>Discount.Grpc HTTP/2 base URL.</summary>
    public const string Discount = "http://localhost:5300";

    /// <summary>Ordering.API base URL.</summary>
    public const string Ordering = "http://localhost:5400";

    /// <summary>Kitchen.API base URL.</summary>
    public const string Kitchen = "http://localhost:5500";

    /// <summary>Identity.API base URL.</summary>
    public const string Identity = "http://localhost:5600";

    /// <summary>YarpApiGateway base URL.</summary>
    public const string Gateway = "http://localhost:5000";
}

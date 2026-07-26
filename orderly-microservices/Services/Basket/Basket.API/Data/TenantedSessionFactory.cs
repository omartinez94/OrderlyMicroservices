using System.Security.Claims;
using Marten.Services;

namespace Basket.API.Data;

/// <summary>
/// Custom Marten <see cref="ISessionFactory"/> that sets the
/// <c>TenantId</c> on every opened <see cref="SessionOptions"/> from
/// the ambient <see cref="ICurrentRestaurantProvider"/>. Without this,
/// the scoped <see cref="IDocumentSession"/> opened by request handlers
/// would carry no tenant — Marten's conjoined <c>MultiTenanted()</c>
/// global query filter would then either (a) match no rows (the
/// default-tenant behaviour) or (b) throw if a non-empty
/// <c>tenantId</c> is required by the filter.
/// </summary>
/// <remarks>
/// <para>
/// The plan said this registration was in place;
/// the source never had it. The unit tests passed because
/// they substitute <c>IDocumentSession</c> with NSubstitute mocks —
/// the production wiring was never exercised until
/// <c>GetCart_WithCart_Returns200AndBody</c> test seeded a basket
/// via the explicit-tenant session
/// (<c>IDocumentStore.LightweightSession(TestRestaurantId)</c>) and
/// then attempted to read it through the default session, finding
/// nothing.
/// </para>
/// <para>
/// The factory pulls the tenant id from
/// <see cref="ICurrentRestaurantProvider"/>, which reads the
/// <c>restaurantId</c> claim from <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/>
/// (via <see cref="IHttpContextAccessor"/>). Inside an HTTP request
/// scope the provider returns the request's tenant; outside a
/// request (background services, the design-time tooling) it returns
/// <see cref="Guid.Empty"/>, which Marten treats as the
/// <c>*DEFAULT*</c> tenant — appropriate for cross-tenant work that
/// must be opt-in (e.g. the admin endpoints' bypass).
/// </para>
/// </remarks>
public sealed class TenantedSessionFactory(IDocumentStore store, IHttpContextAccessor httpContextAccessor) : ISessionFactory
{
    /// <inheritdoc />
    public IDocumentSession OpenSession(DocumentStore documentStore)
    {
        var tenantId = ResolveTenantId();
        Marten.Services.SessionOptions options = new();
        if (tenantId != Guid.Empty)
        {
            options.TenantId = tenantId.ToString();
        }
        return documentStore.OpenSession(options);
    }

    /// <inheritdoc />
    public IDocumentSession OpenSession()
    {
        var tenantId = ResolveTenantId();
        if (tenantId == Guid.Empty) return store.LightweightSession();
        return store.LightweightSession(tenantId.ToString());
    }

    /// <inheritdoc />
    public IQuerySession QuerySession()
    {
        var tenantId = ResolveTenantId();
        if (tenantId == Guid.Empty) return store.QuerySession();
        return store.QuerySession(tenantId.ToString());
    }

    private Guid ResolveTenantId()
    {
        // The ICurrentRestaurantProvider would normally be the
        // canonical source, but at the time `ISessionFactory` is
        // invoked the DI scope is the host scope, not the per-request
        // scope. Read the HttpContext directly — the IHttpContextAccessor
        // surfaces the per-request User claims reliably when the host
        // is processing a request.
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return Guid.Empty;

        var raw = user.FindFirstValue("restaurantId");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}

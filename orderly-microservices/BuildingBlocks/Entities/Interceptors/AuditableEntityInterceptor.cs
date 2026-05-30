using BuildingBlocks.Entities.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NodaTime;
using System.Security.Claims;

namespace BuildingBlocks.Entities.Interceptors;

public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context is null) return;

        var userId = GetUserId(context);
        var timestamp = SystemClock.Instance.GetCurrentInstant();

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedFrom(userId, timestamp);
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedFrom(userId, timestamp);
            }
        }
    }

    private static string GetUserId(DbContext context)
    {
        var httpContextAccessor = context.GetService<IHttpContextAccessor>();
        var httpContext = httpContextAccessor?.HttpContext;

        return httpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
    }
}

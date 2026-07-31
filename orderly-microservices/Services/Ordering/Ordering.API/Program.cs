using BuildingBlocks.Dev;
using Ordering.API;
using Ordering.Application;
using Ordering.Infrastructure;
using Ordering.Infrastructure.Data.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
//------------------------------
// Infrastructure - EF Core
// Application - MediatR
// API - Carter, HealthChecks, ...
//------------------------------
builder.Services
    .AddApplicationServices(builder.Configuration)
    .AddInfrastructureServices(builder.Configuration)
    .AddApiServices(builder.Environment, builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseApiServices();

if(app.Environment.IsDevelopment())
{
    await app.InitializeDatabaseAsync();

    // Dev-only trigger endpoints for the Orderly.DevMCP.Server's
    // `trigger_scheduled_jobs` tool. Gated on IsDevelopment() +
    // X-Dev-Trigger-Secret header (constant-time compared against
    // DEV_TRIGGER_SECRET env var).
    app.MapDevTriggerEndpoint(
        "/_dev/trigger/daily-reconciliation",
        async (Ordering.Infrastructure.IDailyReconciliationRunner runner, HttpContext ctx) =>
        {
            if (!await DevTriggerEndpointExtensions.ValidateSecretAsync(ctx, ctx.RequestAborted))
            {
                return Results.Empty;
            }
            var reconciled = await runner.RunAsync(ctx.RequestAborted);
            return Results.Ok(new { reconciledCount = reconciled });
        });

    app.MapDevTriggerEndpoint(
        "/_dev/trigger/outbox-relay",
        async (Ordering.Infrastructure.IOrderingOutboxRunner runner, HttpContext ctx) =>
        {
            if (!await DevTriggerEndpointExtensions.ValidateSecretAsync(ctx, ctx.RequestAborted))
            {
                return Results.Empty;
            }
            var dispatched = await runner.DispatchOnceAsync(ctx.RequestAborted);
            return Results.Ok(new { dispatchedCount = dispatched });
        });
}

app.Run();

using BuildingBlocks.Authorization;
using BuildingBlocks.Entities.Interceptors;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Multitenancy;
using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.Outbox;
using Discount.Grpc.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// JWT bearer against Identity authority; per-method permission policies evaluated
// by DiscountAuthorizationInterceptor (gRPC's [Authorize(Policy=...)] is silently ignored).
builder.Services.AddJwtAuthentication(
    authority: builder.Configuration["Jwt:Authority"] ?? "https://localhost:5057",
    audience: builder.Configuration["Jwt:Audience"] ?? "OrderlyMicroservices");
builder.Services.AddDiscountPolicies();

builder.Services.AddGrpc(o => o.Interceptors.Add<DiscountAuthorizationInterceptor>());

builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

// Tenant scoping: IHttpContextAccessor feeds ClaimsRestaurantProvider which
// supplies the global query filter's per-request tenant GUID.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICurrentRestaurantProvider, ClaimsRestaurantProvider>();

builder.Services.AddDbContext<DiscountContext>((sp, options) =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Database"))
        .AddInterceptors(new AuditableEntityInterceptor()));

// Outbox: bind options, configure MassTransit with an in-memory bus (Phase 1
// dev transport — RabbitMQ wiring is the Phase 4 cross-service hand-off), and
// register the scoped publisher + the dispatcher as a hosted service.
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.AddMassTransit(o =>
{
    o.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx));
});
builder.Services.AddScoped<IOutboxPublisher, DiscountOutboxPublisher>();
builder.Services.AddHostedService<DiscountOutboxDispatcher>();

// Expiry sweep — soft-deletes coupons whose ExpirationDate has passed.
builder.Services.Configure<DiscountExpirySweepOptions>(
    builder.Configuration.GetSection(DiscountExpirySweepOptions.SectionName));
builder.Services.AddHostedService<DiscountExpirySweepService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMigration();

app.MapGrpcService<DiscountService>();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();

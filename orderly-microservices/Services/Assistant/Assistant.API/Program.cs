using Assistant.API.Infrastructure.Persistence;
using BuildingBlocks.Dev;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.FeatureManagement;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrderlyOpenTelemetry(builder.Configuration, "Orderly.Assistant");
builder.Logging.AddOrderlyOpenTelemetry(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddJwtAuthenticationWithDevFallback(
    builder.Environment,
    builder.Configuration,
    authority: builder.Configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
    audience: "OrderlyMicroservices");

builder.Services.AddAuthorizationServices();
builder.Services.AddCarter();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentRestaurantProvider, ClaimsRestaurantProvider>();

builder.Services.AddDbContext<AiDbContext>((sp, options) =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("AssistantDB") ?? "Host=localhost;Database=AssistantDb;Username=postgres;Password=postgres",
        npgsqlOptions => npgsqlOptions
            .UseNodaTime()
            .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null));
});

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.AddScoped<IOutboxPublisher, AiOutboxPublisher>();
builder.Services.AddScoped<AiOutboxPublisher>();

builder.Services.AddExceptionHandler<CustomExceptionHandler>();
builder.Services.AddFeatureManagement();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AiDbContext>(name: "assistantdb", tags: ["db", "ready"]);

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapCarter();
app.MapOpenApi();
app.UseExceptionHandler(options => { });

app.MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();

public partial class Program
{
}

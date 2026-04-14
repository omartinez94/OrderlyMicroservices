var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCarter();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Setting this to null makes it use the exact C# property names (PascalCase)
    options.SerializerOptions.PropertyNamingPolicy = null;
    
    // Configure System.Text.Json to properly understand NodaTime types!
    options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
});
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBevavior<,>));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
var connectionString = builder.Configuration.GetConnectionString("BasketDB")!;
builder.Services.AddMarten(opt =>
{
    opt.Connection(connectionString);
    opt.CreateDatabasesForTenants(c =>
    {
        var maintenanceDbStr = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        // Specify a db to which to connect in case database needs to be created.
        c.MaintenanceDatabase(maintenanceDbStr);
        c.ForTenant()
            .CheckAgainstPgDatabase();
    });
})
    .ApplyAllDatabaseChangesOnStartup()
    .UseLightweightSessions();

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

builder.Services.AddExceptionHandler<CustomExceptionHandler>();

builder.Services.AddScoped<IBasketRepository, BasketRepository>();
/* Applies decorator pattern using Scrutor. Native DI equivalent:
 * builder.Services.AddScoped<BasketRepository>();
 * builder.Services.AddScoped<IBasketRepository>(p => 
 *     new CachedBasketRepository(
 *         p.GetRequiredService<BasketRepository>(), 
 *         p.GetRequiredService<IDistributedCache>()
 *     )
 * );
*/
builder.Services.Decorate<IBasketRepository, CachedBasketRepository>();

builder.Services.AddStackExchangeRedisCache(rediscache =>
{
    rediscache.Configuration = builder.Configuration.GetConnectionString("Redis")!;
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("BasketDB")!)
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapCarter();

app.UseExceptionHandler(options => { });

app.Run();

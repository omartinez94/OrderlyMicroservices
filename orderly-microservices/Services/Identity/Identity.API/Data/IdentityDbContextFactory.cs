namespace Identity.API.Data;

public class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5435;Database=Identitydb;Username=postgres;Password=postgres");
        optionsBuilder.UseOpenIddict();

        return new IdentityDbContext(optionsBuilder.Options);
    }
}

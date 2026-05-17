namespace Identity.API.Extensions;

public class JwtSettings
{
    public const string SectionName = "Jwt";
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}

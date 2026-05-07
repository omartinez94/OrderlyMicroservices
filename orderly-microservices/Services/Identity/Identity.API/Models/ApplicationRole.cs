namespace Identity.API.Models;

public class ApplicationRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
}

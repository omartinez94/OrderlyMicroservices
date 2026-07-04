namespace Kitchen.API.Application.Abstractions;

/// <summary>
/// Transaction boundary. The Kitchen domain uses the EF Core <c>DbContext</c>
/// directly under the hood; <c>IUnitOfWork</c> exists so the application layer
/// never imports <c>Microsoft.EntityFrameworkCore</c> infrastructure types.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
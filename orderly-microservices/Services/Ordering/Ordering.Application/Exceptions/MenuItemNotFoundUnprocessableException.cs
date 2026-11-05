namespace Ordering.Application.Exceptions;

using Ordering.Domain.Exceptions;

public class MenuItemNotFoundUnprocessableException : DomainException
{
    public IReadOnlyList<Guid> MissingItemIds { get; }

    public MenuItemNotFoundUnprocessableException(IReadOnlyList<Guid> missingItemIds)
        : base($"Menu items not found or do not belong to restaurant: {string.Join(", ", missingItemIds)}", "MenuItemId")
    {
        MissingItemIds = missingItemIds;
    }
}
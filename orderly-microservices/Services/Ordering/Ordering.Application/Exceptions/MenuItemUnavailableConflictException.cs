namespace Ordering.Application.Exceptions;

using Ordering.Domain.Exceptions;

public class MenuItemUnavailableConflictException : DomainException
{
    public IReadOnlyList<Guid> UnavailableItemIds { get; }

    public MenuItemUnavailableConflictException(IReadOnlyList<Guid> unavailableItemIds)
        : base($"One or more items are unavailable: {string.Join(", ", unavailableItemIds)}", "MenuItemId")
    {
        UnavailableItemIds = unavailableItemIds;
    }
}
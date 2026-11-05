namespace Ordering.Application.Exceptions;

using Ordering.Domain.Exceptions;

public class TableNotFoundUnprocessableException : DomainException
{
    public Guid TableId { get; }

    public TableNotFoundUnprocessableException(Guid tableId)
        : base($"Table '{tableId}' was not found or does not belong to the restaurant.", "TableId")
    {
        TableId = tableId;
    }
}
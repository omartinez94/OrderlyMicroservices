namespace Ordering.Application.Exceptions;

using Ordering.Domain.Exceptions;

public class SeatNumberExceedsCapacityUnprocessableException : DomainException
{
    public Guid TableId { get; }
    public int SeatNumber { get; }
    public int Capacity { get; }

    public SeatNumberExceedsCapacityUnprocessableException(Guid tableId, int seatNumber, int capacity)
        : base($"Seat number {seatNumber} exceeds table '{tableId}' capacity of {capacity}.", "SeatNumber")
    {
        TableId = tableId;
        SeatNumber = seatNumber;
        Capacity = capacity;
    }
}
namespace BuildingBlocks.Exceptions;

public class BadRequestException : Exception
{
    public string? Description { get; }

    public BadRequestException(string message) : base(message)
    {

    }
    public BadRequestException(string message, string description) : base(message)
    {
        Description = description;
    }
}

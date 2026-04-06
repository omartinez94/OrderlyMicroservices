namespace BuildingBlocks.Exceptions;

public class InternalServerException : Exception
{
    public string? Description { get; }

    public InternalServerException(string message) : base(message)
    {
        
    }

    public InternalServerException(string message, string description) : base(message)
    {
        Description = description;
    }
}

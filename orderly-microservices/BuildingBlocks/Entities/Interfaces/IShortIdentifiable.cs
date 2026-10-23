namespace BuildingBlocks.Entities.Interfaces;

/// <summary>
/// Contract for entities exposing an external compact typed short identifier (e.g. rstk8j3x9mq).
/// </summary>
public interface IShortIdentifiable
{
    string ShortId { get; set; }
}

namespace BuildingBlocks.Entities.Interfaces;

public interface IDeletableEntity
{
    bool IsActive { get; }
    void Delete();
    void Undelete();
}

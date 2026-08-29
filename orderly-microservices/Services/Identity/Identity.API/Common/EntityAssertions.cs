using BuildingBlocks.Exceptions;

namespace Identity.API.Common;

/// <summary>
/// Guard helpers for the repetitive
/// <c>if (entity is null) throw new NotFoundException(name, key);</c>
/// pattern that shows up at the head of every read/update/delete
/// handler. The two "already exists" <see cref="BadRequestException"/>
/// guards (CreateUser, CreateRole) stay as plain <c>if (existing is not
/// null) throw …;</c> — the semantic is inverted, so they don't fit
/// <see cref="MustExist{T}"/>.
/// </summary>
public static class EntityAssertions
{
    /// <summary>
    /// Returns the entity unchanged if non-null, otherwise throws
    /// <see cref="NotFoundException"/>. Use at the top of a handler
    /// immediately after the load step:
    /// <code>
    /// ApplicationUser user = (await dbContext.Users.FindAsync([id], ct))
    ///     .MustExist("User", id);
    /// </code>
    /// </summary>
    public static T MustExist<T>(this T? entity, string entityName, object key)
        where T : class
    {
        return entity ?? throw new NotFoundException(entityName, key);
    }
}

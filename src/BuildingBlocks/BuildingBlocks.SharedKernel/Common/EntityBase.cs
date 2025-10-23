namespace BuildingBlocks.SharedKernel.Common;

/// <summary>
/// Base entity interface
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IEntityBase<T>
{
    T Id { get; set; }
}

/// <summary>
/// Auditable entity interface
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}

/// <summary>
/// Soft delete interface
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}

/// <summary>
/// Aggregate root interface
/// </summary>
public interface IAggregateRoot
{
}

/// <summary>
/// Base entity class
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class EntityBase<T> : IEntityBase<T>
    where T : IEquatable<T>
{
    public required T Id  { get; set; }
}

/// <summary>
/// Base entity aggregate class
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class EntityAggregateBase<T> : IEntityBase<T>, IAggregateRoot
    where T : IEquatable<T>
{
    public required T Id { get; set; }
}

public static class  EntityExtensions
{
    public static string? GetEntityIdName(this Type type)
    {
        if (!type.IsSubclassOf(typeof(EntityBase<>))) return null;
        var idProperty = type.GetProperty("Id");
        if (idProperty == null) return null;
        var prefix = type.Name.Replace("Entity", "");
        return prefix + idProperty.Name;
    }
}
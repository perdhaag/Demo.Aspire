namespace Demo.Aspire.SharedKernel;

/// <summary>
/// An object with a thread of identity and continuity. Two entities are the same
/// entity when their identifiers match, regardless of their attribute values.
/// </summary>
/// <typeparam name="TId">A strongly typed identifier, normally a readonly record struct.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct, IEquatable<TId>
{
    protected Entity(TId id) => Id = id;

    /// <summary>Required by EF Core materialisation only.</summary>
    protected Entity()
    {
    }

    public TId Id { get; protected set; }

    public bool Equals(Entity<TId>? other) =>
        other is not null && other.GetType() == GetType() && other.Id.Equals(Id);

    public override bool Equals(object? obj) => obj is Entity<TId> entity && Equals(entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}

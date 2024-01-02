using System.Diagnostics;
using Cosmodust.Session;
using Cosmodust.Store;

namespace Cosmodust.Tracking;

public sealed class EntityEntry
{
    public required string Id { get; init; }
    public required string ContainerName { get; init; }
    public required string PartitionKey { get; init; }
    public required string PartitionKeyName { get; init; }
    public required object Entity { get; init; }
    public required Type EntityType { get; init; }
    public required ShadowPropertyProvider Provider { get; init; }
    public required DomainEventAccessor DomainEventAccessor { get; init; }
    public string? ETag { get; set; }
    public IDictionary<string, object?> ShadowProperties { get; private set; }
        = new Dictionary<string, object?>();
    public EntityState State { get; set; } = EntityState.Detached;

    public bool IsModified => State == EntityState.Modified;
    public bool IsRemoved => State == EntityState.Removed;
    public bool IsUnchanged => State == EntityState.Unchanged;
    public bool IsAdded => State == EntityState.Added;
    public bool IsPendingChanges => State != EntityState.Unchanged;

    /// <summary>
    /// Marks the entity as added by setting the state to <see cref="EntityState.Added"/>.
    /// </summary>
    public void Add() =>
        State = EntityState.Added;

    /// <summary>
    /// Marks the entity as modified by setting the state to <see cref="EntityState.Modified"/>.
    /// </summary>
    public void Modify() =>
        State = EntityState.Modified;

    /// <summary>
    /// Marks the entity as removed by setting the state to <see cref="EntityState.Removed"/>.
    /// </summary>
    public void Remove() =>
        State = EntityState.Removed;

    /// <summary>
    /// Marks the entity as unchanged by setting the state to <see cref="EntityState.Unchanged"/>.
    /// </summary>
    public void Unchange() =>
        State = EntityState.Unchanged;

    public void Detach() =>
        State = EntityState.Detached;

    public void UpdateETag(string eTag)
    {
        ETag = eTag;
        ShadowProperties["_etag"] = eTag;
    }

    public TProperty? ReadShadowProperty<TProperty>(string shadowPropertyName) =>
        ShadowProperties.TryGetValue(shadowPropertyName, out var value)
            ? (TProperty?) value
            : default;

    public void WriteShadowProperty<TProperty>(string shadowPropertyName, TProperty? value) =>
        ShadowProperties[shadowPropertyName] = value;

    public void WriteShadowProperty(string shadowPropertyName, object? value) =>
        ShadowProperties[shadowPropertyName] = value;

    /// <summary>
    /// Reads JSON properties from the provider for the current entity.
    /// </summary>
    public void ReadShadowProperties()
    {
        Debug.Assert(Entity != null);
        ShadowProperties = Provider.RemoveAll(Entity) ?? ShadowProperties;
        Debug.WriteLine($"Retrieved entity '{Id}' from the shadow property provider.");
    }

    /// <summary>
    /// Writes the JSON properties of the entity to the provider for serialization.
    /// </summary>
    public void WriteShadowProperties()
    {
        Debug.Assert(Entity != null);

        try
        {
            Provider.AddAll(Entity, ShadowProperties);
            Debug.WriteLine($"Returned entity '{Id}' to the shadow property provider.");
        }

        finally
        {
            ShadowProperties = ShadowPropertyProvider.EmptyShadowProperties;
        }
    }

    public IEnumerable<object> GetDomainEvents() =>
        DomainEventAccessor.GetDomainEvents(Entity);

    public string NextDomainEventId() =>
        DomainEventAccessor.NextId();
}

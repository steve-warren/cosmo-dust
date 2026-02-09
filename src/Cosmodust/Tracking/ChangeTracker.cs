using System.Diagnostics;
using Cosmodust.Session;
using Cosmodust.Shared;
using Cosmodust.Store;

namespace Cosmodust.Tracking;

public sealed class ChangeTracker : IDisposable
{
    private readonly ShadowPropertyProvider _propertyProvider;
    private readonly List<EntityEntry> _entries = new();
    private readonly Dictionary<(Type Type, string Id), object> _entityByTypeId = new();
    private readonly Dictionary<object, EntityEntry> _entriesByEntity = new();

    public ChangeTracker(
        EntityConfigurationProvider entityConfiguration,
        ShadowPropertyProvider propertyProvider)
    {
        Ensure.NotNull(entityConfiguration);
        Ensure.NotNull(propertyProvider);

        EntityConfiguration = entityConfiguration;
        _propertyProvider = propertyProvider;
    }

    public EntityConfigurationProvider EntityConfiguration { get; }
    public IReadOnlyList<EntityEntry> Entries => _entries;

    public IEnumerable<EntityEntry> PendingChanges =>
        _entries.Where(x => x.State != EntityState.Unchanged);

    public EntityEntry Entry(object entity) =>
        _entriesByEntity.TryGetValue(key: entity, out var entry)
            ? entry
            : throw new InvalidOperationException("Entity has not been loaded into the session.");

    /// <summary>
    /// Registers an entity as added in the change tracker.
    /// </summary>
    /// <param name="entity">The entity to register.</param>
    public void RegisterAdded(object entity)
    {
        Ensure.NotNull(entity);

        var entry = CreateEntry(entity, EntityState.Added);

        TrackEntity(entry);
    }

    /// <summary>
    /// Registers an entity as unchanged in the change tracker.
    /// </summary>
    /// <param name="entity">The entity to register.</param>
    /// <param name="eTag">The ETag of the entity.</param>
    public void RegisterUnchanged(
        object entity,
        string? eTag = null)
    {
        Ensure.NotNull(entity);

        var entry = CreateEntry(entity, EntityState.Unchanged);

        entry.UpdateETag(eTag);

        TrackEntity(entry);
    }

    public object GetOrRegisterUnchanged(object entity)
    {
        Ensure.NotNull(entity);

        var config = EntityConfiguration.GetEntityConfiguration(entity.GetType());
        var id = config.IdGetter.GetString(entity);

        if (_entityByTypeId.TryGetValue((Type: entity.GetType(), Id: id), out var trackedEntity))
            return trackedEntity;

        RegisterUnchanged(entity);

        return entity;
    }

    /// <summary>
    /// Registers an entity as modified in the change tracker.
    /// </summary>
    /// <param name="entity">The entity to register as modified.</param>
    public void RegisterModified(object entity)
    {
        Ensure.NotNull(entity);

        var entry = Entry(entity)
                    ?? throw new InvalidOperationException($"Cannot update entity of type {entity.GetType()} because it has not been loaded into the session.");

        entry.Modify();
    }

    /// <summary>
    /// Registers an entity to be removed from the session.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void RegisterRemoved(object entity)
    {
        Ensure.NotNull(entity);

        var entry = Entry(entity)
                    ?? throw new InvalidOperationException($"Cannot update entity of type {entity.GetType()} because it has not been loaded into the session.");

        entry.Remove();
    }

    /// <summary>
    /// Attempts to retrieve an entity of type <typeparamref name="TEntity"/> with the specified ID from the identity map.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to retrieve.</typeparam>
    /// <param name="id">The ID of the entity to retrieve.</param>
    /// <param name="entity">When this method returns, contains the entity associated with the specified ID, if the ID is found; otherwise, the default value for the type of the <paramref name="entity"/> parameter. This parameter is passed uninitialized.</param>
    /// <returns><c>true</c> if the identity map contains an entity with the specified ID; otherwise, <c>false</c>.</returns>
    public bool TryGet<TEntity>(string id, out TEntity? entity)
    {
        Ensure.NotNullOrWhiteSpace(id);

        if (_entityByTypeId.TryGetValue((Type: typeof(TEntity), Id: id), out var value))
        {
            entity = (TEntity?) value;
            return true;
        }

        entity = default;
        return false;
    }

    public bool Exists<TEntity>(string id)
    {
        Ensure.NotNullOrWhiteSpace(id);

        return _entityByTypeId.ContainsKey((Type: typeof(TEntity), Id: id));
    }

    public void Commit(EntityEntry entry)
    {
        entry.ClearDomainEvents();

        switch (entry.State)
        {
            case EntityState.Added or EntityState.Modified:
                entry.Unchange();
                break;
            case EntityState.Removed:
                _entries.Remove(entry);
                _entityByTypeId.Remove((Type: entry.EntityType, entry.Id));
                break;
        }
    }

    private EntityEntry CreateEntry(object entity, EntityState state)
    {
        Ensure.NotNull(entity);

        var entityType = entity.GetType();
        var config = EntityConfiguration.GetEntityConfiguration(entityType);
        var entry = config.CreateEntry(_propertyProvider, entity, state);

        return entry;
    }

    private void TrackEntity(EntityEntry entry)
    {
        if (_entityByTypeId.ContainsKey((Type: entry.EntityType, Id: entry.Id)))
            throw new InvalidOperationException($"An entity of type '{entry.EntityType.Name}' with ID '{entry.Id}' has already been loaded into the session.");

        _entries.Add(entry);
        _entityByTypeId.Add((Type: entry.EntityType, entry.Id), entry.Entity);
        _entriesByEntity.Add(key: entry.Entity, value: entry);
    }

    public void Dispose()
    {
        foreach (var entry in _entries)
        {
            try
            {
                entry.PullShadowPropertiesFromSerializer();
            }

            catch
            {
                // ignored
            }
        }
    }
}

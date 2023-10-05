namespace CosmicConnector;

public sealed class ChangeTracker
{
    private readonly List<EntityEntry> _entries = new();

    public IReadOnlyList<EntityEntry> Entries => _entries;

    /// <summary>
    /// Gets an enumerable collection of <see cref="EntityEntry"/> objects that represent entities
    /// that have been marked for deletion from the database.
    /// </summary>
    public IEnumerable<EntityEntry> RemovedEntries =>
        _entries.Where(x => x.State == EntityState.Removed);

    public EntityEntry? FindEntry(object entity) =>
        _entries.FirstOrDefault(x => x.Entity == entity);

    /// <summary>
    /// Tracks changes made to an entity with the specified ID.
    /// </summary>
    /// <param name="id">The ID of the entity to track.</param>
    /// <param name="entity">The entity to track.</param>
    public void Track(string id, object entity) =>
        Register(id, entity, EntityState.Added);

    private void Register(string id, object entity, EntityState state)
    {
        var entry = new EntityEntry
        {
            Id = id,
            Entity = entity,
            EntityType = entity.GetType(),
            State = state
        };

        _entries.Add(entry);
    }

    public void Reset()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Unchange();
                    break;
                case EntityState.Modified:
                    entry.Unchange();
                    break;
                case EntityState.Removed:
                    _entries.RemoveAt(i);
                    i--;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown entity state: {entry.State}");
            }
        }
    }
}

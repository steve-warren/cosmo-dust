namespace CosmicConnector;

public interface IDatabaseFacade
{
    EntityConfigurationHolder EntityConfiguration { get; set; }
    ValueTask<TEntity?> FindAsync<TEntity>(string id, string? partitionKey = default, CancellationToken cancellationToken = default) where TEntity : class;

    Task SaveChangesAsync(EntityEntry entry, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(IEnumerable<EntityEntry> entries, CancellationToken cancellationToken = default);
}

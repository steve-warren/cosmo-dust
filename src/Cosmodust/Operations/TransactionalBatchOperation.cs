using Cosmodust.Shared;
using Cosmodust.Tracking;
using Microsoft.Azure.Cosmos;

namespace Cosmodust.Operations;

public class TransactionalBatchOperation
{
    private readonly Database _database;
    private readonly IEnumerable<EntityEntry> _entries;

    public TransactionalBatchOperation(
        Database database,
        IEnumerable<EntityEntry> entries)
    {
        Ensure.NotNull(database);
        Ensure.NotNull(entries);

        _database = database;
        _entries = entries;
    }

    public async Task<List<OperationResult>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var containerAndPartitionKey = _entries
            .GroupBy(e => (e.ContainerName, e.PartitionKey));

        var results = new List<OperationResult>();

        foreach (var entriesGrouping in containerAndPartitionKey)
            await ExecuteTransactionalBatchAsync(
                    containerName: entriesGrouping.Key.ContainerName,
                    partitionKey: entriesGrouping.Key.PartitionKey,
                    entries: entriesGrouping,
                    results: results,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        return results;
    }

    private async Task ExecuteTransactionalBatchAsync(
        string containerName,
        string partitionKey,
        IEnumerable<EntityEntry> entries,
        List<OperationResult> results,
        CancellationToken cancellationToken = default)
    {
        var container = _database.GetContainer(containerName);
        var batch = container.CreateTransactionalBatch(new PartitionKey(partitionKey));

        var entityEntries = entries.ToList();
        var domainEvents = new List<Dictionary<string, object>>();

        foreach (var entry in entityEntries)
        {
            // send the json properties to the provider
            // for the json serializer to pick up
            entry.ClearAndPushShadowPropertiesToSerializer();

            var batchOptions = new TransactionalBatchItemRequestOptions
            {
                EnableContentResponseOnWrite = false,
                IfNoneMatchEtag = entry.ETag
            };
            
            _ = entry.State switch
            {
                EntityState.Added => batch.CreateItem(entry.Entity, batchOptions),
                EntityState.Removed => batch.DeleteItem(entry.Id, batchOptions),
                EntityState.Modified => batch.ReplaceItem(entry.Id, entry.Entity, batchOptions),
                EntityState.Unchanged => throw new InvalidOperationException(),
                _ => throw new InvalidOperationException()
            };

            foreach(var domainEvent in
                    entry.DomainEventAccessor.GetDomainEvents(entry.Entity))
            {
                var eventEntry = new Dictionary<string, object>
                {
                    { "id", entry.DomainEventAccessor.NextId() },
                    { entry.PartitionKeyName, entry.PartitionKey },
                    { "domainEvent", domainEvent }
                };

                domainEvents.Add(eventEntry);
            }
        }

        var createBatchOptions = new TransactionalBatchItemRequestOptions { EnableContentResponseOnWrite = false };
        
        foreach (var eventEntry in domainEvents)
            batch.CreateItem(eventEntry, createBatchOptions);

        var response = await batch
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        
        for(var i = 0; i < entityEntries.Count; i ++)
        {
            var itemResponse = response[i];
            
            var entry = entityEntries[i];
            entry.PullShadowPropertiesFromSerializer();
            entry.UpdateETag(itemResponse.ETag);
            
            results.Add(itemResponse.ToOperationResult(entry));
        }
    }
}

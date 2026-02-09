using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cosmodust.Extensions;
using Cosmodust.Linq;
using Cosmodust.Operations;
using Cosmodust.Shared;
using Cosmodust.Tracking;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Cosmodust;

/// <summary>
/// Represents a Cosmos DB database.
/// </summary>
public sealed class CosmosDatabase : IDatabase
{
    private readonly ContainerProvider _containerProvider;
    private readonly Database _database;
    private readonly CosmosLinqSerializerOptions _cosmosLinqSerializerOptions;

    public CosmosDatabase(
        CosmosClient cosmosClient,
        CosmodustOptions options,
        CosmosLinqSerializerOptions? cosmosLinqSerializerOptions = null)
    {
        Ensure.NotNull(cosmosClient);
        Ensure.NotNullOrWhiteSpace(options.DatabaseId);

        _database = cosmosClient.GetDatabase(options.DatabaseId);
        _cosmosLinqSerializerOptions = cosmosLinqSerializerOptions ?? new CosmosLinqSerializerOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        };
        _containerProvider = new ContainerProvider(_database);
    }

    public string Name => _database.Id;

    /// <summary>
    /// Finds an entity of type <typeparamref name="TEntity"/> in the specified container with the given ID and partition key.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to find.</typeparam>
    /// <param name="containerName">The name of the container to search for the entity.</param>
    /// <param name="id">The ID of the entity to find.</param>
    /// <param name="partitionKey">The partition key of the entity to find.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    public ValueTask<OperationResult> FindAsync<TEntity>(
        string containerName,
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        Ensure.NotNullOrWhiteSpace(containerName);
        Ensure.NotNullOrWhiteSpace(id);
        Ensure.NotNullOrWhiteSpace(partitionKey);

        var container = _containerProvider.GetOrAddContainer(containerName);

        var operation = new ReadItemOperation<TEntity?>(container, id, partitionKey);

        return operation.ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a LINQ queryable for the specified Cosmos DB container.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to query.</typeparam>
    /// <param name="containerName">The name of the Cosmos DB container.</param>
    /// <returns>A LINQ queryable for the specified Cosmos DB container.</returns>
    public IQueryable<TEntity> CreateLinqQuery<TEntity>(string containerName)
    {
        Ensure.NotNullOrWhiteSpace(containerName);

        return _containerProvider.GetOrAddContainer(containerName).GetItemLinqQueryable<TEntity>(
            linqSerializerOptions: _cosmosLinqSerializerOptions);
    }

    public async IAsyncEnumerable<TEntity> ToAsyncEnumerable<TEntity>(
        CosmodustLinqQuery<TEntity> query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Ensure.NotNull(query);

        var queryDefinition = query.DatabaseLinqQuery.ToQueryDefinition();

        if (queryDefinition is null)
            queryDefinition = new QueryDefinition(
                "select * from root where root._type = @type");

        else
        {
            var typedQuerySql = queryDefinition.QueryText + " AND root._type = @type";
            queryDefinition = new QueryDefinition(query: typedQuerySql);
        }

        queryDefinition.WithParameter("@type", typeof(TEntity).Name);

        var container = _containerProvider.GetOrAddContainer(query.EntityConfiguration.ContainerName);

        var queryRequestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(query.PartitionKey) };

        using var feed = container.GetItemQueryIterator<TEntity>(
            queryDefinition,
            continuationToken: null,
            queryRequestOptions);

        while (feed.HasMoreResults)
        {
            var response = await feed
                .ReadNextAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var entity in response)
                yield return entity;
        }
    }

    public async IAsyncEnumerable<TEntity> ToAsyncEnumerable<TEntity>(
        string containerName,
        string partitionKey,
        string sql,
        IEnumerable<(string Name, object? Value)>? parameters = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Ensure.NotNullOrWhiteSpace(containerName);
        Ensure.NotNullOrWhiteSpace(partitionKey);
        Ensure.NotNullOrWhiteSpace(sql);
        
        var queryDefinition = new QueryDefinition(sql);

        if (parameters is not null)
            foreach (var parameter in parameters)
                queryDefinition.WithParameter(name: parameter.Name, value: parameter.Value);

        var container = _containerProvider.GetOrAddContainer(containerName);

        var queryRequestOptions =
            new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) };

        using var feed = container.GetItemQueryIterator<TEntity>(
            queryDefinition,
            continuationToken: null,
            queryRequestOptions);

        while (feed.HasMoreResults)
        {
            var response = await feed
                .ReadNextAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var entity in response)
                yield return entity;
        }
    }

    /// <summary>
    /// Commits the changes made to the entity entry to the Cosmos database.
    /// </summary>
    /// <param name="entry">The entity entry to commit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An IDocumentOperationResult representing the result of the commit operation.</returns>
    public async Task<OperationResult> CommitAsync(
        EntityEntry entry,
        CancellationToken cancellationToken = default)
    {
        Ensure.NotNull(entry);

        entry.ClearAndPushShadowPropertiesToSerializer();

        var operation = CreateWriteOperation(entry);

        var response = await operation
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        // pull shadow properties back before updating the etag
        entry.PullShadowPropertiesFromSerializer();
        entry.UpdateETag(response.ETag);

        foreach(var domainEvent in entry.GetDomainEvents())
            await CommitDomainEventAsync(entry, domainEvent, cancellationToken)
                .ConfigureAwait(false);

        return response;
    }

    private async Task CommitDomainEventAsync(
        EntityEntry entry,
        object domainEvent,
        CancellationToken cancellationToken)
    {
        var eventEntry = new Dictionary<string, object>
        {
            { "id", entry.DomainEventAccessor.NextId() },
            { entry.PartitionKeyName, entry.PartitionKey },
            { "domainEvent", domainEvent }
        };

        var container = _containerProvider.GetOrAddContainer(entry.ContainerName);

        var createItemOperation = new CreateItemOperation(
            container,
            eventEntry,
            entry.PartitionKey);

        var response = await createItemOperation
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        Debug.WriteLine(
            $"AddOrUpdate operation HTTP {response.StatusCode} - {response.Cost} RUs");
    }

    public async Task<List<OperationResult>> CommitTransactionAsync(
        IEnumerable<EntityEntry> entries,
        CancellationToken cancellationToken)
    {
        Ensure.NotNull(entries);

        var batchOperation = new TransactionalBatchOperation(
            database: _database,
            entries: entries);

        var results = await batchOperation.ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return results;
    }

    private IDocumentWriteOperation CreateWriteOperation(EntityEntry entry)
    {
        var container = _containerProvider.GetOrAddContainer(entry.ContainerName);

        return entry.State switch
        {
            EntityState.Added => new CreateItemOperation(container, entry.Entity, entry.PartitionKey),
            EntityState.Removed => new DeleteItemOperation(container, entry.Id, entry.PartitionKey),
            EntityState.Modified => new ReplaceDocumentOperation(
                container,
                entry.Entity,
                entry.Id,
                entry.PartitionKey,
                entry.ETag),
            _ => throw new NotImplementedException()
        };
    }
}

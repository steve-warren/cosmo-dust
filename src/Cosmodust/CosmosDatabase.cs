using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cosmodust.Extensions;
using Cosmodust.Json;
using Cosmodust.Linq;
using Cosmodust.Operations;
using Cosmodust.Query;
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
        SqlQuery<TEntity> query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Ensure.NotNull(query);

        var queryDefinition = new QueryDefinition(query.Sql);

        foreach (var parameter in query.Parameters)
            queryDefinition.WithParameter(name: parameter.Name, value: parameter.Value);

        var container = _containerProvider.GetOrAddContainer(query.EntityConfiguration.ContainerName);

        var queryRequestOptions =
            new QueryRequestOptions { PartitionKey = new PartitionKey(query.PartitionKey) };

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

    public async Task CommitAsync(
        IEnumerable<EntityEntry> entries,
        CancellationToken cancellationToken = default)
    {
        Ensure.NotNull(entries);

        foreach (var entry in entries)
        {
            if (entry.IsUnchanged)
                continue;

            entry.WriteShadowProperties();

            var operation = CreateWriteOperation(entry);
            var response = await operation
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            Debug.WriteLine(
                $"AddOrUpdate operation HTTP {response.StatusCode} - {response.Cost} RUs");

            foreach(var domainEvent in entry.GetDomainEvents())
            {
                var domainEventMetadata = new Dictionary<string, object>
                {
                    { "id", entry.NextDomainEventId() },
                    { entry.PartitionKeyName, entry.PartitionKey },
                    { "domainEvent", domainEvent }
                };

                var container = _containerProvider.GetOrAddContainer(entry.ContainerName);

                var createItemOperation = new CreateItemOperation(
                    container,
                    domainEventMetadata,
                    entry.PartitionKey);

                response = await createItemOperation
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);

                Debug.WriteLine(
                    $"AddOrUpdate operation HTTP {response.StatusCode} - {response.Cost} RUs");
            }
        }
    }

    public async Task CommitTransactionAsync(
        IEnumerable<EntityEntry> entries,
        CancellationToken cancellationToken)
    {
        Ensure.NotNull(entries);

        var batchOperation = new TransactionalBatchOperation(
            database: _database,
            entries: entries);

        await batchOperation.ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private ICosmosWriteOperation CreateWriteOperation(EntityEntry entry)
    {
        var container = _containerProvider.GetOrAddContainer(entry.ContainerName);

        return entry.State switch
        {
            EntityState.Added => new CreateItemOperation(container, entry.Entity, entry.PartitionKey),
            EntityState.Removed => new DeleteItemOperation(container, entry.Id, entry.PartitionKey),
            EntityState.Modified => new ReplaceItemOperation(container, entry.Entity, entry.Id, entry.PartitionKey),
            _ => throw new NotImplementedException()
        };
    }
}

using Cosmodust.Linq;
using Cosmodust.Operations;
using Cosmodust.Query;
using Cosmodust.Serialization;
using Cosmodust.Shared;
using Cosmodust.Store;
using Cosmodust.Tracking;

namespace Cosmodust.Session;

public sealed class DocumentSession : IDocumentSession, IDisposable
{
    internal DocumentSession(
        Guid id,
        IDatabase database,
        EntityConfigurationProvider entityConfiguration,
        SqlParameterObjectTypeResolver sqlParameterObjectTypeResolver,
        ShadowPropertyProvider shadowPropertyProvider)
    {
        Ensure.NotNull(database);
        Ensure.NotNull(entityConfiguration);
        Ensure.NotNull(sqlParameterObjectTypeResolver);

        Id = id;
        Database = database;
        EntityConfiguration = entityConfiguration;
        SqlParameterObjectTypeResolver = sqlParameterObjectTypeResolver;
        ChangeTracker = new ChangeTracker(
            entityConfiguration,
            shadowPropertyProvider);

    }

    public Guid Id { get; }
    public ChangeTracker ChangeTracker { get; }
    public IDatabase Database { get; }
    public EntityConfigurationProvider EntityConfiguration { get; }
    public SqlParameterObjectTypeResolver SqlParameterObjectTypeResolver { get; }

    /// <inheritdoc />
    public async ValueTask<TEntity?> FindAsync<TEntity>(
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        Ensure.NotNullOrWhiteSpace(
            argument: partitionKey,
            message: "Document id key required.");

        Ensure.NotNullOrWhiteSpace(
            argument: partitionKey,
            message: "Partition key required.");

        var configuration = GetConfiguration<TEntity>();

        if (ChangeTracker.TryGet(id, out TEntity? entity))
            return entity;

        var readOperationResult = await Database.FindAsync<TEntity>(
            configuration.ContainerName,
            id,
            partitionKey,
            cancellationToken);

        entity = (TEntity?) readOperationResult.Entity;

        if (entity is null)
            return default;

        ChangeTracker.RegisterUnchanged(entity);

        return entity;
    }

    /// <inheritdoc />
    public IQueryable<TEntity> Query<TEntity>(string partitionKey)
    {
        Ensure.NotNullOrWhiteSpace(
            argument: partitionKey,
            message: "Partition key required.");

        var entityConfiguration = GetConfiguration<TEntity>();
        var queryable = Database.CreateLinqQuery<TEntity>(entityConfiguration.ContainerName);

        return new CosmodustLinqQuery<TEntity>(
            Database,
            ChangeTracker,
            entityConfiguration,
            partitionKey,
            queryable);
    }

    /// <inheritdoc />
    public SqlQuery<TEntity> Query<TEntity>(
        string partitionKey,
        string sql,
        object? parameters = null)
    {
        Ensure.NotNullOrWhiteSpace(
            argument: partitionKey,
            message: "Partition key required.");
        
        Ensure.NotNullOrWhiteSpace(
            argument: sql,
            message: "Query required.");

        var config = GetConfiguration<TEntity>();

        return new SqlQuery<TEntity>(
            database: Database,
            changeTracker: ChangeTracker,
            entityConfiguration: config,
            sql: sql,
            partitionKey: partitionKey,
            parameters: SqlParameterObjectTypeResolver.ExtractParametersFromObject(parameters));
    }

    /// <inheritdoc />
    public void Store<TEntity>(TEntity entity)
    {
        Ensure.NotNull(entity);
        ChangeTracker.RegisterAdded(entity);
    }

    /// <inheritdoc />
    public void Attach<TEntity>(
        TEntity entity,
        string? eTag = null)
    {
        Ensure.NotNull(entity);
        ChangeTracker.RegisterUnchanged(entity, eTag);
    }

    /// <inheritdoc />
    public void Update<TEntity>(TEntity entity)
    {
        Ensure.NotNull(entity);
        ChangeTracker.RegisterModified(entity);
    }

    /// <inheritdoc />
    public void Remove<TEntity>(TEntity entity)
    {
        Ensure.NotNull(entity);
        ChangeTracker.RegisterRemoved(entity);
    }

    /// <inheritdoc />
    public async Task<DocumentOperationResult> CommitAsync(CancellationToken cancellationToken = default)
    {
        var documentOperationResults = new List<IDocumentOperationResult>();
        var pendingChanges = ChangeTracker.PendingChanges.ToArray();

        if (pendingChanges.Length == 1)
        {
            var result = await Database
                .CommitAsync(pendingChanges[0], cancellationToken)
                .ConfigureAwait(false);

            ChangeTracker.Commit(pendingChanges[0]);
            
            documentOperationResults.Add(DocumentOperationResultFactory.Create(result));
        }

        else
        {
            foreach (var entry in pendingChanges)
            {
                var result = await Database.CommitAsync(entry, cancellationToken).ConfigureAwait(false);
                ChangeTracker.Commit(entry);

                var documentOperationResult = DocumentOperationResultFactory.Create(result);
            
                documentOperationResults.Add(documentOperationResult);
            }   
        }

        return new DocumentOperationResult(documentOperationResults);
    }

    /// <inheritdoc />
    public async Task<DocumentOperationResult> CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        var operationResults = await Database.
            CommitTransactionAsync(
                ChangeTracker.PendingChanges,
                cancellationToken)
            .ConfigureAwait(false);
        
        foreach(var result in operationResults)
            ChangeTracker.Commit(result.Entry);

        return new DocumentOperationResult([]);
    }

    public EntityEntry Entity(object entity) =>
        ChangeTracker.Entry(entity);

    public void Dispose()
    {
        ChangeTracker.Dispose();
    }

    private EntityConfiguration GetConfiguration<TEntity>() =>
        EntityConfiguration.GetEntityConfiguration(typeof(TEntity)) ??
        throw new InvalidOperationException($"No configuration has been registered for type {typeof(TEntity).FullName}.");
}

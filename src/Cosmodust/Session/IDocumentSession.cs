using Cosmodust.Operations;
using Cosmodust.Query;
using Cosmodust.Tracking;

namespace Cosmodust.Session;

/// <summary>
/// Represents a session for interacting with the Cosmos DB database.
/// </summary>
public interface IDocumentSession
{
    ChangeTracker ChangeTracker { get; }

    /// <summary>
    /// Finds an entity of type <typeparamref name="TEntity"/> by its ID and partition key.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to find.</typeparam>
    /// <param name="id">The ID of the entity to find.</param>
    /// <param name="partitionKey">The partition key of the entity to find.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="ValueTask{TEntity}"/> representing the asynchronous operation, containing the found entity, or <c>null</c> if the entity was not found.</returns>
    ValueTask<TEntity?> FindAsync<TEntity>(
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Stores the specified entity in the session.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to provider.</typeparam>
    /// <param name="entity">The entity to provider.</param>
    void Store<TEntity>(TEntity entity);
    /// <summary>
    /// Removes the specified entity from the session.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity to remove.</typeparam>
    /// <param name="entity">The entity to remove.</param>
    void Remove<TEntity>(TEntity entity);
    /// <summary>
    /// Updates the specified entity in the change tracker.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    /// <param name="entity">The entity to update.</param>
    void Update<TEntity>(TEntity entity);

    /// <summary>
    /// Attaches the specified entity to the session.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to attach.</typeparam>
    /// <param name="entity">The entity to attach.</param>
    /// <param name="eTag">The entity tag (optional).</param>
    void Attach<TEntity>(TEntity entity, string? eTag = null);
    /// <summary>
    /// Returns a queryable object for the specified entity type and partition key.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to query.</typeparam>
    /// <param name="partitionKey">The partition key to use for the query.</param>
    /// <returns>A queryable object for the specified entity type and partition key.</returns>
    IQueryable<TEntity> Query<TEntity>(string partitionKey);
    /// <summary>
    /// Executes a SQL query against the Cosmos DB database and returns the results as a <see cref="SqlQuery{TEntity}"/> object.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to query.</typeparam>
    /// <param name="partitionKey">The partition key value for the query.</param>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="parameters">An optional object containing parameters to be used in the query.</param>
    /// <returns>A <see cref="SqlQuery{TEntity}"/> object representing the results of the query.</returns>
    SqlQuery<TEntity> Query<TEntity>(
        string partitionKey,
        string sql,
        object? parameters = default);
    /// <summary>
    /// Commits all pending changes made in the session to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    Task<DocumentOperationResult> CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Commits the pending changes in the current transaction to the database.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    Task<DocumentOperationResult> CommitTransactionAsync(CancellationToken cancellationToken = default);
}

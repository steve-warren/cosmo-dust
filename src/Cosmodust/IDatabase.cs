using System.Runtime.CompilerServices;
using Cosmodust.Linq;
using Cosmodust.Operations;
using Cosmodust.Query;
using Cosmodust.Tracking;

namespace Cosmodust;

public interface IDatabase
{
    string Name { get; }
    ValueTask<OperationResult> FindAsync<TEntity>(
        string containerName,
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<TEntity> ToAsyncEnumerable<TEntity>(
        CosmodustLinqQuery<TEntity> query,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TEntity> ToAsyncEnumerable<TEntity>(
        string containerName,
        string partitionKey,
        string sql,
        IEnumerable<(string Name, object? Value)>? parameters = default,
        CancellationToken cancellationToken = default);
    Task<OperationResult> CommitAsync(
        EntityEntry entry,
        CancellationToken cancellationToken = default);
    Task<List<OperationResult>> CommitTransactionAsync(
        IEnumerable<EntityEntry> entries,
        CancellationToken cancellationToken = default);
    IQueryable<TEntity> CreateLinqQuery<TEntity>(string containerName);
}

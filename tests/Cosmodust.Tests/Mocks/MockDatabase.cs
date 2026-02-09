using System.Net;
using System.Runtime.CompilerServices;
using Azure;
using Cosmodust.Linq;
using Cosmodust.Operations;
using Cosmodust.Query;
using Cosmodust.Tracking;

namespace Cosmodust.Tests;

public sealed class MockDatabase : IDatabase
{
    private readonly Dictionary<(Type EntityType, string Id), object> _entities = new();

    public string Name { get; private set; } = "MockDatabase";
    public int Count => _entities.Count;
    public bool CommitWasCalled { get; private set; }

    public ValueTask<OperationResult> FindAsync<TEntity>(string containerName, string id,
        string partitionKey, CancellationToken cancellationToken = default)
    {
        _ = _entities.TryGetValue((typeof(TEntity), id), out var entity);

        return ValueTask.FromResult(new OperationResult
        {
            Entity = entity,
            EntityType = typeof(TEntity),
            StatusCode = HttpStatusCode.OK,
            Cost = 1_000
        });
    }

    public void Add(string id, object entity)
    {
        _entities.Add((entity.GetType(), id), entity);
    }

    public IAsyncEnumerable<TEntity> ToAsyncEnumerable<TEntity>(string containerName, string partitionKey, string sql,
        IEnumerable<(string Name, object? Value)>? parameters = default, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<OperationResult> CommitAsync(EntityEntry entry, CancellationToken cancellationToken = default)
    {
        CommitWasCalled = true;

        return Task.FromResult(new OperationResult
        {
            Entity = entry.Entity,
            EntityType = entry.EntityType,
            StatusCode = HttpStatusCode.OK,
            ETag = "",
            Cost = 0.00
        });
    }

    public Task<List<OperationResult>> CommitTransactionAsync(IEnumerable<EntityEntry> entries, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IQueryable<TEntity> CreateLinqQuery<TEntity>(string containerName)
    {
        return _entities.Values.OfType<TEntity>().AsQueryable();
    }

    public IQueryable<TEntity> GetLinqQuery<TEntity>(string containerName, string? partitionKey = null)
    {
        return _entities.Values.OfType<TEntity>().AsQueryable();
    }

    public async IAsyncEnumerable<TEntity> ToAsyncEnumerable<TEntity>(CosmodustLinqQuery<TEntity> query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entity in query.DatabaseLinqQuery)
        {
            await Task.Yield();
            yield return entity;
        }
    }
}

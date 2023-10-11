using System.Runtime.CompilerServices;

namespace CosmoDust.Linq;

public static class DocumentQueryExtensions
{
    public static async Task<List<TEntity>> ToListAsync<TEntity>(this IQueryable<TEntity> queryable, CancellationToken cancellationToken = default)
    {
        var documentQuery = GetDocumentQuery(queryable);
        var iterator = documentQuery.GetAsyncEnumerable(cancellationToken)
                                    .WithCancellation(cancellationToken);

        var list = new List<TEntity>();

        await foreach (var entity in iterator)
            list.Add(entity);

        return list;
    }

    public static IAsyncEnumerable<TEntity> ToAsyncEnumerable<TEntity>(this IQueryable<TEntity> queryable, CancellationToken cancellationToken = default)
    {
        var documentQuery = GetDocumentQuery(queryable);
        return documentQuery.GetAsyncEnumerable(cancellationToken);
    }

    public static async Task<TEntity?> FirstOrDefaultAsync<TEntity>(this IQueryable<TEntity> queryable, CancellationToken cancellationToken = default)
    {
        var documentQuery = GetDocumentQuery(queryable.Take(1));

        var iterator = documentQuery.GetAsyncEnumerable(cancellationToken)
                                    .WithCancellation(cancellationToken);

        await foreach (var entity in iterator)
            return entity;

        return default;
    }

    private static DocumentQuery<TEntity> GetDocumentQuery<TEntity>(IQueryable<TEntity> queryable)
    {
        if (queryable is not DocumentQuery<TEntity> documentQueryable)
            throw new ArgumentException($"The {nameof(queryable)} must be of type {nameof(DocumentQuery<TEntity>)}", nameof(queryable));

        return documentQueryable;
    }
}

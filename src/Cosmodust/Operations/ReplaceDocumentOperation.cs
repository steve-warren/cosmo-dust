using Cosmodust.Shared;
using Microsoft.Azure.Cosmos;

namespace Cosmodust.Operations;

internal class ReplaceDocumentOperation : IDocumentWriteOperation
{
    private readonly Container _container;
    private readonly object _entity;
    private readonly string _id;
    private readonly string _partitionKey;
    private readonly string? _eTag;

    public ReplaceDocumentOperation(
        Container container,
        object entity,
        string id,
        string partitionKey,
        string? eTag)
    {
        Ensure.NotNull(container);
        Ensure.NotNull(entity);
        Ensure.NotNullOrWhiteSpace(id);
        Ensure.NotNullOrWhiteSpace(partitionKey);
        
        _container = container;
        _entity = entity;
        _id = id;
        _partitionKey = partitionKey;
        _eTag = eTag;
    }

    public async Task<OperationResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReplaceItemAsync(
                item: _entity,
                id: _id,
                partitionKey: new PartitionKey(_partitionKey),
                requestOptions: new ItemRequestOptions { EnableContentResponseOnWrite = false, IfMatchEtag = _eTag },
                cancellationToken: cancellationToken);

            return response.ToOperationResult();
        }

        catch (CosmosException ex)
        {
            return new OperationResult
            {
                Entry = null,
                StatusCode = ex.StatusCode,
                Cost = ex.RequestCharge,
                ETag = ex.Headers.ETag
            };
        }
    }
}

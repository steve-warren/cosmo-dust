using System.Diagnostics;
using System.Net;
using Cosmodust.Shared;
using Microsoft.Azure.Cosmos;

namespace Cosmodust.Operations;

internal class ReadItemOperation<TEntity>
{
    private readonly Container _container;
    private readonly string _id;
    private readonly string _partitionKey;

    public ReadItemOperation(
        Container container,
        string id,
        string partitionKey)
    {
        Ensure.NotNull(container);
        Ensure.NotNullOrWhiteSpace(id);
        Ensure.NotNullOrWhiteSpace(partitionKey);

        _container = container;
        _id = id;
        _partitionKey = partitionKey;
    }

    public async ValueTask<OperationResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<TEntity?>(
                _id,
                new PartitionKey(_partitionKey),
                cancellationToken: cancellationToken);

            return response.ToOperationResult();
        }

        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new OperationResult
            {
                Entry = null,
                StatusCode = ex.StatusCode
            };
        }
    }
}

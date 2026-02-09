using Cosmodust.Tracking;
using Microsoft.Azure.Cosmos;

namespace Cosmodust.Operations;

internal static class OperationExtensions
{
    public static OperationResult ToOperationResult<TEntity>(
        this ItemResponse<TEntity> response)
    {
        return new OperationResult
        {
            Entry = null,
            StatusCode = response.StatusCode,
            Cost = response.RequestCharge,
            ETag = response.ETag
        };
    }

    public static OperationResult ToOperationResult(
        this TransactionalBatchOperationResult response,
        EntityEntry entry)
    {
        return new OperationResult
        {
            Entry = entry,
            StatusCode = response.StatusCode,
            ETag = response.ETag
        };
    }
}

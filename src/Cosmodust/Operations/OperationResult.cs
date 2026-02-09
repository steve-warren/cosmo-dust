using System.Net;
using Cosmodust.Tracking;

namespace Cosmodust.Operations;

public record struct OperationResult
{
    public required EntityEntry Entry { get; init; }
    public required HttpStatusCode StatusCode { get; init; }
    public string? ETag { get; init; }
    public double Cost { get; init; }
}

namespace Cosmodust.Operations;

public class DocumentOperationResult(
    IList<IDocumentOperationResult> results) : IDocumentOperationResult
{
    public IList<IDocumentOperationResult> Results { get; } = results;
}

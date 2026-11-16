namespace Ascolotl.Core.Models;

public sealed class ExportResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    /// <summary>Things the export worked around, e.g. images without alt text in an accessible PDF.</summary>
    public IReadOnlyList<string> Warnings { get; }

    private ExportResult(bool isSuccess, string? errorMessage, IReadOnlyList<string>? warnings = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Warnings = warnings ?? [];
    }

    public static ExportResult Success(IReadOnlyList<string>? warnings = null)
        => new(true, null, warnings);

    public static ExportResult Failure(string errorMessage)
        => new(false, errorMessage);
}

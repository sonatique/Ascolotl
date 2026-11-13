namespace Ascolotl.Core.Models;

public sealed class ExportResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    private ExportResult(bool isSuccess, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static ExportResult Success()
        => new(true, null);

    public static ExportResult Failure(string errorMessage)
        => new(false, errorMessage);
}

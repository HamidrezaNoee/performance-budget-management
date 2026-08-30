using PBM.Application;

namespace PBM.Infrastructure;

public sealed class ActualLedgerBatchService(IActualLedgerKeyPostingService posting) : IActualLedgerBatchService
{
    public async Task<ActualLedgerBatchResult> PostAsync(
        PostActualLedgerBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Entries.Count == 0)
            throw new ArgumentException("At least one Actual ledger entry is required.");
        if (request.Entries.Count > 1000)
            throw new ArgumentException("A single Actual ledger batch cannot exceed 1000 entries.");

        var results = new List<ActualLedgerBatchItemResult>(request.Entries.Count);
        for (var index = 0; index < request.Entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = request.Entries[index];
            try
            {
                var posted = await posting.PostAsync(item, cancellationToken);
                results.Add(new ActualLedgerBatchItemResult(
                    index,
                    item.SourceSystem,
                    item.ExternalDocumentId,
                    item.ExternalLineId,
                    true,
                    posted.WasDuplicate,
                    posted.Entry.Id,
                    posted.ProjectedActual,
                    null));
            }
            catch (Exception ex) when (request.ContinueOnError && ex is not OperationCanceledException)
            {
                results.Add(new ActualLedgerBatchItemResult(
                    index,
                    item.SourceSystem,
                    item.ExternalDocumentId,
                    item.ExternalLineId,
                    false,
                    false,
                    null,
                    null,
                    SafeError(ex)));
            }
        }

        return new ActualLedgerBatchResult(
            results.Count,
            results.Count(x => x.Success),
            results.Count(x => x.Success && x.WasDuplicate),
            results.Count(x => !x.Success),
            results);
    }

    private static string SafeError(Exception exception) => exception switch
    {
        ArgumentException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException
            => exception.Message,
        TimeoutException => "The posting could not acquire a concurrency lock in time; retry the row with the same business key.",
        _ => "The Actual ledger row failed with an unexpected server error. Use the Correlation ID to investigate before retrying."
    };
}

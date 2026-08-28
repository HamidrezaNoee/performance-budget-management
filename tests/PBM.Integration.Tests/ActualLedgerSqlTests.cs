using Microsoft.EntityFrameworkCore;
using PBM.Application;
using PBM.Domain;
using PBM.Infrastructure;
using Xunit;

namespace PBM.Integration.Tests;

[Collection("PBM SQL Integration")]
public sealed class ActualLedgerSqlTests(PbmSqlFixture fixture)
{
    [Fact]
    public async Task Exact_retry_does_not_duplicate_ledger_or_projection()
    {
        if (!fixture.IsEnabled) return;
        await using var db = fixture.CreateContext();
        var service = fixture.CreateLedgerService(db);
        var request = await RequestAsync(db, 7, 125_000m, "DOC-RETRY", "1");

        var first = await service.PostAsync(request);
        var retry = await service.PostAsync(request);

        Assert.False(first.WasDuplicate);
        Assert.True(retry.WasDuplicate);
        Assert.Equal(first.Entry.Id, retry.Entry.Id);
        Assert.Equal(125_000m, retry.ProjectedActual);
        Assert.Equal(1, await db.ActualLedgerEntries.CountAsync(x =>
            x.SourceSystem == "ERP_TEST"
            && x.ExternalDocumentId == "DOC-RETRY"
            && x.ExternalLineId == "1"
            && x.EntryType == ActualLedgerEntryType.Posting));
        Assert.Equal(125_000m, await db.BudgetFacts
            .Where(x => x.Id == retry.ProjectionFactId)
            .Select(x => x.Value)
            .SingleAsync());
    }

    [Fact]
    public async Task Exact_retry_self_heals_a_missing_projection()
    {
        if (!fixture.IsEnabled) return;
        await using var db = fixture.CreateContext();
        var service = fixture.CreateLedgerService(db);
        var request = await RequestAsync(db, 8, 210_000m, "DOC-HEAL", "1");
        var first = await service.PostAsync(request);

        var projection = await db.BudgetFacts.Include(x => x.Dimensions)
            .SingleAsync(x => x.Id == first.ProjectionFactId);
        db.BudgetFactDimensions.RemoveRange(projection.Dimensions);
        db.BudgetFacts.Remove(projection);
        await db.SaveChangesAsync();

        var retry = await service.PostAsync(request);

        Assert.True(retry.WasDuplicate);
        Assert.NotEqual(Guid.Empty, retry.ProjectionFactId);
        Assert.Equal(210_000m, retry.ProjectedActual);
        Assert.Equal(1, await db.ActualLedgerEntries.CountAsync(x =>
            x.SourceSystem == "ERP_TEST"
            && x.ExternalDocumentId == "DOC-HEAL"
            && x.EntryType == ActualLedgerEntryType.Posting));
        Assert.Equal(210_000m, await db.BudgetFacts
            .Where(x => x.Id == retry.ProjectionFactId)
            .Select(x => x.Value)
            .SingleAsync());
    }

    [Fact]
    public async Task Reversal_creates_immutable_counter_entry_and_returns_projection_to_zero()
    {
        if (!fixture.IsEnabled) return;
        await using var db = fixture.CreateContext();
        var service = fixture.CreateLedgerService(db);
        var request = await RequestAsync(db, 9, 350_000m, "DOC-REV", "1");
        var posting = await service.PostAsync(request);

        var reversal = await service.ReverseAsync(posting.Entry.Id, new ReverseActualLedgerRequest("Test source document was cancelled."));
        var reconciliation = await service.ReconcileAsync(fixture.VersionId);

        Assert.False(reversal.WasDuplicate);
        Assert.Equal(ActualLedgerEntryType.Reversal, reversal.Entry.EntryType);
        Assert.Equal(posting.Entry.Id, reversal.Entry.OriginalEntryId);
        Assert.Equal(-350_000m, reversal.Entry.Amount);
        Assert.Equal(0m, reversal.ProjectedActual);
        Assert.Equal(2, await db.ActualLedgerEntries.CountAsync(x => x.ExternalDocumentId == "DOC-REV"));
        Assert.Contains(reconciliation, x =>
            x.PeriodId == fixture.PeriodIds[9]
            && x.MeasureId == fixture.MeasureId
            && x.Status == ActualLedgerReconciliationStatus.Reconciled
            && x.LedgerAmount == 0m
            && x.ProjectedAmount == 0m);
    }

    [Fact]
    public async Task Same_external_key_with_different_payload_is_rejected()
    {
        if (!fixture.IsEnabled) return;
        await using var db = fixture.CreateContext();
        var service = fixture.CreateLedgerService(db);
        var first = await RequestAsync(db, 10, 500_000m, "DOC-CONFLICT", "1");
        var conflicting = first with { Amount = 500_001m };
        await service.PostAsync(first);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(conflicting));

        Assert.Contains("different payload", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.ActualLedgerEntries.CountAsync(x =>
            x.ExternalDocumentId == "DOC-CONFLICT"
            && x.ExternalLineId == "1"
            && x.EntryType == ActualLedgerEntryType.Posting));
    }

    [Fact]
    public async Task Posting_date_must_belong_to_the_selected_fiscal_period()
    {
        if (!fixture.IsEnabled) return;
        await using var db = fixture.CreateContext();
        var service = fixture.CreateLedgerService(db);
        var request = await RequestAsync(db, 11, 100_000m, "DOC-DATE", "1");
        var invalid = request with { PostingDate = request.PostingDate.AddMonths(2) };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.PostAsync(invalid));

        Assert.Contains("outside fiscal period", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Closed_period_rejects_new_Actual_ledger_postings()
    {
        if (!fixture.IsEnabled) return;
        await using var db = fixture.CreateContext();
        var periodId = fixture.PeriodIds[12];
        var period = await db.FiscalPeriods.SingleAsync(x => x.Id == periodId);
        period.IsClosed = true;
        await db.SaveChangesAsync();
        var service = fixture.CreateLedgerService(db);
        var request = await RequestAsync(db, 12, 100_000m, "DOC-CLOSED", "1");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(request));

        Assert.Contains("Closed fiscal periods", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PostActualLedgerRequest> RequestAsync(
        PbmDbContext db,
        int periodSequence,
        decimal amount,
        string documentId,
        string lineId)
    {
        var postingDate = await fixture.GetPostingDateAsync(db, periodSequence);
        return new PostActualLedgerRequest(
            fixture.VersionId,
            fixture.PeriodIds[periodSequence],
            fixture.MeasureId,
            postingDate,
            amount,
            "IRR",
            fixture.Dimensions,
            "ERP_TEST",
            documentId,
            lineId,
            "SQL integration test");
    }
}

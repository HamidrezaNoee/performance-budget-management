using PBM.Domain;

namespace PBM.Application;

public sealed record BudgetFactWriteDecision(bool IsAllowed, string? DenialReason)
{
    public static BudgetFactWriteDecision Allow() => new(true, null);
    public static BudgetFactWriteDecision Deny(string reason) => new(false, reason);
}

public static class BudgetFactWritePolicy
{
    public static BudgetFactWriteDecision Evaluate(BudgetStatus status, bool isLocked, ValueKind valueKind)
    {
        if (status == BudgetStatus.Draft && !isLocked)
        {
            if (valueKind is ValueKind.Budget or ValueKind.Forecast)
                return BudgetFactWriteDecision.Allow();

            return BudgetFactWriteDecision.Deny(
                valueKind == ValueKind.Actual
                    ? "Actual facts cannot be entered through the general budget-entry path. Post actual performance through Actual Ledger, ERP integration, or a controlled import into the approved execution version."
                    : "Commitment facts cannot be entered through the general budget-entry path. Create commitments through the reservation/commitment workflow on the approved budget version.");
        }

        if (status == BudgetStatus.Approved && valueKind is ValueKind.Actual or ValueKind.Commitment)
            return BudgetFactWriteDecision.Allow();

        if (status == BudgetStatus.Approved)
            return BudgetFactWriteDecision.Deny("Approved versions only accept Actual and Commitment execution facts. Budget and Forecast must be changed through a governed revision.");

        if (status == BudgetStatus.Closed)
            return BudgetFactWriteDecision.Deny("Closed budget versions are read-only.");

        if (status is BudgetStatus.Submitted or BudgetStatus.UnderReview)
            return BudgetFactWriteDecision.Deny("Budget versions under workflow review are read-only.");

        if (status == BudgetStatus.Returned)
            return BudgetFactWriteDecision.Deny("Returned versions must be moved back to Draft before editing.");

        if (status == BudgetStatus.Rejected)
            return BudgetFactWriteDecision.Deny("Rejected budget versions are read-only.");

        if (status == BudgetStatus.Revised)
            return BudgetFactWriteDecision.Deny("Superseded budget versions are read-only. Write execution facts to the current approved version.");

        return BudgetFactWriteDecision.Deny("The selected budget version does not allow fact writes in its current state.");
    }
}

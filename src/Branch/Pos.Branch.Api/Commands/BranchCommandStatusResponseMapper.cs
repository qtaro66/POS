using Pos.Application.TableSessions;
using Pos.Contracts.Commands;

namespace Pos.Branch.Api.Commands;

public static class BranchCommandStatusResponseMapper
{
    public const int CurrentSchemaVersion = 1;

    public static BranchCommandStatusResponse Map(
        string branchId,
        string requestId,
        BranchCommandReceipt? receipt)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        requestId = RequireIdentifier(requestId, nameof(requestId));
        if (receipt is null)
        {
            return new BranchCommandStatusResponse(
                CurrentSchemaVersion,
                requestId,
                BranchCommandStatusValues.NoDurableResult,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        if (!string.Equals(receipt.BranchId, branchId, StringComparison.Ordinal) ||
            !string.Equals(receipt.RequestId, requestId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The durable receipt does not match the requested Branch command identity.");
        }

        var status = receipt.Outcome switch
        {
            BranchCommandOutcome.BranchAccepted => BranchCommandStatusValues.BranchAccepted,
            BranchCommandOutcome.Rejected => BranchCommandStatusValues.Rejected,
            _ => throw new InvalidOperationException(
                $"Branch command outcome '{receipt.Outcome}' is not supported.")
        };

        return new BranchCommandStatusResponse(
            CurrentSchemaVersion,
            receipt.RequestId,
            status,
            receipt.TableSessionId.Value,
            receipt.SessionStatus?.ToString(),
            receipt.TableVersion,
            receipt.ErrorCode,
            receipt.ServerReceivedAt,
            receipt.BranchSequence);
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}

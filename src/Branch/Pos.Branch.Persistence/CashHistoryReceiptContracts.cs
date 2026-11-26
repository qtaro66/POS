namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record CashHistoryReceiptEvidence(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    string CashRequestId, string TableSessionId, string ReceiptNumber, DateTimeOffset QueuedAt,
    string CapturedPrintStatus, CashHistoryReceiptSimulation? Simulation);
public sealed record CashHistoryReceiptSimulation(string Status, string? Text);

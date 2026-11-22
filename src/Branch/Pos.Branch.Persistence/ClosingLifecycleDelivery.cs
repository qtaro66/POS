namespace Pos.Branch.Infrastructure.Sqlite;

public enum ClosingLifecycleFailure { TransportUnavailable, CentralRejected, AcknowledgmentUnavailable, ParentUnavailable }
public sealed record ClosingLifecycleLease(string BranchId, string ContextId, string ReportId, string RequestId,
    string Operation, string PayloadJson, string ParentPayloadJson, string LeaseId, string LeaseOwner,
    DateTimeOffset LeaseExpiresAt, int AttemptCount);
public sealed record ClosingLifecycleDelivery(string ContextId, string DeliveryState, int AttemptCount,
    DateTimeOffset AvailableAt, DateTimeOffset? LeaseExpiresAt, DateTimeOffset? DeliveredAt, string? LastFailure);

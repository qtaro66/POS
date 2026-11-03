using System.Text.Json;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchEmployeePermissionCache(string branchId, string path)
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true
    };
    private BranchEmployeePermissionSnapshot? _current;

    public bool IsForBranch(string requestBranchId) => string.Equals(branchId, requestBranchId, StringComparison.Ordinal);

    public bool CanSetSoldOut(string requestBranchId, string employeeId) =>
        IsForBranch(requestBranchId)
        && Volatile.Read(ref _current)?.Employees.Any(entry =>
            entry.Active && entry.SetSoldOut && string.Equals(entry.EmployeeId, employeeId, StringComparison.Ordinal)) == true;

    // The synchronization worker is the only writer.
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) { return; }
        await using var stream = File.OpenRead(path);
        var snapshot = await ReadAsync(stream, cancellationToken);
        if (snapshot is not null) { Volatile.Write(ref _current, snapshot); }
    }

    public async Task<bool> ApplyAsync(Stream stream, CancellationToken cancellationToken)
    {
        var snapshot = await ReadAsync(stream, cancellationToken);
        if (snapshot is null || snapshot.Revision <= (Volatile.Read(ref _current)?.Revision ?? 0)) { return false; }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(output, snapshot, Json, cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
            Volatile.Write(ref _current, snapshot);
            return true;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private async Task<BranchEmployeePermissionSnapshot?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[MaxBytes + 1];
        var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, cancellationToken);
        if (length > MaxBytes) { return null; }

        var snapshot = JsonSerializer.Deserialize<BranchEmployeePermissionSnapshot>(bytes.AsSpan(0, length), Json);
        if (snapshot is not { SchemaVersion: 1, Revision: > 0, Employees.Count: <= 10000 }
            || !string.Equals(snapshot.BranchId, branchId, StringComparison.Ordinal)) { return null; }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        return snapshot.Employees.All(entry => entry?.EmployeeId is { Length: > 0 and <= 128 } id
            && id == id.Trim() && !id.Any(char.IsControl) && ids.Add(id))
            ? snapshot : null;
    }
}

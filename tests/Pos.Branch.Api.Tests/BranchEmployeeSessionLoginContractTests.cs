using System.Text.Json;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeSessionLoginContractTests
{
    [Fact]
    public void Public_request_json_contains_only_version_and_pin()
    {
        var request = new BranchEmployeeSessionLoginRequest(1, "1234");
        var json = JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(2, root.EnumerateObject().Count());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("1234", root.GetProperty("pin").GetString());
        Assert.False(root.TryGetProperty("branchId", out _));
        Assert.False(root.TryGetProperty("deviceId", out _));
        Assert.False(root.TryGetProperty("employeeId", out _));
    }

    [Fact]
    public void Web_json_deserializes_the_versioned_request_shape()
    {
        const string json = """
            {"schemaVersion":1,"pin":"1234"}
            """;

        var request = JsonSerializer.Deserialize<BranchEmployeeSessionLoginRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var parsed = Assert.IsType<BranchEmployeeSessionLoginRequest>(request);
        Assert.Equal(1, parsed.SchemaVersion);
        Assert.Equal("1234", parsed.Pin);
    }
}

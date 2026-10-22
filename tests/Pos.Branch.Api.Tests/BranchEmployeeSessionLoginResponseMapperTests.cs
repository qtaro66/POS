using System.Text.Json;
using Pos.Application.Security;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeSessionLoginResponseMapperTests
{
    private static readonly DateTimeOffset ExpiresAt =
        new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Login_maps_to_versioned_public_response()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var login = Login(issued);

        var response = BranchEmployeeSessionLoginResponseMapper.Map(login);

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal("EMPLOYEE-01", response.EmployeeId);
        Assert.Equal(issued.SessionId, response.SessionId);
        Assert.Equal(issued.Token, response.SessionToken);
        Assert.Equal(ExpiresAt, response.ExpiresAt);
    }

    [Fact]
    public void Public_json_contains_only_version_employee_and_session_material()
    {
        var response = BranchEmployeeSessionLoginResponseMapper.Map(
            Login(BranchEmployeeSessionTokenFormat.Generate()));
        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(5, root.EnumerateObject().Count());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("EMPLOYEE-01", root.GetProperty("employeeId").GetString());
        Assert.Equal(response.SessionId, root.GetProperty("sessionId").GetString());
        Assert.Equal(
            response.SessionToken,
            root.GetProperty("sessionToken").GetString());
        Assert.Equal(
            ExpiresAt,
            root.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.False(root.TryGetProperty("branchId", out _));
        Assert.False(root.TryGetProperty("deviceId", out _));
        Assert.False(root.TryGetProperty("pin", out _));
    }

    [Fact]
    public void Noncanonical_session_identifier_or_token_is_rejected()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();

        Assert.Throws<ArgumentException>(() =>
            BranchEmployeeSessionLoginResponseMapper.Map(
                Login(issued) with { SessionId = "not-canonical" }));
        Assert.Throws<ArgumentException>(() =>
            BranchEmployeeSessionLoginResponseMapper.Map(
                Login(issued) with { Token = "es1.invalid" }));
    }

    [Fact]
    public void Token_for_different_session_is_rejected()
    {
        var first = BranchEmployeeSessionTokenFormat.Generate();
        var second = BranchEmployeeSessionTokenFormat.Generate();

        Assert.Throws<ArgumentException>(() =>
            BranchEmployeeSessionLoginResponseMapper.Map(
                Login(first) with { Token = second.Token }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_employee_identifier_is_rejected(string employeeId)
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();

        Assert.Throws<ArgumentException>(() =>
            BranchEmployeeSessionLoginResponseMapper.Map(
                Login(issued) with { EmployeeId = employeeId }));
    }

    [Fact]
    public void Empty_trusted_identity_is_rejected()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();

        Assert.Throws<ArgumentException>(() =>
            BranchEmployeeSessionLoginResponseMapper.Map(
                Login(issued) with { BranchId = " " }));
        Assert.Throws<ArgumentException>(() =>
            BranchEmployeeSessionLoginResponseMapper.Map(
                Login(issued) with { DeviceId = string.Empty }));
    }

    [Fact]
    public void Non_utc_expiry_is_rejected()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();

        Assert.Throws<ArgumentException>(() =>
            BranchEmployeeSessionLoginResponseMapper.Map(
                Login(issued) with
                {
                    ExpiresAt = ExpiresAt.ToOffset(TimeSpan.FromHours(7))
                }));
    }

    [Fact]
    public void Null_login_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchEmployeeSessionLoginResponseMapper.Map(null!));
    }

    private static BranchEmployeeSessionLoginResult Login(
        IssuedBranchEmployeeSessionToken issued) =>
        new(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            issued.SessionId,
            issued.Token,
            ExpiresAt);
}

using System.Text.Json;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingRedemptionResponseMapperTests
{
    [Fact]
    public void Redeemed_device_maps_to_versioned_public_response()
    {
        var deviceId = BranchDeviceIdentifierFormat.Generate();
        var credential = BranchDeviceCredentialFormat.Generate();

        var response = BranchPairingRedemptionResponseMapper.Map(
            new RedeemedBranchDevice(deviceId, credential.Token));

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal(deviceId, response.DeviceId);
        Assert.Equal(credential.Token, response.DeviceCredential);
    }

    [Fact]
    public void Noncanonical_device_identifier_is_rejected()
    {
        var credential = BranchDeviceCredentialFormat.Generate();

        Assert.Throws<ArgumentException>(() =>
            BranchPairingRedemptionResponseMapper.Map(
                new RedeemedBranchDevice("not-canonical", credential.Token)));
    }

    [Fact]
    public void Noncanonical_device_credential_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            BranchPairingRedemptionResponseMapper.Map(
                new RedeemedBranchDevice(
                    BranchDeviceIdentifierFormat.Generate(),
                    "v1.invalid")));
    }

    [Fact]
    public void Public_response_json_contains_only_version_device_and_credential()
    {
        var credential = BranchDeviceCredentialFormat.Generate();
        var response = BranchPairingRedemptionResponseMapper.Map(
            new RedeemedBranchDevice(
                BranchDeviceIdentifierFormat.Generate(),
                credential.Token));
        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(response.DeviceId, root.GetProperty("deviceId").GetString());
        Assert.Equal(
            response.DeviceCredential,
            root.GetProperty("deviceCredential").GetString());
        Assert.False(root.TryGetProperty("branchId", out _));
        Assert.False(root.TryGetProperty("pairingId", out _));
        Assert.False(root.TryGetProperty("oneTimeToken", out _));
        Assert.False(root.TryGetProperty("employeeId", out _));
    }

    [Fact]
    public void Public_request_json_has_the_versioned_redemption_shape()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var request = new BranchPairingRedemptionRequest(
            1,
            "BRANCH-01",
            pairing.PairingId,
            pairing.Token,
            "Dining Tablet 01");
        var json = JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(5, root.EnumerateObject().Count());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("BRANCH-01", root.GetProperty("branchId").GetString());
        Assert.Equal(pairing.PairingId, root.GetProperty("pairingId").GetString());
        Assert.Equal(pairing.Token, root.GetProperty("oneTimeToken").GetString());
        Assert.Equal(
            "Dining Tablet 01",
            root.GetProperty("deviceDisplayName").GetString());
    }
}

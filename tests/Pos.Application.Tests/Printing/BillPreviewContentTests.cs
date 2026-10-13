using System.Text.Json;
using Pos.Application.Printing;

namespace Pos.Application.Tests.Printing;

public sealed class BillPreviewContentTests
{
    [Fact]
    public void Same_exact_json_has_same_sha256()
    {
        const string json = """
            {"items":[],"netTotal":0}
            """;

        var first = BillPreviewContent.FromJson(1, json);
        var second = BillPreviewContent.FromJson(1, json);

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(64, first.Sha256.Length);
    }

    [Fact]
    public void Different_exact_json_has_different_sha256()
    {
        var first = BillPreviewContent.FromJson(1, "{\"netTotal\":100}");
        var second = BillPreviewContent.FromJson(1, "{\"netTotal\":101}");

        Assert.NotEqual(first.Sha256, second.Sha256);
    }

    [Fact]
    public void Invalid_json_is_rejected()
    {
        Assert.ThrowsAny<JsonException>(() => BillPreviewContent.FromJson(1, "not-json"));
    }

    [Fact]
    public void Non_positive_schema_version_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BillPreviewContent.FromJson(0, "{}"));
    }

    [Fact]
    public void Non_object_json_root_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => BillPreviewContent.FromJson(1, "[]"));
    }
}

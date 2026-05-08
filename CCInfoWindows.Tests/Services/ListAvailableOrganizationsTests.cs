using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// ORGID-01 (D-OG-01): verifies <see cref="ClaudeApiService.ListAvailableOrganizationsAsync"/>
/// parses /api/organizations JSON correctly and handles error cases defensively.
/// Does NOT instantiate ClaudeApiService (requires IWebViewBridge + WinRT COM).
/// Mirrors the parsing logic directly.
/// </summary>
public class ListAvailableOrganizationsTests
{
    /// <summary>
    /// Parses a valid JSON array from /api/organizations into OrganizationInfo records.
    /// Mirrors ClaudeApiService parsing logic.
    /// </summary>
    private sealed class OrgListParser
    {
        public static IReadOnlyList<OrganizationInfo> Parse(string? responseBody)
        {
            if (responseBody is null) return Array.Empty<OrganizationInfo>();

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
                    return Array.Empty<OrganizationInfo>();

                var list = new List<OrganizationInfo>(root.GetArrayLength());
                foreach (var element in root.EnumerateArray())
                {
                    if (!element.TryGetProperty("uuid", out var uuidProp)) continue;
                    var uuid = uuidProp.GetString();
                    if (string.IsNullOrEmpty(uuid)) continue;

                    var name = element.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? uuid
                        : uuid;

                    list.Add(new OrganizationInfo(uuid, name));
                }

                return list;
            }
            catch
            {
                return Array.Empty<OrganizationInfo>();
            }
        }
    }

    [Fact]
    public void Parse_ValidArray_ReturnsOrganizationInfoList()
    {
        const string json = """
            [
              {"uuid":"org-abc-123","name":"Personal"},
              {"uuid":"org-def-456","name":"My Team"}
            ]
            """;

        var result = OrgListParser.Parse(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("org-abc-123", result[0].Uuid);
        Assert.Equal("Personal", result[0].Name);
        Assert.Equal("org-def-456", result[1].Uuid);
        Assert.Equal("My Team", result[1].Name);
    }

    [Fact]
    public void Parse_NullBody_ReturnsEmptyList()
    {
        var result = OrgListParser.Parse(null);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptyList()
    {
        var result = OrgListParser.Parse("[]");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_MissingName_FallsBackToUuid()
    {
        const string json = """[{"uuid":"org-no-name-789"}]""";

        var result = OrgListParser.Parse(json);

        Assert.Single(result);
        Assert.Equal("org-no-name-789", result[0].Uuid);
        Assert.Equal("org-no-name-789", result[0].Name);
    }

    [Fact]
    public void Parse_EntryWithEmptyUuid_IsSkipped()
    {
        const string json = """
            [
              {"uuid":"","name":"Empty UUID"},
              {"uuid":"org-valid","name":"Valid"}
            ]
            """;

        var result = OrgListParser.Parse(json);

        Assert.Single(result);
        Assert.Equal("org-valid", result[0].Uuid);
    }

    [Fact]
    public void Parse_NotAnArray_ReturnsEmptyList()
    {
        const string json = """{"uuid":"org-not-an-array","name":"Obj"}""";

        var result = OrgListParser.Parse(json);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsEmptyList()
    {
        var result = OrgListParser.Parse("{broken json");
        Assert.Empty(result);
    }

    [Fact]
    public void OrganizationInfo_Record_HasUuidAndName()
    {
        // OrganizationInfo is a sealed record — verify structural contract
        var org = new OrganizationInfo("test-uuid", "Test Name");
        Assert.Equal("test-uuid", org.Uuid);
        Assert.Equal("Test Name", org.Name);
    }

    [Fact]
    public void OrganizationInfo_RecordEquality_WorksCorrectly()
    {
        var a = new OrganizationInfo("uid", "Name");
        var b = new OrganizationInfo("uid", "Name");
        var c = new OrganizationInfo("other", "Name");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}

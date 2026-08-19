using System.Text.Json;
using CCInfoWindows.Messages;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.TestSupport;
using CommunityToolkit.Mvvm.Messaging;
using Moq;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// ORGID-01 (D-OG-01): the real <see cref="ClaudeApiService.ListAvailableOrganizationsAsync"/> driven
/// through a mocked <see cref="IWebViewBridge"/>, plus the first-org auto-pick the private
/// TryMigrateOrgIdAsync derives from its result.
///
/// Finding 31: this file used to hold a private OrgListParser — a second copy of the production
/// parsing loop — justified with "requires IWebViewBridge + WinRT COM", which ClaudeApiServiceTests
/// disproves by building the real service from the same mock in the same test project. Removing the
/// empty-uuid guard from production left all nine tests green while the auto-pick persisted "" and
/// every following usage fetch went to /api/organizations//usage.
/// </summary>
[Collection("WeakReferenceMessenger")]
public class ListAvailableOrganizationsTests : ClaudeApiServiceTestBase
{
    private const string OrganizationsUrl = ClaudeAiUrlPolicy.Origin + "/api/organizations";
    private const string UsagePathSuffix = "/usage";
    private const string ValidOrgId = "org-valid";

    public ListAvailableOrganizationsTests() : base("ccinfo_orglist_")
    {
    }

    private void RespondToOrganizationsWith(string? responseBody)
        => BridgeMock.Setup(b => b.FetchJsonAsync(OrganizationsUrl)).ReturnsAsync(responseBody);

    [Fact]
    public async Task ListAvailableOrganizations_ParsesEveryEntryFromTheOrganizationsEndpoint()
    {
        RespondToOrganizationsWith("""
            [
              {"uuid":"org-abc-123","name":"Personal"},
              {"uuid":"org-def-456","name":"My Team"}
            ]
            """);

        var result = await CreateService().ListAvailableOrganizationsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("org-abc-123", result[0].Uuid);
        Assert.Equal("Personal", result[0].Name);
        Assert.Equal("org-def-456", result[1].Uuid);
        Assert.Equal("My Team", result[1].Name);

        // The endpoint itself is part of the contract — a mirrored parser could never assert it.
        BridgeMock.Verify(b => b.FetchJsonAsync(OrganizationsUrl), Times.Once);
    }

    [Fact]
    public async Task ListAvailableOrganizations_WithoutAnInitializedBridge_ReturnsEmptyWithoutFetching()
    {
        BridgeMock.Setup(b => b.IsInitialized).Returns(false);

        Assert.Empty(await CreateService().ListAvailableOrganizationsAsync());

        BridgeMock.Verify(b => b.FetchJsonAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ListAvailableOrganizations_WithANullResponseBody_ReturnsEmpty()
    {
        RespondToOrganizationsWith(null);

        Assert.Empty(await CreateService().ListAvailableOrganizationsAsync());
    }

    [Fact]
    public async Task ListAvailableOrganizations_WithAnEmptyArray_ReturnsEmpty()
    {
        RespondToOrganizationsWith("[]");

        Assert.Empty(await CreateService().ListAvailableOrganizationsAsync());
    }

    [Fact]
    public async Task ListAvailableOrganizations_WithoutAName_FallsBackToTheUuid()
    {
        RespondToOrganizationsWith("""[{"uuid":"org-no-name-789"}]""");

        var result = await CreateService().ListAvailableOrganizationsAsync();

        Assert.Single(result);
        Assert.Equal("org-no-name-789", result[0].Uuid);
        Assert.Equal("org-no-name-789", result[0].Name);
    }

    [Fact]
    public async Task ListAvailableOrganizations_WithANullName_FallsBackToTheUuid()
    {
        RespondToOrganizationsWith($$"""[{"uuid":"{{ValidOrgId}}","name":null}]""");

        var result = await CreateService().ListAvailableOrganizationsAsync();

        Assert.Single(result);
        Assert.Equal(ValidOrgId, result[0].Name);
    }

    [Fact]
    public async Task ListAvailableOrganizations_SkipsEntriesWithoutAUsableUuid()
    {
        RespondToOrganizationsWith($$"""
            [
              {"uuid":"","name":"Empty uuid"},
              {"name":"No uuid at all"},
              {"uuid":"{{ValidOrgId}}","name":"Valid"}
            ]
            """);

        var result = await CreateService().ListAvailableOrganizationsAsync();

        Assert.Single(result);
        Assert.Equal(ValidOrgId, result[0].Uuid);
    }

    [Fact]
    public async Task ListAvailableOrganizations_WithAJsonObjectInsteadOfAnArray_ReturnsEmpty()
    {
        RespondToOrganizationsWith("""{"uuid":"org-not-an-array","name":"Obj"}""");

        Assert.Empty(await CreateService().ListAvailableOrganizationsAsync());
    }

    [Fact]
    public async Task ListAvailableOrganizations_WithMalformedJson_ReturnsEmpty()
    {
        RespondToOrganizationsWith("{broken json");

        Assert.Empty(await CreateService().ListAvailableOrganizationsAsync());
    }

    [Fact]
    public async Task ListAvailableOrganizations_OnSessionExpiry_ReturnsEmptyAndAsksForReauthentication()
    {
        BridgeMock.Setup(b => b.FetchJsonAsync(OrganizationsUrl))
                   .ThrowsAsync(new SessionExpiredException());

        var authStates = new List<bool>();
        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (_, m) => authStates.Add(m.Value));

        Assert.Empty(await CreateService().ListAvailableOrganizationsAsync());

        Assert.Equal([false], authStates);
    }

    [Fact]
    public async Task FetchUsage_WithoutAStoredOrgId_AutoPicksTheFirstUsableOrgForTheUsageUrl()
    {
        // The failure the empty-uuid guard prevents: the auto-pick takes orgs[0], so an entry the parser
        // should have dropped becomes the persisted org id and every usage fetch of that session goes to
        // /api/organizations//usage — a 404 loop the user can only escape by logging out.
        CredentialMock.Setup(c => c.GetOrganizationId()).Returns((string?)null);
        RespondToOrganizationsWith($$"""
            [
              {"uuid":"","name":"Empty uuid"},
              {"uuid":"{{ValidOrgId}}","name":"Valid"}
            ]
            """);

        string? usageUrl = null;
        BridgeMock
            .Setup(b => b.FetchJsonAsync(It.Is<string>(url => url.EndsWith(UsagePathSuffix, StringComparison.Ordinal))))
            .Callback<string>(url => usageUrl = url)
            .ReturnsAsync(EmptyUsageJson);

        var result = await CreateService().FetchUsageAsync();

        Assert.NotNull(result);
        CredentialMock.Verify(c => c.SaveOrganizationId(ValidOrgId), Times.Once);
        Assert.Equal($"{OrganizationsUrl}/{ValidOrgId}{UsagePathSuffix}", usageUrl);
    }

    [Fact]
    public async Task FetchUsage_WhenNoOrgHasAUsableUuid_FailsInsteadOfFetchingAnEmptyOrgUrl()
    {
        CredentialMock.Setup(c => c.GetOrganizationId()).Returns((string?)null);
        RespondToOrganizationsWith("""[{"uuid":"","name":"Empty uuid"}]""");

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchUsageAsync());

        CredentialMock.Verify(c => c.SaveOrganizationId(It.IsAny<string>()), Times.Never);
        BridgeMock.Verify(
            b => b.FetchJsonAsync(It.Is<string>(url => url.EndsWith(UsagePathSuffix, StringComparison.Ordinal))),
            Times.Never);
    }

    private static string EmptyUsageJson => JsonSerializer.Serialize(new
    {
        five_hour = new { utilization = 0.0, resets_at = DateTimeOffset.UtcNow.AddHours(1).ToString("o") }
    });
}

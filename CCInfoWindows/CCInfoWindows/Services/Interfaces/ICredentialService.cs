namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Secure credential storage via Windows Credential Manager (DPAPI).
/// Implementation deferred to Plan 02.
/// </summary>
public interface ICredentialService
{
    void SaveSessionToken(string token);

    string? GetSessionToken();

    void ClearCredentials();

    void SaveOrganizationId(string orgId);

    string? GetOrganizationId();

    /// <summary>
    /// Drops only the cached organization id, forcing re-resolution via /api/organizations.
    /// </summary>
    void ClearOrganizationId();

    /// <summary>
    /// Returns true if a session token is stored.
    /// </summary>
    bool HasValidToken();
}

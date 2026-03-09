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
}

using AdysTech.CredentialManager;
using CCInfoWindows.Services.Interfaces;
using System.Net;

namespace CCInfoWindows.Services;

/// <summary>
/// Windows Credential Manager wrapper for secure session token storage (DPAPI-encrypted).
/// </summary>
public class CredentialService : ICredentialService
{
    private const string CredentialTarget = "CCInfoWindows/claude-session";

    public void SaveSessionToken(string token)
    {
        var cred = new NetworkCredential("sessionKey", token);
        CredentialManager.SaveCredentials(CredentialTarget, cred);
    }

    public string? GetSessionToken()
    {
        var cred = CredentialManager.GetCredentials(CredentialTarget);
        return cred?.Password;
    }

    public void ClearCredentials()
    {
        try
        {
            CredentialManager.RemoveCredentials(CredentialTarget);
        }
        catch (Exception)
        {
            // Credential may not exist -- safe to ignore
        }
    }
}

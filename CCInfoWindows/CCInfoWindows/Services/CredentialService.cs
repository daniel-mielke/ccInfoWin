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
    private const string OrgCredentialTarget = "CCInfoWindows/claude-org";
    private const string SessionKeyUsername = "sessionKey";
    private const string OrgIdUsername = "orgId";

    public void SaveSessionToken(string token)
    {
        var cred = new NetworkCredential(SessionKeyUsername, token);
        CredentialManager.SaveCredentials(CredentialTarget, cred);
    }

    public string? GetSessionToken()
    {
        var cred = CredentialManager.GetCredentials(CredentialTarget);
        return cred?.Password;
    }

    public void ClearCredentials()
    {
        Remove(CredentialTarget);
        Remove(OrgCredentialTarget);
    }

    public void ClearOrganizationId() => Remove(OrgCredentialTarget);

    private static void Remove(string target)
    {
        try
        {
            CredentialManager.RemoveCredentials(target);
        }
        catch (Exception)
        {
            // Credential may not exist -- safe to ignore
        }
    }

    public void SaveOrganizationId(string orgId)
    {
        var cred = new NetworkCredential(OrgIdUsername, orgId);
        CredentialManager.SaveCredentials(OrgCredentialTarget, cred);
    }

    public string? GetOrganizationId()
    {
        var cred = CredentialManager.GetCredentials(OrgCredentialTarget);
        return cred?.Password;
    }

    public bool HasValidToken()
    {
        return !string.IsNullOrEmpty(GetSessionToken());
    }
}

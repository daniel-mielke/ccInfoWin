namespace CCInfoWindows.Models;

/// <summary>
/// ORGID-01 (D-OG-01): DTO for entries returned by /api/organizations.
/// Used by the Settings → Account → Re-detect organization flow.
/// The API returns more fields (capabilities[], settings, member_role)
/// but only Uuid + Name are relevant for the picker UI.
/// </summary>
public sealed record OrganizationInfo(string Uuid, string Name);

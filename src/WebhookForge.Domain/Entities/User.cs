using WebhookForge.Domain.Common;
using WebhookForge.Domain.Enums;

namespace WebhookForge.Domain.Entities;

/// <summary>
/// Represents a registered user of the platform.
/// A user can own multiple workspaces and be a member of others.
/// </summary>
public class User : BaseEntity
{
    /// <summary>Unique, lowercased email address used for login.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the user's password. Never expose in API responses.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Human-readable name shown in the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>When false, login is rejected even with correct credentials.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC time of the most recent successful login. Null until first login.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Selected AI provider. Null means AI not configured.</summary>
    public AiProvider? AiProvider { get; set; }

    /// <summary>API key for the selected AI provider. Never returned to frontend.</summary>
    public string? AiApiKey { get; set; }

    // ── Navigation ──────────────────────────────────────────────
    /// <summary>Workspaces where this user is the owner.</summary>
    public ICollection<Workspace> OwnedWorkspaces { get; set; } = new List<Workspace>();

    /// <summary>Membership records linking this user to workspaces they belong to.</summary>
    public ICollection<WorkspaceMember> WorkspaceMemberships { get; set; } = new List<WorkspaceMember>();

    /// <summary>Active and revoked refresh tokens for this user.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

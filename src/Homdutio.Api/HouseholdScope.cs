using System.Security.Claims;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Api;

/// <summary>
/// The single canonical place the cross-household boundary is derived (S-07). Every endpoint that acts on
/// household-owned data resolves its caller and loads scoped entities through here, so the rule — "scope to
/// the caller's own household, derived server-side from the JWT, never a client-supplied id" — lives in one
/// location instead of being re-implemented per endpoint. Previously <c>TaskEndpoints</c> and
/// <c>HouseholdEndpoints</c> each carried a private copy of this logic and its own <see cref="CallerContext"/>
/// record; collapsing them here removes the drift vector. The route-coverage guard
/// (<c>RouteIsolationCoverageTests</c>) ensures any new household-scoped route is exercised by the isolation
/// sweep that proves this boundary holds.
/// </summary>
internal static class HouseholdScope
{
    /// <summary>
    /// Resolves the caller's household membership from the JWT <c>sub</c> claim. Null when the principal
    /// carries no <c>sub</c> or has no membership — callers surface this as 404 (no board / no household),
    /// consistent with the foreign-household rule.
    /// </summary>
    public static async Task<CallerContext?> ResolveCallerAsync(ClaimsPrincipal principal, ApplicationDbContext db)
    {
        var userId = principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var member = await db.HouseholdMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.UserId == userId);

        return member is null ? null : new CallerContext(member.HouseholdId, member.Role, userId);
    }

    /// <summary>Loads a task scoped to the caller's household — foreign or missing id → null (no existence leak).</summary>
    public static Task<HouseholdTask?> LoadScopedTaskAsync(ApplicationDbContext db, Guid id, Guid householdId) =>
        db.HouseholdTasks.SingleOrDefaultAsync(t => t.Id == id && t.HouseholdId == householdId);
}

/// <summary>The caller's resolved household scope: which household they act in, their role, and their user id.</summary>
internal sealed record CallerContext(Guid HouseholdId, HouseholdRole Role, string UserId);

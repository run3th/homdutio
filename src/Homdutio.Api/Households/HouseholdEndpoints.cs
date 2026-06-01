using System.Security.Claims;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Api.Households;

/// <summary>
/// Household membership endpoints (S-02). The acting user comes from the JWT <c>sub</c> claim and the
/// household is derived server-side — neither endpoint accepts a client-supplied household id. This
/// establishes the cross-household isolation pattern (server-derived scope, never trust a client id) at
/// the first domain endpoint; S-07 generalises it into a systematic sweep.
/// </summary>
public static class HouseholdEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/households").RequireAuthorization();

        // GET /api/households/me — the caller's household, or 204 when they have none yet.
        group.MapGet("/me", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");

            var member = await db.HouseholdMembers
                .AsNoTracking()
                .Include(m => m.Household)
                .SingleOrDefaultAsync(m => m.UserId == userId);

            return member is null
                ? Results.NoContent()
                : Results.Ok(new HouseholdResponse(member.HouseholdId, member.Household!.Name, member.Role.ToString()));
        });

        // POST /api/households — create a household and make the caller its first admin.
        group.MapPost("/", async (CreateHouseholdRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Name"] = ["A household name is required."],
                });
            }

            var userId = principal.FindFirstValue("sub")!;

            var alreadyMember = await db.HouseholdMembers.AnyAsync(m => m.UserId == userId);
            if (alreadyMember)
            {
                return Results.Conflict(new { message = "You already belong to a household." });
            }

            var household = new Household
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
            };

            var member = new HouseholdMember
            {
                Id = Guid.NewGuid(),
                HouseholdId = household.Id,
                UserId = userId,
                Role = HouseholdRole.Admin,
                JoinedAtUtc = DateTime.UtcNow,
            };

            db.Households.Add(household);
            db.HouseholdMembers.Add(member);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/households/me",
                new HouseholdResponse(household.Id, household.Name, member.Role.ToString()));
        });

        return app;
    }
}

public sealed record CreateHouseholdRequest(string Name);

public sealed record HouseholdResponse(Guid Id, string Name, string Role);

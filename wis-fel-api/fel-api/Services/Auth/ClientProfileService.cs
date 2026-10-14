using System.Security.Claims;
using fel_api.Data;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Auth;

public sealed record ClientCompanyAccess(Guid Id, string Name);
public sealed record ClientRoleAccess(Guid Id, string Name, IReadOnlyList<ClientCompanyAccess> Companies);
public sealed record ClientTenantAccess(Guid Id, string Name, IReadOnlyList<ClientRoleAccess> Roles);
public sealed record ClientProfileResponse(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<ClientTenantAccess> Tenants);

public sealed class ClientProfileService(FelApiDbContext dbContext)
{
    public async Task<ClientProfileResponse?> GetForPrincipalAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var subject = principal.FindFirstValue("sub");
        var issuer = principal.FindFirstValue("iss")
            ?? principal.FindFirst(x => x.Type == "sub")?.Issuer;
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer))
        {
            return null;
        }

        var user = await dbContext.ExternalUserIdentities
            .Where(x => x.Issuer == issuer && x.Subject == subject)
            .Select(x => x.UserAccount)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var memberships = await dbContext.TenantMemberships
            .Where(x => x.UserAccountId == user.Id && x.IsActive)
            .Select(x => new { x.TenantId, TenantName = x.Tenant.Name })
            .ToListAsync(cancellationToken);
        var userRoles = await dbContext.TenantUserRoles
            .Where(x => x.UserAccountId == user.Id)
            .Select(x => new { x.TenantId, RoleId = x.TenantRoleId, RoleName = x.TenantRole.Name })
            .ToListAsync(cancellationToken);
        var roleCompanies = await dbContext.TenantRoleCompanies
            .Where(x => dbContext.TenantUserRoles.Any(r =>
                r.TenantId == x.TenantId && r.TenantRoleId == x.TenantRoleId && r.UserAccountId == user.Id))
            .Select(x => new { x.TenantId, x.TenantRoleId, x.CompanyId, CompanyName = x.Company.Name })
            .ToListAsync(cancellationToken);

        var tenants = memberships.Select(membership =>
        {
            var roles = userRoles
                .Where(role => role.TenantId == membership.TenantId)
                .Select(role => new ClientRoleAccess(
                    role.RoleId,
                    role.RoleName,
                    roleCompanies
                        .Where(company => company.TenantId == role.TenantId && company.TenantRoleId == role.RoleId)
                        .Select(company => new ClientCompanyAccess(company.CompanyId, company.CompanyName))
                        .ToArray()))
                .ToArray();

            return new ClientTenantAccess(membership.TenantId, membership.TenantName, roles);
        }).ToArray();

        return new ClientProfileResponse(user.Id, user.Email, user.DisplayName, tenants);
    }
}
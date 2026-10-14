using fel_api.Auth;
using fel_api.Data;
using fel_api.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Auth;

public sealed class PlatformAuthenticationService(
    FelApiDbContext dbContext,
    IPasswordHasher<UserAccount> passwordHasher,
    RefreshTokenService refreshTokenService)
{
    public async Task<IssuedTokenPair?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await dbContext.UserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail && x.IsActive, cancellationToken);

        if (user?.PasswordHash is null)
        {
            return null;
        }

        var isPlatformAdministrator = await dbContext.PlatformAdministrators
            .AnyAsync(x => x.UserAccountId == user.Id, cancellationToken);
        if (!isPlatformAdministrator)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await refreshTokenService.IssueForPlatformUserAsync(user, cancellationToken);
    }
}
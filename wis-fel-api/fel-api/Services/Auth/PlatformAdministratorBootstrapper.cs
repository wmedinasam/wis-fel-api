using fel_api.Data;
using fel_api.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Auth;

public sealed class PlatformAdministratorBootstrapper(
    FelApiDbContext dbContext,
    IPasswordHasher<UserAccount> passwordHasher)
{
    public async Task CreateFirstAdministratorAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (await dbContext.PlatformAdministrators.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("A platform administrator already exists.");
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail) || password.Length < 12)
        {
            throw new ArgumentException("Email and a password of at least 12 characters are required.");
        }

        if (await dbContext.UserAccounts.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw new InvalidOperationException("An account already exists with this email.");
        }

        var user = new UserAccount
        {
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = email.Trim(),
            IsActive = true
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        dbContext.UserAccounts.Add(user);
        dbContext.PlatformAdministrators.Add(new PlatformAdministrator { UserAccountId = user.Id });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
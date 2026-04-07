using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SimpleExpandedPaperlessImporter.Data;

namespace SimpleExpandedPaperlessImporter.Services;

/// <summary>
/// Handles user creation and password validation using ASP.NET Core PasswordHasher (PBKDF2).
/// </summary>
public class AuthService(IServiceScopeFactory scopeFactory, ILogger<AuthService> logger)
{
    // ASP.NET Core PasswordHasher uses PBKDF2 with HMAC-SHA512, 100k iterations
    private static readonly PasswordHasher<AppUser> Hasher = new();

    /// <summary>
    /// Called on first startup: creates the default admin user if no users exist.
    /// </summary>
    public async Task EnsureDefaultUserAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Users.AnyAsync()) return;

        const string defaultUsername = "admin";
        const string defaultPassword = "admin";

        var user = new AppUser { Username = defaultUsername };
        user.PasswordHash = Hasher.HashPassword(user, defaultPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        logger.LogWarning(
            "Default admin user created (username: '{User}', password: '{Pass}'). " +
            "Please change the password immediately via the settings page!",
            defaultUsername, defaultPassword);
    }

    /// <summary>Validates credentials. Returns the user on success, null on failure.</summary>
    public async Task<AppUser?> ValidateAsync(string username, string password)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Username.ToLower() == username.ToLower());

        if (user is null) return null;

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        // Rehash on success if needed (algorithm upgrade)
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await db.SaveChangesAsync();
        }

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Changes the password for an existing user.</summary>
    public async Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        var user = await ValidateAsync(username, currentPassword);
        if (user is null) return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbUser = await db.Users.FindAsync(user.Id);
        if (dbUser is null) return false;

        dbUser.PasswordHash = Hasher.HashPassword(dbUser, newPassword);
        await db.SaveChangesAsync();
        logger.LogInformation("Password changed for user '{Username}'", username);
        return true;
    }
}

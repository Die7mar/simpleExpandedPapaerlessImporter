namespace SimpleExpandedPaperlessImporter.Data;

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    /// <summary>PBKDF2 hash via ASP.NET Core PasswordHasher.</summary>
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsAdmin { get; set; } = true;
}

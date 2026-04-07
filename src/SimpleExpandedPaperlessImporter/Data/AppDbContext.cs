using Microsoft.EntityFrameworkCore;

namespace SimpleExpandedPaperlessImporter.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ImportJobEntity> ImportJobs => Set<ImportJobEntity>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<CorrespondentSettings> CorrespondentSettings => Set<CorrespondentSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportJobEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.State).HasConversion<string>();
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.State);
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<CorrespondentSettings>(e =>
        {
            e.HasKey(x => x.CorrespondentId);
        });
    }
}

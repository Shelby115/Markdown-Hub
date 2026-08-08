using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Data.Entities.Admin;
using MarkdownHub.Api.Data.Entities.Auth;

namespace MarkdownHub.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<FolderPermission> FolderPermissions => Set<FolderPermission>();
    public DbSet<PageMetadata> Pages => Set<PageMetadata>();
    public DbSet<PageLink> PageLinks => Set<PageLink>();
    public DbSet<ConflictFile> ConflictFiles => Set<ConflictFile>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<BackupRecord> Backups => Set<BackupRecord>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<AuthenticationProvider> AuthenticationProviders => Set<AuthenticationProvider>();
    public DbSet<AuthenticationIdentity> AuthenticationIdentities => Set<AuthenticationIdentity>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<GenerationPool> GenerationPools => Set<GenerationPool>();
    public DbSet<GenerationPoolEntry> GenerationPoolEntries => Set<GenerationPoolEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.NormalizedUsername).IsUnique();
        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Username).IsUnique();

        modelBuilder.Entity<AuthenticationProvider>()
            .HasIndex(p => p.Name).IsUnique();

        modelBuilder.Entity<AuthenticationIdentity>()
            .HasIndex(i => new { i.AuthenticationProviderId, i.Subject }).IsUnique();
        modelBuilder.Entity<AuthenticationIdentity>()
            .HasIndex(i => i.UserId);
        modelBuilder.Entity<AuthenticationIdentity>()
            .HasOne(i => i.User)
            .WithMany(u => u.AuthenticationIdentities)
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AuthenticationIdentity>()
            .HasOne(i => i.Provider)
            .WithMany()
            .HasForeignKey(i => i.AuthenticationProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Session>()
            .HasIndex(s => s.UserId);

        modelBuilder.Entity<FolderPermission>()
            .HasIndex(p => new { p.AppUserId, p.FolderPath }).IsUnique();

        // Unique only among *active* (non-soft-deleted) pages - a soft-deleted page's history
        // must never block a brand new page being created at the same path later; see
        // PageMetadata.IsDeleted and MarkdownFileService.IndexPageAsync.
        modelBuilder.Entity<PageMetadata>()
            .HasIndex(p => p.RelativePath).IsUnique().HasFilter("IsDeleted = 0");
        modelBuilder.Entity<PageMetadata>()
            .HasIndex(p => p.PageName);
        modelBuilder.Entity<PageMetadata>()
            .HasIndex(p => p.PublishSlug).IsUnique(false);

        modelBuilder.Entity<PageLink>()
            .HasOne(l => l.SourcePage)
            .WithMany(p => p.OutgoingLinks)
            .HasForeignKey(l => l.SourcePageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PageLink>()
            .HasOne(l => l.TargetPage)
            .WithMany(p => p.IncomingLinks)
            .HasForeignKey(l => l.TargetPageId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AppSetting>()
            .HasIndex(s => s.Key).IsUnique();

        // No FK constraint to PageMetadata - DocumentId is a plain stable int reference kept
        // valid by never hard-deleting PageMetadata rows (see PageMetadata.IsDeleted), so a
        // real foreign key/cascade relationship would add nothing but risk.
        modelBuilder.Entity<DocumentVersion>()
            .HasIndex(v => new { v.DocumentId, v.IsOpen });
        modelBuilder.Entity<DocumentVersion>()
            .HasIndex(v => v.CreatedAtUtc);

        modelBuilder.Entity<AuditLogEntry>()
            .HasIndex(a => a.Timestamp);

        modelBuilder.Entity<GenerationPool>()
            .HasIndex(p => p.Name).IsUnique();

        // The content hash is what makes "forget this entry" permanent - a duplicate can never be
        // inserted, whatever state the original ended up in.
        modelBuilder.Entity<GenerationPoolEntry>()
            .HasIndex(e => new { e.PoolId, e.ContentHash }).IsUnique();
        modelBuilder.Entity<GenerationPoolEntry>()
            .HasIndex(e => new { e.PoolId, e.Status });
    }
}

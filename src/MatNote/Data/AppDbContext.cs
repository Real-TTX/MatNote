using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Data;

/// <summary>
/// The database (PostgreSQL). Tables are PascalCase and singular, the key is always <c>Id</c> (BIGINT), every record has CreateDate, CreateUserId, UpdateDate and UpdateUserId
/// (filled in <see cref="SaveChangesAsync(bool, CancellationToken)"/>), and a record with an <c>UpdateState</c> is deleted softly (0 = deleted, 1 = created, 2 = updated).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser? current = null) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<NoteFile> NoteFiles => Set<NoteFile>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<NoteTag> NoteTags => Set<NoteTag>();

    public DbSet<NoteLink> NoteLinks => Set<NoteLink>();

    public DbSet<Share> Shares => Set<Share>();

    public DbSet<BackupRun> BackupRuns => Set<BackupRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("User");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Theme).HasMaxLength(10);
            e.Property(x => x.Scheme).HasMaxLength(10);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<UserSession>(e =>
        {
            e.ToTable("UserSession");
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.Property(x => x.IpAddress).HasMaxLength(64);
        });

        b.Entity<Folder>(e =>
        {
            e.ToTable("Folder");
            e.HasIndex(x => new { x.OwnerId, x.ParentId });
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Color).HasMaxLength(7);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<Note>(e =>
        {
            e.ToTable("Note");
            e.HasIndex(x => new { x.OwnerId, x.FolderId });
            e.HasIndex(x => x.UpdateDate);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Folder).WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Title).HasMaxLength(250);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(150);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<NoteFile>(e =>
        {
            e.ToTable("NoteFile");
            e.HasIndex(x => x.NoteId).IsUnique();
            e.HasOne(x => x.Note).WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Sha256).HasMaxLength(64);
        });

        b.Entity<Tag>(e =>
        {
            e.ToTable("Tag");
            e.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique().HasFilter("\"UpdateState\" <> 0");
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Color).HasMaxLength(7);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<NoteTag>(e =>
        {
            e.ToTable("NoteTag");
            e.HasIndex(x => new { x.NoteId, x.TagId }).IsUnique();
            e.HasOne(x => x.Note).WithMany(n => n.Tags).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NoteLink>(e =>
        {
            e.ToTable("NoteLink");
            e.HasIndex(x => new { x.SourceNoteId, x.TargetNoteId }).IsUnique();
            e.HasIndex(x => x.TargetNoteId);
        });

        b.Entity<Share>(e =>
        {
            e.ToTable("Share");
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.SharedWithUserId);
            e.HasIndex(x => x.OwnerId);
            e.HasOne(x => x.Note).WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Folder).WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SharedWithUser).WithMany().HasForeignKey(x => x.SharedWithUserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Label).HasMaxLength(120);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<BackupRun>(e =>
        {
            e.ToTable("BackupRun");
            e.HasIndex(x => x.StartDate);
            e.Property(x => x.ScheduleId).HasMaxLength(64);
            e.Property(x => x.ScheduleName).HasMaxLength(120);
            e.Property(x => x.TargetName).HasMaxLength(120);
            e.Property(x => x.Trigger).HasMaxLength(16);
            e.Property(x => x.FileName).HasMaxLength(260);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Fills the audit columns, and turns the delete of a soft record into UpdateState 0.</summary>
    private void Stamp()
    {
        var now = DateTime.UtcNow;
        var user = current?.Id;
        foreach (var entry in ChangeTracker.Entries<Entity>().ToList())
        {
            var entity = entry.Entity;
            if (entry.State == EntityState.Deleted && entity is SoftEntity soft)
            {
                entry.State = EntityState.Modified;
                soft.UpdateState = SoftEntity.Deleted;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    if (entity.CreateDate == default)
                    {
                        entity.CreateDate = now;
                    }

                    entity.CreateUserId ??= user;
                    entity.UpdateDate = now;
                    entity.UpdateUserId = user;
                    break;
                case EntityState.Modified:
                    entity.UpdateDate = now;
                    entity.UpdateUserId = user;
                    if (entity is SoftEntity { UpdateState: not SoftEntity.Deleted } updated)
                    {
                        updated.UpdateState = SoftEntity.Updated;
                    }

                    break;
            }
        }
    }
}

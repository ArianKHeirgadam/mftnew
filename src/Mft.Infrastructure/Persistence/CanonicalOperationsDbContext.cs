using Microsoft.EntityFrameworkCore;

namespace Mft.Infrastructure.Persistence;

/// <summary>
/// Canonical bigint persistence mappings for operational features that are
/// being migrated from the legacy GUID-based AppDbContext.
/// </summary>
public sealed class CanonicalOperationsDbContext(
    DbContextOptions<CanonicalOperationsDbContext> options) : DbContext(options)
{
    public DbSet<CanonicalNotification> Notifications => Set<CanonicalNotification>();
    public DbSet<CanonicalAppSetting> AppSettings => Set<CanonicalAppSetting>();
    public DbSet<CanonicalAttendanceRecord> AttendanceRecords => Set<CanonicalAttendanceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CanonicalNotification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            entity.Property(x => x.UserId).HasColumnType("bigint");
            entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Message).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.IsRead).HasDefaultValue(false);
            entity.Property(x => x.CreatedAt).HasColumnType("datetime2")
                .HasDefaultValueSql("sysutcdatetime()");
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        modelBuilder.Entity<CanonicalAppSetting>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Value).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnType("datetime2")
                .HasDefaultValueSql("sysutcdatetime()");
            entity.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<CanonicalAttendanceRecord>(entity =>
        {
            entity.ToTable("AttendanceRecords");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            entity.Property(x => x.StudentUserId).HasColumnType("bigint");
            entity.Property(x => x.DepartmentId).HasColumnType("bigint");
            entity.Property(x => x.RecordedByUserId).HasColumnType("bigint");
            entity.Property(x => x.AttendanceDate).HasColumnType("date");
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnType("datetime2")
                .HasDefaultValueSql("sysutcdatetime()");
            entity.HasIndex(x => new { x.StudentUserId, x.AttendanceDate }).IsUnique();
            entity.HasIndex(x => new { x.DepartmentId, x.AttendanceDate });
        });
    }
}

public sealed class CanonicalNotification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CanonicalAppSetting
{
    public long Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public sealed class CanonicalAttendanceRecord
{
    public long Id { get; set; }
    public long StudentUserId { get; set; }
    public long DepartmentId { get; set; }
    public long RecordedByUserId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

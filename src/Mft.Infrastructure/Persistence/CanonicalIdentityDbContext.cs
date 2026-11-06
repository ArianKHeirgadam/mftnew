using Microsoft.EntityFrameworkCore;

namespace Mft.Infrastructure.Persistence;

/// <summary>
/// Read/write persistence model for the canonical identity tables.
/// Kept separate from the legacy GUID-based AppDbContext while the remaining
/// API modules are migrated in coordinated batches.
/// </summary>
public sealed class CanonicalIdentityDbContext(
    DbContextOptions<CanonicalIdentityDbContext> options) : DbContext(options)
{
    public DbSet<CanonicalUser> Users => Set<CanonicalUser>();
    public DbSet<CanonicalDepartment> Departments => Set<CanonicalDepartment>();
    public DbSet<CanonicalRole> Roles => Set<CanonicalRole>();
    public DbSet<CanonicalUserRole> UserRoles => Set<CanonicalUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CanonicalUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            entity.Property(x => x.Username).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(600).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(400).IsRequired();
            entity.Property(x => x.PhoneNumber).HasMaxLength(40).IsRequired(false);
            entity.Property(x => x.Email).HasMaxLength(512).IsRequired(false);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CreatedAt).HasColumnType("datetime2")
                .HasDefaultValueSql("sysutcdatetime()");
            entity.HasIndex(x => x.Username).IsUnique();
            entity.HasIndex(x => x.PhoneNumber).IsUnique();
            entity.HasOne(x => x.Department).WithMany(x => x.Users)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CanonicalDepartment>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CreatedAt).HasColumnType("datetime2")
                .HasDefaultValueSql("sysutcdatetime()");
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<CanonicalRole>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("int").ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<CanonicalUserRole>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.Property(x => x.UserId).HasColumnType("bigint");
            entity.Property(x => x.RoleId).HasColumnType("int");
            entity.HasOne(x => x.User).WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Role).WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}

public sealed class CanonicalUser
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public long? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public CanonicalDepartment? Department { get; set; }
    public ICollection<CanonicalUserRole> UserRoles { get; set; } = new List<CanonicalUserRole>();
}

public sealed class CanonicalDepartment
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public ICollection<CanonicalUser> Users { get; set; } = new List<CanonicalUser>();
}

public sealed class CanonicalRole
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<CanonicalUserRole> UserRoles { get; set; } = new List<CanonicalUserRole>();
}

public sealed class CanonicalUserRole
{
    public long UserId { get; set; }
    public int RoleId { get; set; }
    public CanonicalUser User { get; set; } = null!;
    public CanonicalRole Role { get; set; } = null!;
}

/// <summary>
/// Database role codes remain authoritative. "Department" is only the
/// existing API authorization-policy alias for "DepartmentAdmin".
/// </summary>
public static class CanonicalRoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string DepartmentAdmin = "DepartmentAdmin";
    public const string Teacher = "Teacher";
    public const string Student = "Student";

    public static string ToPolicyRole(string databaseRole) =>
        string.Equals(databaseRole, DepartmentAdmin, StringComparison.Ordinal)
            ? "Department"
            : databaseRole;
}

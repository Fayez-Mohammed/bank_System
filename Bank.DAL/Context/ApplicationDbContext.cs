using Bank.DAL.Models.BaseModels;
using Bank.DAL.Models.SystemModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bank.DAL.Context;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<UserDataFilePermission> UserDataFilePermissions => Set<UserDataFilePermission>();
    public DbSet<CheckFile> CheckFiles => Set<CheckFile>();
    public DbSet<CheckPlate> CheckPlates => Set<CheckPlate>();
    public DbSet<RecordingSession> RecordingSessions => Set<RecordingSession>();
    public DbSet<RecordedPlate> RecordedPlates => Set<RecordedPlate>();
    public DbSet<LiveCheckLog> LiveCheckLogs => Set<LiveCheckLog>();
    public DbSet<DataFile> DataFiles => Set<DataFile>();
    public DbSet<HistoricalPlate> HistoricalPlates => Set<HistoricalPlate>();
    public DbSet<ReferralJob> ReferralJobs => Set<ReferralJob>();
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();
    public DbSet<ReferralPlate> ReferralPlates => Set<ReferralPlate>();
    public DbSet<RecordingGpsPoint> RecordingGpsPoints => Set<RecordingGpsPoint>(); 
    public DbSet<CheckSession> CheckSessions => Set<CheckSession>();
    public DbSet<RecordingArchivePlate> RecordingArchivePlates
    => Set<RecordingArchivePlate>();
 
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<MapResult> MapResults => Set<MapResult>();
    public DbSet<MapUploadedFile> MapUploadedFiles => Set<MapUploadedFile>();
    public DbSet<MapUploadedPlateRow> MapUploadedPlateRows => Set<MapUploadedPlateRow>();
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Npgsql بيرفض أي DateTime مش UTC في أعمدة timestamptz، فبنثبّت الـ Kind
        // للتواريخ اللي جاية من الـ client من غير Z (Unspecified) أو بـ offset (Local).
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Utc ? v
                    : v.Kind == DateTimeKind.Local ? v.ToUniversalTime()
                    : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // أعمدة الـ geography (Location) محتاجة PostGIS
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
    }
}


//using Bank.DAL.Models.BaseModels;
//using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.EntityFrameworkCore.Metadata.Internal;
//using System;
//using System.Collections.Generic;
//using System.Reflection;
//using System.Runtime.CompilerServices;
//using System.Text;

//namespace Bank.DAL.Context
//{
//    public class ApplicationDbContext:IdentityDbContext<ApplicationUser>
//    {
//        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options):base(options)
//        {

//        }
//        protected override void OnModelCreating(ModelBuilder builder)
//        {
//            base.OnModelCreating(builder);
//            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());


//            foreach (var entityType in builder.Model.GetEntityTypes())
//            {
//                if (typeof(ISoftDelete).IsAssignableFrom((entityType.ClrType)) && !entityType.IsOwned())
//                {
//                    var method = typeof(ApplicationDbContext).GetMethod(nameof(ConfigureSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static)!
//                        .MakeGenericMethod(entityType.ClrType);
//                    method.Invoke(null, new object[] { builder });
//                }
//            }
//        }
//        private static void ConfigureSoftDeleteFilter<TEntity>(ModelBuilder builder)where TEntity :class,ISoftDelete
//        {
//            builder.Entity<TEntity>().HasQueryFilter(e=>!e.IsDeleted);
//        }
//        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
//        {
//            UpdateAuditableFields();
//            SoftDeleteFields();
//            return base.SaveChangesAsync(cancellationToken);
//        }
//        public override int SaveChanges()
//        {
//            UpdateAuditableFields();
//            SoftDeleteFields();
//            return base.SaveChanges();
//        }
//        private void UpdateAuditableFields()
//        {
//            var entries = ChangeTracker.Entries<IAuditableEntity>();
//            foreach (var entry in entries)
//            {
//                if (entry.State == EntityState.Modified)
//                {
//                    entry.Entity.UpdatedAt = DateTime.UtcNow;
//                }
//            }
//        }
//        private void SoftDeleteFields()
//        {
//            var entries = ChangeTracker.Entries<ISoftDelete>();
//            foreach (var entry in entries)
//            {
//                if (entry.State == EntityState.Deleted)
//                {
//                    entry.State = EntityState.Modified;
//                    entry.Entity.IsDeleted = true;
//                    entry.Entity.DeletedAt = DateTime.UtcNow;
//                }
//            }
//        }

//        #region DbSets

//        // 1. Identity & Tokens
//        //public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;

//        // 2. Course Management & Content


//        // 3. Enrollments & Learning Progress

//        #endregion
//    }
//}


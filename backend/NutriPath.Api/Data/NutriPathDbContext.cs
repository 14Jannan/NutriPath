using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Models;

namespace NutriPath.Api.Data;

/// <summary>
/// The single point of contact between your C# code and the database.
/// Every table you want EF Core to manage gets a DbSet property here.
/// </summary>
public class NutriPathDbContext : DbContext
{
    public NutriPathDbContext(DbContextOptions<NutriPathDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<Food> Foods => Set<Food>();
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealItem> MealItems => Set<MealItem>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    // Admin portal
    public DbSet<Models.Admin.UserRole> UserRoles => Set<Models.Admin.UserRole>();
    public DbSet<Models.Admin.AdminSession> AdminSessions => Set<Models.Admin.AdminSession>();
    public DbSet<Models.Admin.AdminMfa> AdminMfa => Set<Models.Admin.AdminMfa>();
    public DbSet<Models.Admin.AdminRecoveryCode> AdminRecoveryCodes => Set<Models.Admin.AdminRecoveryCode>();
    public DbSet<Models.Admin.AdminInvitation> AdminInvitations => Set<Models.Admin.AdminInvitation>();
    public DbSet<Models.Admin.AdminAuditLog> AdminAuditLogs => Set<Models.Admin.AdminAuditLog>();
    public DbSet<Models.Admin.ProviderCredential> ProviderCredentials => Set<Models.Admin.ProviderCredential>();
    public DbSet<Models.Admin.RuntimeSetting> RuntimeSettings => Set<Models.Admin.RuntimeSetting>();
    public DbSet<Models.Admin.RuntimeSettingVersion> RuntimeSettingVersions => Set<Models.Admin.RuntimeSettingVersion>();
    public DbSet<Models.Admin.AiRequestMetric> AiRequestMetrics => Set<Models.Admin.AiRequestMetric>();
    public DbSet<Models.Admin.UserAiLimit> UserAiLimits => Set<Models.Admin.UserAiLimit>();
    public DbSet<Models.Admin.BackgroundJob> BackgroundJobs => Set<Models.Admin.BackgroundJob>();
    public DbSet<Models.Admin.AnswerFeedback> AnswerFeedback => Set<Models.Admin.AnswerFeedback>();
    public DbSet<Models.Admin.SafetyIncident> SafetyIncidents => Set<Models.Admin.SafetyIncident>();
    public DbSet<Models.Admin.AdminNotification> AdminNotifications => Set<Models.Admin.AdminNotification>();
    public DbSet<Models.Admin.AdminNotificationRead> AdminNotificationReads => Set<Models.Admin.AdminNotificationRead>();
    public DbSet<Models.Admin.NutritionTerm> NutritionTerms => Set<Models.Admin.NutritionTerm>();
    public DbSet<Models.Admin.EvaluationRun> EvaluationRuns => Set<Models.Admin.EvaluationRun>();
    public DbSet<Models.Admin.DataRequest> DataRequests => Set<Models.Admin.DataRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnowledgeDocument>().HasIndex(d => d.ContentHash).IsUnique();
        modelBuilder.Entity<KnowledgeDocument>().Property(d => d.ContentHash).IsConcurrencyToken();
        modelBuilder.Entity<User>().Property(u => u.RefreshTokenHash).IsConcurrencyToken();

        // A user's email must be unique — enforced at the database level,
        // not just checked in application code, so it holds true even
        // under concurrent requests.
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // Avatar IDs are short catalog keys, never free text.
        modelBuilder.Entity<UserProfile>()
            .Property(p => p.AvatarId)
            .HasMaxLength(40);

        // One User has exactly one UserProfile, and vice versa.
        modelBuilder.Entity<User>()
            .HasOne(u => u.Profile)
            .WithOne(p => p.User)
            .HasForeignKey<UserProfile>(p => p.UserId);

        // A given external food can only exist once per source — this is the
// database-level guarantee that re-running a sync never creates duplicates.
        modelBuilder.Entity<Food>()
            .HasIndex(f => new { f.DataSourceId, f.ExternalId })
            .IsUnique();

        // User-added foods belong to that user and go when the account does.
        modelBuilder.Entity<Food>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.CreatedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Usage is summed per user over recent hours on every chat.
        modelBuilder.Entity<AiUsageRecord>()
            .HasIndex(r => new { r.UserId, r.CreatedAtUtc });
        modelBuilder.Entity<AiUsageRecord>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One Meal has many MealItems. Naming the MealItem.Meal navigation
        // here ties both sides to the same MealId foreign key — without it,
        // EF would infer a second relationship and add a shadow MealId1.
        modelBuilder.Entity<Meal>()
            .HasMany(m => m.Items)
            .WithOne(mi => mi.Meal)
            .HasForeignKey(mi => mi.MealId);

        ConfigureAdmin(modelBuilder);
    }

    private static void ConfigureAdmin(ModelBuilder b)
    {
        b.Entity<User>().Property(u => u.SecurityStamp).IsConcurrencyToken();
        b.Entity<KnowledgeDocument>().HasIndex(d => d.Status);

        b.Entity<Models.Admin.UserRole>().HasKey(r => new { r.UserId, r.Role });
        b.Entity<Models.Admin.UserRole>().HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Models.Admin.AdminSession>().HasIndex(s => s.RefreshTokenHash).IsUnique();
        b.Entity<Models.Admin.AdminSession>().HasIndex(s => s.UserId);
        b.Entity<Models.Admin.AdminSession>().Property(s => s.RefreshTokenHash).IsConcurrencyToken();
        b.Entity<Models.Admin.AdminSession>().HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Models.Admin.AdminMfa>().HasOne<User>().WithOne().HasForeignKey<Models.Admin.AdminMfa>(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Models.Admin.AdminMfa>().Property(m => m.LastUsedTimeStep).IsConcurrencyToken();
        b.Entity<Models.Admin.AdminRecoveryCode>().HasIndex(c => c.UserId);
        b.Entity<Models.Admin.AdminRecoveryCode>().HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Models.Admin.AdminInvitation>().HasIndex(i => i.TokenHash).IsUnique();
        b.Entity<Models.Admin.AdminInvitation>().HasIndex(i => i.Email);

        // Audit rows keep the actor id even if that account is later deleted.
        b.Entity<Models.Admin.AdminAuditLog>().HasIndex(a => a.AtUtc);
        b.Entity<Models.Admin.AdminAuditLog>().HasIndex(a => new { a.ActorUserId, a.AtUtc });
        b.Entity<Models.Admin.AdminAuditLog>().HasIndex(a => a.Action);

        b.Entity<Models.Admin.ProviderCredential>().HasIndex(c => new { c.Provider, c.Version }).IsUnique();
        b.Entity<Models.Admin.ProviderCredential>().HasIndex(c => new { c.Provider, c.Status });

        b.Entity<Models.Admin.RuntimeSetting>().Property(s => s.Version).IsConcurrencyToken();
        b.Entity<Models.Admin.RuntimeSettingVersion>().HasIndex(v => new { v.Key, v.Version }).IsUnique();

        b.Entity<Models.Admin.AiRequestMetric>().HasIndex(m => m.AtUtc);
        b.Entity<Models.Admin.AiRequestMetric>().HasIndex(m => new { m.UserId, m.AtUtc });

        b.Entity<Models.Admin.UserAiLimit>().HasOne<User>().WithOne().HasForeignKey<Models.Admin.UserAiLimit>(l => l.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Models.Admin.BackgroundJob>().HasIndex(j => new { j.Status, j.CreatedAtUtc });

        // Feedback holds a copy of the rated conversation, so it goes with the account.
        b.Entity<Models.Admin.AnswerFeedback>().HasOne<User>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Models.Admin.AnswerFeedback>().HasIndex(f => new { f.UserId, f.MessageId }).IsUnique();
        b.Entity<Models.Admin.AnswerFeedback>().HasIndex(f => new { f.Status, f.CreatedAtUtc });
        b.Entity<Models.Admin.SafetyIncident>().HasIndex(i => new { i.Status, i.CreatedAtUtc });
        b.Entity<Models.Admin.SafetyIncident>().HasOne<Models.Admin.AnswerFeedback>().WithMany().HasForeignKey(i => i.FeedbackId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Models.Admin.AdminNotification>().HasIndex(n => n.CreatedAtUtc);
        b.Entity<Models.Admin.AdminNotificationRead>().HasKey(r => new { r.NotificationId, r.UserId });
        b.Entity<Models.Admin.AdminNotificationRead>().HasOne<Models.Admin.AdminNotification>().WithMany().HasForeignKey(r => r.NotificationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Models.Admin.AdminNotificationRead>().HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Models.Admin.NutritionTerm>().HasIndex(t => t.Status);
        b.Entity<Models.Admin.EvaluationRun>().HasIndex(r => r.CreatedAtUtc);
        // No foreign key: a completed deletion request outlives the account it deleted.
        b.Entity<Models.Admin.DataRequest>().HasIndex(r => new { r.Status, r.CreatedAtUtc });
        b.Entity<Models.Admin.DataRequest>().HasIndex(r => r.UserId);
    }

}

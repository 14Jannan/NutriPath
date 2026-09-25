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
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // A user's email must be unique — enforced at the database level,
        // not just checked in application code, so it holds true even
        // under concurrent requests.
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

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

        // One Meal has many MealItems. Naming the MealItem.Meal navigation
        // here ties both sides to the same MealId foreign key — without it,
        // EF would infer a second relationship and add a shadow MealId1.
        modelBuilder.Entity<Meal>()
            .HasMany(m => m.Items)
            .WithOne(mi => mi.Meal)
            .HasForeignKey(mi => mi.MealId);
    }

}
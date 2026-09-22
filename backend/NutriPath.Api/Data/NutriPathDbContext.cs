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
    }
}
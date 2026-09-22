namespace NutriPath.Api.Models;

/// <summary>
/// Login identity only. Kept deliberately minimal — everything about
/// who the person actually is (goals, allergies, body stats) lives in
/// UserProfile, one-to-one with this table.
/// </summary>

public class User
{
    public Guid Id {get; set;}=Guid.NewGuid();
    public string Email {get; set;}=string.Empty;
    public string PasswordHash {get; set;}=string.Empty;
    public DateTime CreatedAtUtc {get; set;}=DateTime.UtcNow;
    public UserProfile? Profile {get; set;}
}

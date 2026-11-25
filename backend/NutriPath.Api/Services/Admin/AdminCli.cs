using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Trusted operator commands (server shell access required):
///   admin bootstrap          create the first SuperAdmin (refused once one exists)
///   admin list               list administrators and their roles
///   admin grant EMAIL ROLE   give an existing, verified account an admin role
///   admin reset-mfa EMAIL    clear an administrator's MFA so they re-enrol at next sign-in
/// Credentials come from NUTRIPATH_ADMIN_EMAIL / NUTRIPATH_ADMIN_PASSWORD or a hidden prompt,
/// never from command-line arguments (which end up in shell history).
/// </summary>
public sealed class AdminCli(NutriPathDbContext db, IPasswordHasher passwords, AdminAudit audit)
{
    public const int MinimumPasswordLength = 12;

    public async Task RunAsync(string[] args)
    {
        switch (args)
        {
            case ["bootstrap"]:
                await BootstrapAsync();
                break;
            case ["list"]:
                var admins = await db.UserRoles.Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new { u.Email, r.Role, u.IsSuspended })
                    .OrderBy(x => x.Email).ToListAsync();
                foreach (var group in admins.GroupBy(a => a.Email))
                    Console.WriteLine($"{group.Key}\t{string.Join(",", group.Select(g => g.Role))}{(group.First().IsSuspended ? "\tSUSPENDED" : "")}");
                break;
            case ["grant", var email, var role]:
                if (!AdminRoles.IsValid(role)) throw new ArgumentException($"Unknown role. Roles: {string.Join(", ", AdminRoles.All)}");
                var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email)
                    ?? throw new ArgumentException("No account with that email.");
                if (!user.EmailVerified) throw new ArgumentException("The account's email is not verified.");
                if (!await db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.Role == role))
                {
                    db.UserRoles.Add(new UserRole { UserId = user.Id, Role = role });
                    user.SecurityStamp = Guid.NewGuid().ToString("N");
                    await db.SaveChangesAsync();
                }
                await audit.WriteAsync("admin.cli.grant", resourceType: "User", resourceId: user.Id.ToString(), summary: $"Granted {role} from server CLI", actorEmail: "server-cli");
                Console.WriteLine($"{email} now has {role}.");
                break;
            case ["reset-mfa", var email]:
                var target = await db.Users.SingleOrDefaultAsync(u => u.Email == email)
                    ?? throw new ArgumentException("No account with that email.");
                await db.AdminMfa.Where(m => m.UserId == target.Id).ExecuteDeleteAsyncSafe(db, default);
                await db.AdminRecoveryCodes.Where(c => c.UserId == target.Id).ExecuteDeleteAsyncSafe(db, default);
                target.SecurityStamp = Guid.NewGuid().ToString("N");
                foreach (var s in await db.AdminSessions.Where(s => s.UserId == target.Id && s.RevokedAtUtc == null).ToListAsync())
                    s.RevokedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await audit.WriteAsync("admin.cli.reset_mfa", resourceType: "User", resourceId: target.Id.ToString(), summary: "MFA cleared from server CLI; sessions revoked", actorEmail: "server-cli");
                Console.WriteLine($"MFA cleared for {email}. They will enrol again at next sign-in.");
                break;
            default:
                throw new ArgumentException("Commands: bootstrap | list | grant EMAIL ROLE | reset-mfa EMAIL");
        }
    }

    private async Task BootstrapAsync()
    {
        if (await db.UserRoles.AnyAsync(r => r.Role == AdminRoles.SuperAdmin))
            throw new InvalidOperationException("A SuperAdmin already exists. Use the portal to invite administrators.");

        var email = (Environment.GetEnvironmentVariable("NUTRIPATH_ADMIN_EMAIL") ?? Prompt("Admin email: ", hidden: false)).Trim();
        if (!email.Contains('@') || email.Length > 256) throw new ArgumentException("Enter a valid email address.");
        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        User user;
        if (existing is null)
        {
            var password = Environment.GetEnvironmentVariable("NUTRIPATH_ADMIN_PASSWORD") ?? Prompt("Password: ", hidden: true);
            if (password.Length < MinimumPasswordLength)
                throw new ArgumentException($"Use a password of at least {MinimumPasswordLength} characters.");
            user = new User { Email = email, PasswordHash = passwords.Hash(password), EmailVerified = true };
            user.Profile = new UserProfile { UserId = user.Id, FullName = "Administrator" };
            db.Users.Add(user);
        }
        else
        {
            // An existing account keeps its password; the operator must know it to sign in.
            if (!existing.EmailVerified) throw new ArgumentException("That account's email is not verified.");
            user = existing;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }
        db.UserRoles.Add(new UserRole { UserId = user.Id, Role = AdminRoles.SuperAdmin });
        await db.SaveChangesAsync();
        await audit.WriteAsync("admin.bootstrap", resourceType: "User", resourceId: user.Id.ToString(),
            summary: existing is null ? "First SuperAdmin created" : "Existing account promoted to first SuperAdmin", actorEmail: "server-cli");
        Console.WriteLine($"SuperAdmin ready: {email}. Sign in to the admin portal to set up MFA.");
    }

    private static string Prompt(string label, bool hidden)
    {
        if (Console.IsInputRedirected && hidden)
            throw new InvalidOperationException("Set NUTRIPATH_ADMIN_PASSWORD when input is not interactive.");
        Console.Write(label);
        if (!hidden) return Console.ReadLine() ?? "";
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && buffer.Length > 0) buffer.Length--;
            else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
        Console.WriteLine();
        return buffer.ToString();
    }
}

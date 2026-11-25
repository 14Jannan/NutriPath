using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Raises in-portal notifications for events administrators must act on. Each notification is
/// visible only to administrators holding its permission, and never contains health data.
/// </summary>
public sealed class AdminNotifications(NutriPathDbContext db)
{
    /// <summary>Adds the notification to the context; it's saved with the caller's SaveChanges.</summary>
    public void Add(string type, string title, string permission, string? body = null, string? link = null, string tone = "info") =>
        db.AdminNotifications.Add(new AdminNotification
        {
            Type = type, Title = Cut(title, 200)!, Body = Cut(body, 500), Link = Cut(link, 200), Permission = permission, Tone = tone,
        });

    public async Task NotifyAsync(string type, string title, string permission, string? body = null, string? link = null,
        string tone = "info", CancellationToken ct = default)
    {
        Add(type, title, permission, body, link, tone);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(List<(AdminNotification Item, bool Read)> Items, int Unread)> ForAsync(Guid userId, IReadOnlySet<string> permissions,
        int take, CancellationToken ct)
    {
        var visible = db.AdminNotifications.AsNoTracking().Where(n => permissions.Contains(n.Permission));
        var read = db.AdminNotificationReads.Where(r => r.UserId == userId).Select(r => r.NotificationId);
        var unread = await visible.CountAsync(n => !read.Contains(n.Id), ct);
        var rows = await visible.OrderByDescending(n => n.CreatedAtUtc).Take(take)
            .Select(n => new { n, isRead = read.Contains(n.Id) }).ToListAsync(ct);
        return (rows.Select(r => (r.n, r.isRead)).ToList(), unread);
    }

    public async Task MarkReadAsync(Guid userId, IReadOnlySet<string> permissions, Guid? id, CancellationToken ct)
    {
        var query = db.AdminNotifications.Where(n => permissions.Contains(n.Permission) &&
            !db.AdminNotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == userId));
        if (id is { } one) query = query.Where(n => n.Id == one);
        var ids = await query.Select(n => n.Id).ToListAsync(ct);
        db.AdminNotificationReads.AddRange(ids.Select(n => new AdminNotificationRead { NotificationId = n, UserId = userId }));
        await db.SaveChangesAsync(ct);
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..(max - 1)] + "…";
}

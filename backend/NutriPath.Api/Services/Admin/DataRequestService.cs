using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models;
using NutriPath.Api.Models.Admin;

namespace NutriPath.Api.Services.Admin;

/// <summary>
/// Users' requests for a copy of their data or deletion of their account. Requests are made in
/// the app and fulfilled by an administrator with the privacy.requests.manage permission. Exports
/// are built by a background job, encrypted at rest and expire; deletion removes every row that
/// belongs to the user and keeps only a minimal record that the request was honoured.
/// </summary>
public sealed class DataRequestService(NutriPathDbContext db, IMessageProtector protector, PlatformSettingsStore settings,
    BackgroundJobs jobs, AdminNotifications notifications, SecurityState securityState, ILogger<DataRequestService> logger)
{
    public static readonly IReadOnlyList<string> Types = ["Export", "Deletion"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<DataRequest> CreateAsync(Guid userId, string type, string? note, CancellationToken ct)
    {
        if (!settings.IsEnabled(FeatureFlags.PrivacyRequests))
            throw new AdminRuleException($"Data requests in the app are turned off. Contact {settings.System.SupportEmail ?? "NutriPath support"}.", StatusCodes.Status403Forbidden);
        if (!Types.Contains(type)) throw new AdminRuleException("Choose Export or Deletion.", StatusCodes.Status400BadRequest);
        if (await db.DataRequests.AnyAsync(r => r.UserId == userId && r.Type == type && r.Status == "Pending", ct))
            throw new AdminRuleException("You already have a request of this type in progress.", StatusCodes.Status409Conflict);
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(ct);
        var request = new DataRequest { UserId = userId, Type = type, Email = email, UserNote = note?.Trim() is { Length: > 0 } n ? n[..Math.Min(500, n.Length)] : null };
        db.DataRequests.Add(request);
        notifications.Add("privacy.request", type == "Deletion" ? "New account deletion request" : "New data export request",
            Permissions.PrivacyRequestsRead, null, "/privacy", type == "Deletion" ? "warning" : "info");
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task CancelAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var request = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct) ?? throw new KeyNotFoundException();
        if (request.Status != "Pending") throw new AdminRuleException("Only pending requests can be cancelled.");
        request.Status = "Cancelled";
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The user's own export, decrypted, while it hasn't expired.</summary>
    public async Task<string> DownloadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var request = await db.DataRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.UserId == userId && r.Type == "Export", ct)
            ?? throw new KeyNotFoundException();
        if (request.Status != "Completed" || request.ExportCiphertext is null || request.ExportExpiresAtUtc < DateTime.UtcNow)
            throw new AdminRuleException("This export isn't available. Request a new one.", StatusCodes.Status410Gone);
        return protector.Unprotect(request.ExportCiphertext);
    }

    /// <summary>Approves a pending request: queues the export or deletes the account now.</summary>
    public async Task<object> ApproveAsync(Guid id, Guid actor, string? note, CancellationToken ct)
    {
        var request = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new KeyNotFoundException();
        if (request.Status != "Pending") throw new AdminRuleException("This request has already been handled.");
        request.AdminNote = note?.Trim() is { Length: > 0 } n ? n[..Math.Min(500, n.Length)] : null;
        if (request.Type == "Export")
        {
            var jobId = await jobs.EnqueueAsync(BackgroundJobs.PrivacyExport, new { requestId = id }, actor, ct);
            request.ProcessedByUserId = actor;
            await db.SaveChangesAsync(ct);
            return new { jobId };
        }
        if (await db.UserRoles.AnyAsync(r => r.UserId == request.UserId, ct))
            throw new AdminRuleException("Administrator accounts must have their roles removed before deletion.");
        await DeleteAccountAsync(request.UserId, ct);
        request.Status = "Completed";
        request.Email = null; // keep no personal data about the deleted account
        request.UserNote = null;
        request.ProcessedByUserId = actor;
        request.ProcessedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { deleted = true };
    }

    public async Task RejectAsync(Guid id, Guid actor, string reason, CancellationToken ct)
    {
        var request = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new KeyNotFoundException();
        if (request.Status != "Pending") throw new AdminRuleException("This request has already been handled.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) throw new AdminRuleException("Give the user a reason (at least 5 characters).", StatusCodes.Status400BadRequest);
        request.Status = "Rejected";
        request.AdminNote = reason.Trim()[..Math.Min(500, reason.Trim().Length)];
        request.ProcessedByUserId = actor;
        request.ProcessedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Background job: builds the export and stores it encrypted until it expires.</summary>
    public async Task<object> BuildExportAsync(Guid id, CancellationToken ct)
    {
        var request = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new AdminRuleException("The request no longer exists.");
        var userId = request.UserId;
        var user = await db.Users.AsNoTracking().Include(u => u.Profile).SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new AdminRuleException("The account no longer exists.");
        var meals = await db.Meals.AsNoTracking().Where(m => m.UserId == userId).OrderBy(m => m.Date)
            .Select(m => new
            {
                m.Date, mealType = m.MealType.ToString(),
                items = m.Items.Select(i => new
                {
                    food = i.Food!.Name, i.QuantityGrams, calories = i.CaloriesSnapshot, proteinGrams = i.ProteinGramsSnapshot,
                    carbsGrams = i.CarbsGramsSnapshot, fatGrams = i.FatGramsSnapshot, fiberGrams = i.FiberGramsSnapshot,
                    sugarGrams = i.SugarGramsSnapshot, sodiumMilligrams = i.SodiumMilligramsSnapshot, i.LoggedAtUtc,
                }),
            }).ToListAsync(ct);
        var foods = await db.Foods.AsNoTracking().Where(f => f.CreatedByUserId == userId)
            .Select(f => new { f.Name, f.ServingSizeGrams, f.Calories, f.ProteinGrams, f.CarbsGrams, f.FatGrams, f.FiberGrams, f.SugarGrams, f.SodiumMilligrams, f.Allergens })
            .ToListAsync(ct);
        var conversations = await db.AiConversations.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.StartedAtUtc)
            .Select(c => new { c.StartedAtUtc, messages = c.Messages.OrderBy(m => m.CreatedAtUtc).Select(m => new { m.Role, m.Content, m.CreatedAtUtc }).ToList() })
            .ToListAsync(ct);
        var feedback = await db.AnswerFeedback.AsNoTracking().Where(f => f.UserId == userId)
            .Select(f => new { f.Helpful, f.Reason, f.CommentCiphertext, f.CreatedAtUtc }).ToListAsync(ct);
        var p = user.Profile;
        var export = new
        {
            exportedAtUtc = DateTime.UtcNow,
            format = "NutriPath personal data export v1",
            account = new { user.Email, user.EmailVerified, user.CreatedAtUtc, user.LastLoginAtUtc },
            profile = p is null ? null : new
            {
                p.FullName, p.Age, sex = p.Sex.ToString(), p.HeightCm, p.WeightKg, activityLevel = p.ActivityLevel.ToString(), goal = p.Goal.ToString(),
                p.TargetCalories, p.TargetProteinGrams, p.TargetCarbsGrams, p.TargetFatGrams, p.TargetFiberGrams,
                p.Allergies, p.DietaryPreferences, p.PreferredLanguage, p.AvatarId,
            },
            meals,
            customFoods = foods,
            assistantConversations = conversations.Select(c => new
            {
                c.StartedAtUtc,
                messages = c.messages.Select(m => new { role = m.Role.ToString(), text = protector.Unprotect(m.Content), m.CreatedAtUtc }),
            }),
            answerFeedback = feedback.Select(f => new { f.Helpful, f.Reason, comment = f.CommentCiphertext is null ? null : protector.Unprotect(f.CommentCiphertext), f.CreatedAtUtc }),
        };
        request.ExportCiphertext = protector.Protect(JsonSerializer.Serialize(export, Json));
        request.ExportExpiresAtUtc = DateTime.UtcNow.AddDays(settings.System.ExportExpiryDays);
        request.Status = "Completed";
        request.ProcessedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { meals = meals.Count, conversations = conversations.Count, expiresAtUtc = request.ExportExpiresAtUtc };
    }

    /// <summary>Removes every row belonging to the user, in dependency order, in one transaction.</summary>
    public async Task DeleteAccountAsync(Guid userId, CancellationToken ct)
    {
        var relational = db.Database.IsRelational();
        await using var transaction = relational ? await db.Database.BeginTransactionAsync(ct) : null;
        var conversationIds = db.AiConversations.Where(c => c.UserId == userId).Select(c => c.Id);
        var mealIds = db.Meals.Where(m => m.UserId == userId).Select(m => m.Id);
        var foodIds = db.Foods.Where(f => f.CreatedByUserId == userId).Select(f => f.Id);
        await db.AnswerFeedback.Where(f => f.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        await db.AiMessages.Where(m => conversationIds.Contains(m.ConversationId)).ExecuteDeleteAsyncSafe(db, ct);
        await db.AiConversations.Where(c => c.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        await db.MealItems.Where(i => mealIds.Contains(i.MealId) || foodIds.Contains(i.FoodId)).ExecuteDeleteAsyncSafe(db, ct);
        await db.Meals.Where(m => m.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        await db.Foods.Where(f => f.CreatedByUserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        await db.AiUsageRecords.Where(r => r.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        await db.AiRequestMetrics.Where(m => m.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        await db.UserAiLimits.Where(l => l.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        await db.AdminNotificationReads.Where(r => r.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        await db.UserProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsyncSafe(db, ct);
        // Other requests by the same user: keep the record, drop personal data.
        foreach (var other in await db.DataRequests.Where(r => r.UserId == userId).ToListAsync(ct))
        {
            other.Email = null;
            other.UserNote = null;
            other.ExportCiphertext = null;
        }
        await db.SaveChangesAsync(ct);
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsyncSafe(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        securityState.Invalidate(userId);
        logger.LogInformation("Account {UserId} deleted on request.", userId);
    }

    /// <summary>Removes expired export files (run by the retention job).</summary>
    public async Task<int> PurgeExpiredExportsAsync(CancellationToken ct)
    {
        var expired = await db.DataRequests.Where(r => r.ExportCiphertext != null && r.ExportExpiresAtUtc < DateTime.UtcNow).ToListAsync(ct);
        foreach (var r in expired) r.ExportCiphertext = null;
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}

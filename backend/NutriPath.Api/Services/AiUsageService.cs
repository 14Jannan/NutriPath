using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NutriPath.Api.Data;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

/// <summary>
/// Caps how many AI tokens each user spends in a rolling window, so one
/// person can't use up the shared Groq quota for everyone.
/// </summary>
public class AiUsageService : IAiUsageService
{
    private readonly NutriPathDbContext _db;
    private readonly AiUsageSettings _settings;

    public AiUsageService(NutriPathDbContext db, IOptions<AiUsageSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
    }

    public async Task<AiUsageStatus> GetStatusAsync(Guid userId)
    {
        // An administrator can override the allowance for one account.
        var limitOverride = await _db.UserAiLimits.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId);
        var tokenLimit = limitOverride?.TokenLimit ?? _settings.TokenLimit;
        var windowHours = limitOverride?.WindowHours ?? _settings.WindowHours;
        var window = TimeSpan.FromHours(windowHours);
        var since = DateTime.UtcNow - window;

        var records = await _db.AiUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAtUtc > since)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => new { r.CreatedAtUtc, r.Tokens })
            .ToListAsync();

        var used = records.Sum(r => r.Tokens);
        var locked = used >= tokenLimit;

        // Locked until enough of the oldest replies age out of the window
        // to bring usage back under the limit.
        DateTime? resetsAt = null;
        if (locked)
        {
            var remaining = used;
            foreach (var record in records)
            {
                remaining -= record.Tokens;
                if (remaining < tokenLimit)
                {
                    resetsAt = record.CreatedAtUtc + window;
                    break;
                }
            }
        }

        return new AiUsageStatus(used, tokenLimit, windowHours, locked, resetsAt);
    }
}

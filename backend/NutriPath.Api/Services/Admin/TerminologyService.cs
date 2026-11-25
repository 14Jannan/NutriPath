using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;
using NutriPath.Api.Models.Admin;
using NutriPath.Api.Services.Multilingual;

namespace NutriPath.Api.Services.Admin;

public sealed record TermInput(string Language, string? NativeTerms, string? RomanisedTerms, string EnglishTerms, string? Notes);

/// <summary>
/// Reviewed Tamil/Sinhala nutrition terms added from the portal. Approved terms extend the
/// built-in lexicon used to expand keyword search; another reviewer approves each term.
/// </summary>
public sealed class TerminologyService(NutriPathDbContext db, AdminAudit audit)
{
    public static string[] Lines(string? text) =>
        (text ?? "").Split(['\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static void Validate(TermInput input)
    {
        if (input.Language is not ("ta" or "si")) throw new AdminRuleException("Choose Tamil or Sinhala.", StatusCodes.Status400BadRequest);
        var native = Lines(input.NativeTerms);
        var romanised = Lines(input.RomanisedTerms);
        if (native.Length + romanised.Length == 0) throw new AdminRuleException("Add at least one Tamil/Sinhala or romanised term.", StatusCodes.Status400BadRequest);
        if (native.Concat(romanised).Any(t => t.Length > 60)) throw new AdminRuleException("Each term must be at most 60 characters.", StatusCodes.Status400BadRequest);
        if (romanised.Any(t => !t.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or ' ' or '-')))
            throw new AdminRuleException("Romanised terms may contain only Latin letters.", StatusCodes.Status400BadRequest);
        if (native.Any(t => !t.Any(c => input.Language == "ta" ? Languages.IsTamilLetter(c) : Languages.IsSinhalaLetter(c))))
            throw new AdminRuleException(input.Language == "ta" ? "Native terms must be in Tamil script." : "Native terms must be in Sinhala script.", StatusCodes.Status400BadRequest);
        // Very short romanised keys would match unrelated English words.
        if (romanised.Any(t => Transliteration.Key(t).Length < 3)) throw new AdminRuleException("Romanised terms need at least 3 letters.", StatusCodes.Status400BadRequest);
        var english = input.EnglishTerms?.Trim() ?? "";
        if (english.Length is 0 or > 200 || !english.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-'))
            throw new AdminRuleException("English search terms must be 1–200 letters, digits, spaces or hyphens.", StatusCodes.Status400BadRequest);
    }

    public async Task<NutritionTerm> SaveAsync(Guid? id, TermInput input, Guid actor, CancellationToken ct)
    {
        Validate(input);
        var term = id is { } existing ? await db.NutritionTerms.SingleOrDefaultAsync(t => t.Id == existing, ct) ?? throw new KeyNotFoundException() : new NutritionTerm { CreatedByUserId = actor };
        if (id is null) db.NutritionTerms.Add(term);
        var wasApproved = term.Status == "Approved";
        term.Language = input.Language;
        term.NativeTerms = string.Join('\n', Lines(input.NativeTerms));
        term.RomanisedTerms = string.Join('\n', Lines(input.RomanisedTerms).Select(t => t.ToLowerInvariant()));
        term.EnglishTerms = input.EnglishTerms.Trim().ToLowerInvariant();
        term.Notes = input.Notes?.Trim() is { Length: > 0 } n ? n[..Math.Min(300, n.Length)] : null;
        // Any edit goes back through review.
        term.Status = "Draft";
        term.CreatedByUserId = actor;
        term.ApprovedByUserId = null;
        term.ApprovedAtUtc = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(id is null ? "terminology.create" : "terminology.edit", resourceType: "NutritionTerm", resourceId: term.Id.ToString(),
            summary: $"{term.Language}: {term.EnglishTerms}", cancellationToken: ct);
        if (wasApproved) await RefreshAsync(ct);
        return term;
    }

    public async Task ApproveAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var term = await db.NutritionTerms.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        if (term.Status != "Draft") throw new AdminRuleException("Only draft terms can be approved.");
        if (term.CreatedByUserId == actor) throw new AdminRuleException("Another reviewer must approve terms you wrote.", StatusCodes.Status403Forbidden);
        term.Status = "Approved";
        term.ApprovedByUserId = actor;
        term.ApprovedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("terminology.approve", resourceType: "NutritionTerm", resourceId: id.ToString(), summary: term.EnglishTerms, cancellationToken: ct);
        await RefreshAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var term = await db.NutritionTerms.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        term.Status = "Archived";
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("terminology.archive", resourceType: "NutritionTerm", resourceId: id.ToString(), summary: term.EnglishTerms, cancellationToken: ct);
        await RefreshAsync(ct);
    }

    /// <summary>Loads approved terms into the lexicon. Called at startup, after changes and periodically.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var approved = await db.NutritionTerms.AsNoTracking().Where(t => t.Status == "Approved").ToListAsync(ct);
        NutritionTermLexicon.SetReviewedTerms(approved.Select(t => (t.EnglishTerms, Lines(t.NativeTerms), Lines(t.RomanisedTerms))));
    }
}

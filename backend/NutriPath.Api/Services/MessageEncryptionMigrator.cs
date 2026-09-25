using Microsoft.EntityFrameworkCore;
using NutriPath.Api.Data;

namespace NutriPath.Api.Services;

/// <summary>
/// Encrypts chat messages that were saved before encryption existed. Runs
/// at startup; already-encrypted rows are skipped, so it's safe to run
/// every time and does nothing once everything is encrypted.
/// </summary>
public static class MessageEncryptionMigrator
{
    public static async Task<int> EncryptExistingAsync(NutriPathDbContext db, IMessageProtector protector)
    {
        var prefix = AesGcmMessageProtector.Prefix;
        var plaintextRows = await db.AiMessages
            .Where(m => !m.Content.StartsWith(prefix) ||
                        (m.RetrievedContextJson != null && !m.RetrievedContextJson.StartsWith(prefix)))
            .ToListAsync();

        foreach (var message in plaintextRows)
        {
            if (!protector.IsProtected(message.Content))
                message.Content = protector.Protect(message.Content);
            if (message.RetrievedContextJson != null && !protector.IsProtected(message.RetrievedContextJson))
                message.RetrievedContextJson = protector.Protect(message.RetrievedContextJson);
        }

        await db.SaveChangesAsync();
        return plaintextRows.Count;
    }
}

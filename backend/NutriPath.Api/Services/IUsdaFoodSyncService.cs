using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public interface IUsdaFoodSyncService
{
    Task<SyncJob> SyncAsync(string query, int pageSize);
}
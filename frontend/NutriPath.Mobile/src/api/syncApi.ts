import { apiClient } from './client';

// Mirrors DataSourceStatus in DTOs/SyncStatusDtos.cs.
export interface DataSourceStatus {
  name: string;
  lastSyncedAtUtc: string | null;
  daysSinceSync: number | null;
  foodCount: number;
}

export async function getSyncStatus(): Promise<DataSourceStatus[]> {
  const response = await apiClient.get<DataSourceStatus[]>('/api/sync/status');
  return response.data;
}

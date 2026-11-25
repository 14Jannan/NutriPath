import axios from 'axios';
import { API_BASE_URL, apiClient } from './client';

export interface AppStatus {
  // Optional features the server has switched on or off (admin portal → Feature flags).
  features: Record<string, boolean>;
  maintenanceMessage: string | null;
  supportEmail: string | null;
}

/** Public app status; needs no sign-in. */
export async function getAppStatus(): Promise<AppStatus> {
  const { data } = await axios.get<AppStatus>(`${API_BASE_URL}/api/app-status`, { timeout: 8000 });
  return data;
}

export type DataRequestType = 'Export' | 'Deletion';

export interface DataRequest {
  id: string;
  type: DataRequestType;
  status: 'Pending' | 'Completed' | 'Rejected' | 'Cancelled';
  createdAtUtc: string;
  processedAtUtc: string | null;
  adminNote: string | null;
  downloadAvailable: boolean;
  exportExpiresAtUtc: string | null;
}

export async function listDataRequests(): Promise<DataRequest[]> {
  const { data } = await apiClient.get<DataRequest[]>('/api/account/data-requests');
  return data;
}

export async function createDataRequest(type: DataRequestType, note?: string): Promise<void> {
  await apiClient.post('/api/account/data-requests', { type, note });
}

export async function cancelDataRequest(id: string): Promise<void> {
  await apiClient.post(`/api/account/data-requests/${id}/cancel`);
}

/** The completed export, as formatted JSON text. */
export async function downloadExport(id: string): Promise<string> {
  const { data } = await apiClient.get<unknown>(`/api/account/data-requests/${id}/export`, { responseType: 'text', transformResponse: (raw) => raw });
  return typeof data === 'string' ? data : JSON.stringify(data, null, 2);
}

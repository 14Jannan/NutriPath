import React from 'react';
import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import * as accountApi from '@/api/accountApi';
import { DataRequestsCard } from '@/components/DataRequestsCard';

jest.setTimeout(30000);

jest.mock('@/api/accountApi', () => ({
  listDataRequests: jest.fn(),
  createDataRequest: jest.fn(),
  cancelDataRequest: jest.fn(),
  downloadExport: jest.fn(),
}));
let mockEnabled = true;
jest.mock('@/hooks/useAppStatus', () => ({
  useAppStatus: () => ({ status: { features: {}, maintenanceMessage: null, supportEmail: 'help@nutripath.example' }, isEnabled: () => mockEnabled }),
}));
jest.mock('@/utils/alert', () => ({
  showAlert: jest.fn(),
  // Confirms straight away in tests.
  confirmAction: (_t: string, _m: string, _l: string, onConfirm: () => void) => onConfirm(),
}));
const api = jest.mocked(accountApi);

beforeEach(() => {
  jest.clearAllMocks();
  mockEnabled = true;
  api.listDataRequests.mockResolvedValue([]);
  api.createDataRequest.mockResolvedValue(undefined);
});

describe('DataRequestsCard', () => {
  it('requests an export and account deletion', async () => {
    await render(<DataRequestsCard />);
    await fireEvent.press(screen.getByRole('button', { name: 'Request a copy of my data' }));
    await waitFor(() => expect(api.createDataRequest).toHaveBeenCalledWith('Export'));
    await fireEvent.press(screen.getByRole('button', { name: 'Delete my account' }));
    await waitFor(() => expect(api.createDataRequest).toHaveBeenCalledWith('Deletion'));
  });

  it('shows a ready export and blocks a second pending request', async () => {
    api.listDataRequests.mockResolvedValue([
      { id: 'r1', type: 'Export', status: 'Completed', createdAtUtc: '', processedAtUtc: '', adminNote: null, downloadAvailable: true, exportExpiresAtUtc: '' },
      { id: 'r2', type: 'Deletion', status: 'Pending', createdAtUtc: '', processedAtUtc: null, adminNote: null, downloadAvailable: false, exportExpiresAtUtc: null },
    ]);
    await render(<DataRequestsCard />);
    expect(await screen.findByText('Copy of your data: ready to download')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Delete my account' })).toBeDisabled();
  });

  it('points to support when in-app requests are turned off', async () => {
    mockEnabled = false;
    await render(<DataRequestsCard />);
    expect(screen.getByText(/help@nutripath.example/)).toBeTruthy();
  });
});

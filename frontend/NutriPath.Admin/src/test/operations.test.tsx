import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { mockFetch, renderApp, signedIn } from './utils';

const flags = {
  version: 3,
  flags: [
    { key: 'assistant.enabled', name: 'AI assistant', description: 'Emergency stop', default: true, clientVisible: true, enabled: true, overridden: false },
    { key: 'usda.sync', name: 'USDA imports', description: 'Imports', default: true, clientVisible: false, enabled: true, overridden: false },
  ],
};

describe('feature flags', () => {
  it('saves a switched-off flag with the expected version', async () => {
    const { calls } = mockFetch({
      ...signedIn('SuperAdmin'),
      'GET /api/admin/feature-flags': { body: flags },
      'GET /api/admin/platform/flags/history': { body: [] },
      'PUT /api/admin/feature-flags': { body: { version: 4 } },
    });
    const user = userEvent.setup();
    renderApp('/settings/flags');
    await screen.findByText('AI assistant');
    await user.click(screen.getAllByRole('switch')[0]); // flags render in definition order
    await user.type(screen.getByLabelText(/why are you changing/i), 'Provider outage');
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    const put = await vi.waitFor(() => {
      const found = calls.find((c) => c.method === 'PUT');
      if (!found) throw new Error('not sent yet');
      return found;
    });
    expect(put.body).toEqual({ document: { flags: { 'assistant.enabled': false, 'usda.sync': true } }, expectedVersion: 3, note: 'Provider outage' });
  });

  it('is read-only without the manage permission', async () => {
    mockFetch({
      'POST /api/admin/auth/refresh': { body: { accessToken: 't' } },
      'GET /api/admin/auth/me': { body: { id: '1', email: 'ai@example.test', fullName: 'AI', roles: ['AIManager'], permissions: ['dashboard.read', 'system.monitor'], mfaEnabled: true } },
      'GET /api/admin/feature-flags': { body: flags },
      'GET /api/admin/platform/flags/history': { body: [] },
    });
    renderApp('/settings/flags');
    await screen.findByText('AI assistant');
    for (const toggle of screen.getAllByRole('switch')) expect(toggle).toBeDisabled();
    expect(screen.queryByRole('button', { name: /save changes/i })).toBeNull();
  });
});

describe('privacy requests', () => {
  it('requires typing the email before deleting an account', async () => {
    const request = { id: 'r1', userId: 'u1', type: 'Deletion', status: 'Pending', email: 'leaving@uni.lk', userNote: null, adminNote: null, createdAtUtc: '2026-11-24T10:00:00Z', processedAtUtc: null, exportExpiresAtUtc: null, exportReady: false, accountExists: true, isAdministrator: false, processedBy: null };
    const { calls } = mockFetch({
      ...signedIn('SuperAdmin'),
      'GET /api/admin/privacy/requests': { body: { items: [request], total: 1, page: 1, pageSize: 25 } },
      'POST /api/admin/privacy/requests/r1/approve': { body: { deleted: true } },
    });
    const user = userEvent.setup();
    renderApp('/privacy');
    await user.click(await screen.findByRole('button', { name: /delete/i }));
    const dialog = await screen.findByRole('dialog');
    const confirm = within(dialog).getByRole('button', { name: 'Delete account' });
    expect(confirm).toBeDisabled();
    await user.type(within(dialog).getByLabelText(/type leaving@uni.lk/i), 'leaving@uni.lk');
    await user.click(confirm);
    await vi.waitFor(() => expect(calls.some((c) => c.url === '/api/admin/privacy/requests/r1/approve')).toBe(true));
  });
});

describe('notifications', () => {
  it('shows unread notifications and opens their link', async () => {
    mockFetch({
      ...signedIn('SuperAdmin'),
      'GET /api/admin/notifications': { body: { unread: 1, items: [{ id: 'n1', type: 'safety.blocked', title: 'Validation blocked an allergy-safety guarantee', body: 'Gemini · ta', link: '/safety', tone: 'warning', createdAtUtc: new Date().toISOString(), read: false }] } },
      'POST /api/admin/notifications/n1/read': { status: 204 },
      'GET /api/admin/safety/summary': { body: { open: 1, investigating: 0, highOrCriticalActive: 1, blockedAllergyGuarantees30d: 1 } },
      'GET /api/admin/safety/incidents': { body: { items: [], total: 0, page: 1, pageSize: 25 } },
      'GET /api/admin/auth/sessions': { body: [] },
    });
    const user = userEvent.setup();
    renderApp('/profile');
    await user.click(await screen.findByRole('button', { name: 'Notifications (1 unread)' }));
    await user.click(await screen.findByText('Validation blocked an allergy-safety guarantee'));
    expect(await screen.findByRole('heading', { name: 'Safety incidents' })).toBeInTheDocument();
  });
});

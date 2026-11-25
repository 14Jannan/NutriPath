import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { CredentialStatus } from '@/api/types';
import { mockFetch, renderApp, signedIn } from './utils';

const groq: CredentialStatus = {
  provider: 'Groq', source: 'Runtime', configured: true, hint: 'abcd', activeVersion: 1, activatedAtUtc: '2026-11-20T10:00:00Z',
  lastValidatedAtUtc: null, updatedBy: 'superadmin@example.test', encryptionAvailable: true,
  history: [{ id: 'k1', version: 1, hint: 'abcd', status: 'Active', createdAtUtc: '2026-11-20T10:00:00Z', activatedAtUtc: null, lastValidatedAtUtc: null, createdBy: null }],
};

describe('API keys', () => {
  it('shows only the last four characters of a stored key', async () => {
    mockFetch({ ...signedIn('SuperAdmin'), 'GET /api/admin/credentials': { body: [groq] } });
    renderApp('/ai/keys');
    expect(await screen.findByText('••••••••abcd')).toBeInTheDocument();
  });

  it('sends a replacement key once and keeps the current key when the provider rejects it', async () => {
    const { calls } = mockFetch({
      ...signedIn('SuperAdmin'),
      'GET /api/admin/credentials': { body: [groq] },
      'PUT /api/admin/credentials/Groq': { status: 422, body: { title: 'The new key did not work, so the current key was kept.', detail: 'The provider rejected this key.' } },
    });
    const user = userEvent.setup();
    renderApp('/ai/keys');
    await user.click(await screen.findByRole('button', { name: /replace key/i }));
    const dialog = await screen.findByRole('dialog');
    const input = within(dialog).getByLabelText('New API key');
    expect(input).toHaveAttribute('type', 'password');
    await user.type(input, 'gsk_not_a_real_key_123');
    await user.click(within(dialog).getByRole('button', { name: /test and activate/i }));
    expect(await within(dialog).findByText(/current key was kept/i)).toBeInTheDocument();
    const put = calls.find((c) => c.method === 'PUT');
    expect(put?.body).toEqual({ apiKey: 'gsk_not_a_real_key_123' });
    expect(screen.queryByText('••••••••abcd')).toBeInTheDocument();
  });

  it('hides key actions from admins who may only read', async () => {
    mockFetch({
      'POST /api/admin/auth/refresh': { body: { accessToken: 't' } },
      'GET /api/admin/auth/me': { body: { id: '1', email: 'ai@example.test', fullName: 'AI', roles: ['AIManager'], permissions: ['dashboard.read', 'ai.providers.read'], mfaEnabled: true } },
      'GET /api/admin/credentials': { body: [groq] },
    });
    renderApp('/ai/keys');
    await screen.findByText('••••••••abcd');
    expect(screen.queryByRole('button', { name: /replace key/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /revoke/i })).toBeNull();
  });
});

describe('users', () => {
  it('requires a reason before suspending a user', async () => {
    const row = { id: 'u1', fullName: 'Test User', email: 'user@example.test', roles: [], emailVerified: true, status: 'Active', createdAtUtc: '2026-11-01T00:00:00Z', lastLoginAtUtc: null, preferredLanguage: 'ta', aiTokens30d: 120 };
    const { calls } = mockFetch({
      ...signedIn('SuperAdmin'),
      'GET /api/admin/users': { body: { items: [row], total: 1, page: 1, pageSize: 25 } },
      'GET /api/admin/users/u1': { body: { user: row, suspendedAtUtc: null, suspensionReason: null, lockedUntilUtc: null, mobileSessionActive: true, adminSessions: 0, aiUsageByDay: [], usageLimit: { tokenLimit: 40000, windowHours: 24, overridden: false, reason: null }, activity: [] } },
      'POST /api/admin/users/u1/suspend': { status: 204 },
    });
    const user = userEvent.setup();
    renderApp('/users');
    await user.click(await screen.findByText('Test User'));
    await user.click(await screen.findByRole('button', { name: /suspend/i }));
    const dialog = await screen.findByRole('dialog', { name: /suspend this user/i });
    const confirm = within(dialog).getByRole('button', { name: 'Suspend' });
    expect(confirm).toBeDisabled();
    await user.type(within(dialog).getByLabelText(/reason/i), 'Abusive messages');
    await user.click(confirm);
    expect(calls.find((c) => c.url === '/api/admin/users/u1/suspend')?.body).toEqual({ reason: 'Abusive messages' });
  });
});

describe('theme', () => {
  it('switches to dark mode and remembers the choice', async () => {
    mockFetch({ ...signedIn('SuperAdmin'), 'GET /api/admin/auth/sessions': { body: [] } });
    const user = userEvent.setup();
    renderApp('/profile');
    await user.click(await screen.findByRole('radio', { name: /dark/i }));
    expect(document.documentElement).toHaveClass('dark');
    expect(localStorage.getItem('nutripath-admin-theme')).toBe('dark');
  });
});

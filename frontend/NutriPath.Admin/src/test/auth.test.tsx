import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { api, session } from '@/api/client';
import { mockFetch, profileFor, renderApp, signedIn } from './utils';

const overview = {
  generatedAtUtc: new Date().toISOString(),
  definitions: { activeUser: 'x', aiMetrics: 'y' },
  users: { total: 3, verified: 3, suspended: 0, administrators: 1, newToday: 0, new7d: 1, new30d: 3, dailyActive: 1, weeklyActive: 2 },
  ai: { today: 0, thisMonth: 0, last30Days: 0, successRate: null, averageLatencyMs: null, medianLatencyMs: null, tokens30d: 0, inputTokens30d: 0, outputTokens30d: 0, recordingSince: null },
  knowledge: { approvedDocuments: 5, pendingReview: 0, drafts: 0, approvedChunks: 20, embeddingModel: 'm', indexedChunks: null, coverage: null, coverageNote: 'n' },
  foods: { total: 10, userCreated: 0 },
  jobs: { queued: 0, failed7d: 0 },
  health: [],
  recentActivity: [],
  pendingApprovals: [],
};
const dashboard = {
  'GET /api/admin/dashboard/overview': { body: overview },
  'GET /api/admin/dashboard/trends': { body: { from: '', to: '', series: [], providers: [], languages: [] } },
};

describe('sign-in', () => {
  it('redirects signed-out visitors to the login page', async () => {
    mockFetch({ 'POST /api/admin/auth/refresh': { status: 401 } });
    renderApp('/users');
    expect(await screen.findByRole('heading', { name: /sign in to the admin portal/i })).toBeInTheDocument();
  });

  it('signs in with password then authenticator code, holding the token only in memory', async () => {
    const { calls } = mockFetch({
      'POST /api/admin/auth/refresh': { status: 401 },
      'POST /api/admin/auth/login': { body: { step: 'mfa', ticket: 'ticket-1' } },
      'POST /api/admin/auth/mfa/verify': { body: { accessToken: 'tok', accessTokenExpiresAtUtc: '', sessionExpiresAtUtc: '', profile: profileFor('SuperAdmin') } },
      ...dashboard,
    });
    const user = userEvent.setup();
    renderApp('/login');
    await user.type(await screen.findByLabelText('Email'), 'superadmin@example.test');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    await user.type(await screen.findByLabelText('Authenticator code'), '123456');
    await user.click(screen.getByRole('button', { name: /verify/i }));

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
    const verify = calls.find((c) => c.url === '/api/admin/auth/mfa/verify');
    expect(verify?.body).toMatchObject({ ticket: 'ticket-1', code: '123456' });
    expect(verify?.headers['X-NutriPath-Admin']).toBe('1');
    expect(session.hasToken()).toBe(true);
    expect(JSON.stringify(localStorage)).not.toContain('tok');
    expect(JSON.stringify(sessionStorage)).not.toContain('tok');
  });

  it('shows the server error for wrong credentials', async () => {
    mockFetch({
      'POST /api/admin/auth/refresh': { status: 401 },
      'POST /api/admin/auth/login': { status: 401, body: { title: 'Incorrect email or password.' } },
    });
    const user = userEvent.setup();
    renderApp('/login');
    await user.type(await screen.findByLabelText('Email'), 'a@example.test');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Incorrect email or password.');
  });

  it('shows a locally rendered QR code and the recovery codes once during enrolment', async () => {
    mockFetch({
      'POST /api/admin/auth/refresh': { status: 401 },
      'POST /api/admin/auth/login': { body: { step: 'mfa-enroll', ticket: 't', enrollmentSecret: 'JBSWY3DPEHPK3PXP', otpAuthUri: 'otpauth://totp/NutriPath:a?secret=JBSWY3DPEHPK3PXP&issuer=NutriPath' } },
      'POST /api/admin/auth/mfa/verify': { body: { accessToken: 'tok', accessTokenExpiresAtUtc: '', sessionExpiresAtUtc: '', profile: profileFor('SuperAdmin'), recoveryCodes: ['AAAAA-BBBBB', 'CCCCC-DDDDD'] } },
      ...dashboard,
    });
    const user = userEvent.setup();
    renderApp('/login');
    await user.type(await screen.findByLabelText('Email'), 'a@example.test');
    await user.type(screen.getByLabelText('Password'), 'pw');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByLabelText('Authenticator QR code')).toBeInTheDocument();
    expect(document.querySelector('img[src^="http"]')).toBeNull();
    await user.type(screen.getByLabelText('Authenticator code'), '654321');
    await user.click(screen.getByRole('button', { name: /verify and finish setup/i }));
    expect(await screen.findByText('AAAAA-BBBBB')).toBeInTheDocument();
    const proceed = screen.getByRole('button', { name: /continue to the portal/i });
    expect(proceed).toBeDisabled();
    await user.click(screen.getByLabelText(/saved these codes/i));
    await user.click(proceed);
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
  });

  it('restores a session from the refresh cookie on reload', async () => {
    mockFetch({ ...signedIn('SuperAdmin'), ...dashboard });
    renderApp('/');
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
  });
});

describe('authorization in the UI', () => {
  it('shows only the navigation a SupportAdmin may use', async () => {
    mockFetch({ ...signedIn('SupportAdmin'), ...dashboard });
    renderApp('/');
    const nav = await screen.findByRole('navigation', { name: 'Main' });
    expect(within(nav).getByRole('link', { name: /Users/ })).toBeInTheDocument();
    expect(within(nav).queryByRole('link', { name: /API keys/ })).toBeNull();
    expect(within(nav).queryByRole('link', { name: /Administrators/ })).toBeNull();
    expect(within(nav).queryByRole('link', { name: /Audit logs/ })).toBeNull();
  });

  it('shows 403 when a page needs a permission the admin lacks', async () => {
    mockFetch(signedIn('SupportAdmin'));
    renderApp('/ai/keys');
    expect(await screen.findByText(/403/)).toBeInTheDocument();
  });

  it('shows 404 for unknown pages', async () => {
    mockFetch(signedIn('SuperAdmin'));
    renderApp('/no-such-page');
    expect(await screen.findByText(/404/)).toBeInTheDocument();
  });
});

describe('API client', () => {
  it('refreshes once on 401 and retries the request', async () => {
    let first = true;
    const { calls } = mockFetch({
      'GET /api/admin/thing': () => (first ? ((first = false), { status: 401 }) : { body: { ok: true } }),
      'POST /api/admin/auth/refresh': { body: { accessToken: 'new-token' } },
    });
    await expect(api('/api/admin/thing')).resolves.toEqual({ ok: true });
    expect(calls.map((c) => `${c.method} ${c.url}`)).toEqual(['GET /api/admin/thing', 'POST /api/admin/auth/refresh', 'GET /api/admin/thing']);
    expect(calls[2].headers.Authorization).toBe('Bearer new-token');
  });

  it('gives up when the refresh fails', async () => {
    mockFetch({ 'GET /api/admin/thing': { status: 401 }, 'POST /api/admin/auth/refresh': { status: 401 } });
    await expect(api('/api/admin/thing')).rejects.toMatchObject({ status: 401 });
    await waitFor(() => expect(session.hasToken()).toBe(false));
  });
});

import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';
import { AppRoutes, createQueryClient } from '@/App';
import { ThemeProvider } from '@/providers/theme';
import { AuthProvider } from '@/providers/auth';
import type { AdminProfile } from '@/api/types';

export type Handler = (init: RequestInit & { url: string }) => { status?: number; body?: unknown } | undefined;

/** Minimal fetch fake: the first route whose key matches "METHOD /path" (query ignored) answers. */
export function mockFetch(routes: Record<string, Handler | { status?: number; body?: unknown }>) {
  const calls: { method: string; url: string; body?: unknown; headers: Record<string, string> }[] = [];
  const fn = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const url = String(input);
    const method = (init.method ?? 'GET').toUpperCase();
    const path = url.split('?')[0];
    const body = typeof init.body === 'string' ? JSON.parse(init.body) : undefined;
    calls.push({ method, url, body, headers: (init.headers ?? {}) as Record<string, string> });
    const route = routes[`${method} ${path}`];
    const result = typeof route === 'function' ? route({ ...init, url }) : route;
    if (!result) return new Response(JSON.stringify({ title: `Unmocked ${method} ${path}` }), { status: 404 });
    const status = result.status ?? 200;
    return new Response(status === 204 || result.body === undefined ? null : JSON.stringify(result.body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    });
  });
  vi.stubGlobal('fetch', fn);
  return { fn, calls };
}

export const permissionsFor = {
  SuperAdmin: [
    'dashboard.read', 'users.read', 'users.manage', 'users.suspend', 'users.revoke_sessions', 'users.export', 'admins.read', 'admins.manage',
    'admins.assign_roles', 'ai.providers.read', 'ai.providers.manage', 'ai.secrets.rotate', 'ai.models.manage', 'ai.usage.read', 'ai.usage.manage',
    'ai.evaluate', 'knowledge.read', 'knowledge.create', 'knowledge.edit', 'knowledge.review', 'knowledge.approve', 'knowledge.archive',
    'knowledge.reindex', 'foods.read', 'foods.manage', 'foods.sync', 'system.monitor', 'system.settings.manage', 'system.jobs.manage', 'audit.read', 'audit.export',
  ],
  SupportAdmin: ['dashboard.read', 'users.read'],
};

export const profileFor = (role: keyof typeof permissionsFor): AdminProfile => ({
  id: '00000000-0000-0000-0000-000000000001',
  email: `${role.toLowerCase()}@example.test`,
  fullName: `Test ${role}`,
  roles: [role],
  permissions: permissionsFor[role],
  mfaEnabled: true,
});

/** Routes a signed-in session needs: refresh cookie exchange and profile. */
export const signedIn = (role: keyof typeof permissionsFor) => ({
  'POST /api/admin/auth/refresh': { body: { accessToken: 'test-access-token' } },
  'GET /api/admin/auth/me': { body: profileFor(role) },
});

export function renderApp(path: string) {
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <ThemeProvider>
        <MemoryRouter initialEntries={[path]}>
          <AuthProvider>
            <AppRoutes />
          </AuthProvider>
        </MemoryRouter>
      </ThemeProvider>
    </QueryClientProvider>,
  );
}

import { useEffect, useState, type ReactNode } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import * as Dialog from '@radix-ui/react-dialog';
import { Bell, ChevronsLeft, ChevronsRight, Laptop, LogOut, Menu, Moon, Search, Sun, UserCircle, X } from 'lucide-react';
import { useAuth } from '@/providers/auth';
import { useTheme, type ThemeChoice } from '@/providers/theme';
import { api } from '@/api/client';
import type { ComponentHealth } from '@/api/types';
import { navigation, titleFor } from '@/routes/nav';
import { Button, Input, StatusDot } from '@/components/ui';
import { DropdownContent, DropdownItem, DropdownLabel, DropdownMenu, DropdownSeparator, DropdownTrigger } from '@/components/overlays';
import { cn, relative } from '@/lib/utils';

export function Logo({ compact }: { compact?: boolean }) {
  return (
    <div className="flex items-center gap-2.5">
      <img src="/nutripath-icon.png" alt="" className="h-8 w-8 rounded-lg" />
      {!compact && (
        <div className="leading-tight">
          <p className="text-sm font-semibold text-fg">NutriPath</p>
          <p className="text-[11px] font-medium uppercase tracking-wider text-primary">Admin portal</p>
        </div>
      )}
    </div>
  );
}

function SidebarNav({ collapsed, onNavigate }: { collapsed?: boolean; onNavigate?: () => void }) {
  const { can } = useAuth();
  return (
    <nav aria-label="Main" className="space-y-5">
      {navigation.map((group) => {
        const items = group.items.filter((item) => !item.permission || can(item.permission));
        if (items.length === 0) return null;
        return (
          <div key={group.label}>
            {!collapsed && <p className="mb-1.5 px-3 text-[11px] font-semibold uppercase tracking-wider text-muted">{group.label}</p>}
            <ul className="space-y-0.5">
              {items.map((item) => (
                <li key={item.to}>
                  <NavLink
                    to={item.to}
                    end={item.to === '/' || item.to === '/foods'}
                    onClick={onNavigate}
                    title={collapsed ? item.label : undefined}
                    className={({ isActive }) =>
                      cn(
                        'group flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors',
                        isActive ? 'bg-primary-soft font-medium text-primary' : 'text-muted hover:bg-surface-2 hover:text-fg',
                        collapsed && 'justify-center px-2',
                      )
                    }
                  >
                    <item.icon className="h-[18px] w-[18px] shrink-0" aria-hidden />
                    {!collapsed && <span className="truncate">{item.label}</span>}
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </nav>
  );
}

const themeOptions: { value: ThemeChoice; label: string; icon: ReactNode }[] = [
  { value: 'light', label: 'Light', icon: <Sun className="h-4 w-4" /> },
  { value: 'dark', label: 'Dark', icon: <Moon className="h-4 w-4" /> },
  { value: 'system', label: 'System', icon: <Laptop className="h-4 w-4" /> },
];

function ThemeMenu() {
  const { theme, resolved, setTheme } = useTheme();
  return (
    <DropdownMenu>
      <DropdownTrigger asChild>
        <Button variant="ghost" size="icon" aria-label="Theme">
          {resolved === 'dark' ? <Moon className="h-[18px] w-[18px]" /> : <Sun className="h-[18px] w-[18px]" />}
        </Button>
      </DropdownTrigger>
      <DropdownContent>
        <DropdownLabel>Appearance</DropdownLabel>
        {themeOptions.map((option) => (
          <DropdownItem key={option.value} onSelect={() => setTheme(option.value)}>
            {option.icon} {option.label}
            {theme === option.value && <span className="ml-auto text-primary">✓</span>}
          </DropdownItem>
        ))}
      </DropdownContent>
    </DropdownMenu>
  );
}

interface Overview {
  health: ComponentHealth[];
  pendingApprovals: { id: string; title: string; updatedAtUtc: string | null }[];
}

/** Real alerts only: degraded components and documents waiting for review. */
function NotificationCenter() {
  const { can } = useAuth();
  const navigate = useNavigate();
  const { data } = useQuery({
    queryKey: ['dashboard-overview'],
    queryFn: () => api<Overview>('/api/admin/dashboard/overview'),
    enabled: can('dashboard.read'),
    staleTime: 60_000,
  });
  const alerts = data?.health.filter((h) => h.status !== 'Healthy') ?? [];
  const pending = can('knowledge.read') ? data?.pendingApprovals ?? [] : [];
  const count = alerts.length + pending.length;
  return (
    <DropdownMenu>
      <DropdownTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={`Notifications (${count})`} className="relative">
          <Bell className="h-[18px] w-[18px]" />
          {count > 0 && <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-danger ring-2 ring-surface" />}
        </Button>
      </DropdownTrigger>
      <DropdownContent>
        <div className="w-80 max-w-[85vw]">
          <DropdownLabel>Notifications</DropdownLabel>
          {count === 0 && <p className="px-3 py-4 text-sm text-muted">Nothing needs your attention.</p>}
          {alerts.map((a) => (
            <DropdownItem key={a.name} onSelect={() => navigate('/health')}>
              <StatusDot tone={a.status === 'Unavailable' ? 'danger' : 'warning'} />
              <span className="min-w-0">
                <span className="block text-sm">{a.name}: {a.status}</span>
                {a.detail && <span className="block truncate text-xs text-muted">{a.detail}</span>}
              </span>
            </DropdownItem>
          ))}
          {pending.length > 0 && <DropdownSeparator />}
          {pending.map((p) => (
            <DropdownItem key={p.id} onSelect={() => navigate(`/knowledge/${p.id}`)}>
              <StatusDot tone="info" />
              <span className="min-w-0">
                <span className="block truncate text-sm">Review: {p.title}</span>
                <span className="block text-xs text-muted">Submitted {relative(p.updatedAtUtc)}</span>
              </span>
            </DropdownItem>
          ))}
        </div>
      </DropdownContent>
    </DropdownMenu>
  );
}

function GlobalSearch() {
  const { can } = useAuth();
  const navigate = useNavigate();
  const [text, setText] = useState('');
  if (!can('users.read')) return null;
  return (
    <form
      role="search"
      className="relative hidden w-full max-w-xs md:block"
      onSubmit={(e) => {
        e.preventDefault();
        if (text.trim()) navigate(`/users?search=${encodeURIComponent(text.trim())}`);
      }}
    >
      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
      <Input aria-label="Search users by name or email" placeholder="Search users…" value={text} onChange={(e) => setText(e.target.value)} className="h-9 pl-9" />
    </form>
  );
}

export function AdminLayout() {
  const { profile, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem('nutripath-admin-sidebar') === 'collapsed';
    } catch {
      return false;
    }
  });
  const [mobileOpen, setMobileOpen] = useState(false);
  useEffect(() => {
    try {
      localStorage.setItem('nutripath-admin-sidebar', collapsed ? 'collapsed' : 'expanded');
    } catch {
      /* ignore */
    }
  }, [collapsed]);
  const current = titleFor(location.pathname);
  useEffect(() => {
    document.title = `${current.label} · NutriPath Admin`;
  }, [current.label]);

  return (
    <div className="flex min-h-full">
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-surface focus:px-3 focus:py-2">
        Skip to content
      </a>
      <aside className={cn('sticky top-0 hidden h-dvh shrink-0 flex-col border-r border-border bg-surface transition-[width] lg:flex', collapsed ? 'w-[72px]' : 'w-64')}>
        <div className={cn('flex h-16 items-center border-b border-border px-4', collapsed && 'justify-center px-2')}>
          <Link to="/" aria-label="Dashboard">
            <Logo compact={collapsed} />
          </Link>
        </div>
        <div className="flex-1 overflow-y-auto px-3 py-4">
          <SidebarNav collapsed={collapsed} />
        </div>
        <div className="border-t border-border p-3">
          <Button variant="ghost" size="sm" className="w-full" onClick={() => setCollapsed((c) => !c)} aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}>
            {collapsed ? <ChevronsRight className="h-4 w-4" /> : <><ChevronsLeft className="h-4 w-4" /> Collapse</>}
          </Button>
        </div>
      </aside>

      <Dialog.Root open={mobileOpen} onOpenChange={setMobileOpen}>
        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-40 bg-black/40 lg:hidden" />
          <Dialog.Content className="fixed inset-y-0 left-0 z-50 flex w-72 max-w-[85vw] flex-col bg-surface shadow-2xl lg:hidden">
            <Dialog.Title className="sr-only">Navigation</Dialog.Title>
            <Dialog.Description className="sr-only">Admin portal sections</Dialog.Description>
            <div className="flex h-16 items-center justify-between border-b border-border px-4">
              <Logo />
              <Dialog.Close asChild>
                <Button variant="ghost" size="icon" aria-label="Close navigation">
                  <X className="h-5 w-5" />
                </Button>
              </Dialog.Close>
            </div>
            <div className="flex-1 overflow-y-auto px-3 py-4">
              <SidebarNav onNavigate={() => setMobileOpen(false)} />
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b border-border bg-surface/85 px-4 backdrop-blur sm:px-6">
          <Button variant="ghost" size="icon" className="lg:hidden" onClick={() => setMobileOpen(true)} aria-label="Open navigation">
            <Menu className="h-5 w-5" />
          </Button>
          <nav aria-label="Breadcrumb" className="min-w-0 flex-1">
            <ol className="flex items-center gap-1.5 text-sm text-muted">
              <li className="hidden sm:block"><Link to="/" className="hover:text-fg">Admin</Link></li>
              <li className="hidden sm:block" aria-hidden>/</li>
              <li className="truncate font-medium text-fg" aria-current="page">{current.label}</li>
            </ol>
          </nav>
          <GlobalSearch />
          <NotificationCenter />
          <ThemeMenu />
          <DropdownMenu>
            <DropdownTrigger asChild>
              <button className="flex items-center gap-2 rounded-full p-0.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring cursor-pointer" aria-label="Account menu">
                <span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary text-xs font-semibold text-primary-fg">
                  {(profile?.fullName || profile?.email || '?').slice(0, 1).toUpperCase()}
                </span>
              </button>
            </DropdownTrigger>
            <DropdownContent>
              <div className="px-3 py-2">
                <p className="truncate text-sm font-medium text-fg">{profile?.fullName || 'Administrator'}</p>
                <p className="truncate text-xs text-muted">{profile?.email}</p>
                <p className="mt-1 text-[11px] text-primary">{profile?.roles.join(' · ')}</p>
              </div>
              <DropdownSeparator />
              <DropdownItem onSelect={() => navigate('/profile')}><UserCircle className="h-4 w-4" /> My profile</DropdownItem>
              <DropdownItem danger onSelect={() => void logout().then(() => navigate('/login'))}><LogOut className="h-4 w-4" /> Sign out</DropdownItem>
            </DropdownContent>
          </DropdownMenu>
        </header>
        <main id="main" className="mx-auto w-full max-w-[1400px] flex-1 px-4 py-6 sm:px-6 lg:px-8">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

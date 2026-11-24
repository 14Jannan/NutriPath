import {
  Activity, BarChart3, BookOpenCheck, Boxes, Cpu, Database, FlaskConical, Gauge, KeyRound, LayoutDashboard,
  ListChecks, ScrollText, ShieldCheck, SlidersHorizontal, UserCircle, Users, UtensilsCrossed, Wheat, type LucideIcon,
} from 'lucide-react';

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  /** Required permission; undefined means any signed-in administrator. */
  permission?: string;
}

export interface NavGroup {
  label: string;
  items: NavItem[];
}

/** Only implemented modules appear here. Each is also protected by the server and the route guard. */
export const navigation: NavGroup[] = [
  { label: 'Overview', items: [{ to: '/', label: 'Dashboard', icon: LayoutDashboard, permission: 'dashboard.read' }] },
  {
    label: 'User management',
    items: [
      { to: '/users', label: 'Users', icon: Users, permission: 'users.read' },
      { to: '/administrators', label: 'Administrators', icon: ShieldCheck, permission: 'admins.read' },
      { to: '/roles', label: 'Roles & permissions', icon: ListChecks, permission: 'admins.read' },
    ],
  },
  {
    label: 'AI management',
    items: [
      { to: '/ai/providers', label: 'AI providers', icon: Cpu, permission: 'ai.providers.read' },
      { to: '/ai/keys', label: 'API keys', icon: KeyRound, permission: 'ai.providers.read' },
      { to: '/ai/configuration', label: 'AI configuration', icon: SlidersHorizontal, permission: 'ai.providers.read' },
      { to: '/ai/usage', label: 'AI usage', icon: BarChart3, permission: 'ai.usage.read' },
      { to: '/ai/playground', label: 'AI playground', icon: FlaskConical, permission: 'ai.evaluate' },
    ],
  },
  {
    label: 'Nutrition management',
    items: [
      { to: '/knowledge', label: 'Knowledge base', icon: BookOpenCheck, permission: 'knowledge.read' },
      { to: '/embeddings', label: 'Embeddings', icon: Boxes, permission: 'knowledge.read' },
      { to: '/foods', label: 'Food catalogue', icon: UtensilsCrossed, permission: 'foods.read' },
      { to: '/foods/usda', label: 'USDA sync', icon: Wheat, permission: 'foods.read' },
    ],
  },
  {
    label: 'Operations',
    items: [
      { to: '/jobs', label: 'Background jobs', icon: Database, permission: 'system.monitor' },
      { to: '/health', label: 'System health', icon: Activity, permission: 'system.monitor' },
    ],
  },
  {
    label: 'Administration',
    items: [
      { to: '/audit', label: 'Audit logs', icon: ScrollText, permission: 'audit.read' },
      { to: '/profile', label: 'My profile', icon: UserCircle },
    ],
  },
];

export const allItems = navigation.flatMap((g) => g.items);

export function titleFor(pathname: string) {
  const match = allItems.filter((i) => i.to === pathname || (i.to !== '/' && pathname.startsWith(i.to)))
    .sort((a, b) => b.to.length - a.to.length)[0];
  return match ?? { to: pathname, label: 'Not found', icon: Gauge };
}

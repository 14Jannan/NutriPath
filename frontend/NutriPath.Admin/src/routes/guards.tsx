import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router';
import { Loader2 } from 'lucide-react';
import { useAuth } from '@/providers/auth';
import { ForbiddenPage } from '@/features/auth/AccountPages';

export function FullPageSpinner() {
  return (
    <div className="flex min-h-full items-center justify-center" role="status" aria-label="Loading">
      <Loader2 className="h-6 w-6 animate-spin text-primary" />
    </div>
  );
}

/** Signed-in administrators only. The server enforces this independently. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  const location = useLocation();
  if (status === 'loading') return <FullPageSpinner />;
  if (status === 'expired') return <Navigate to="/session-expired" replace />;
  if (status !== 'signed-in') return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  return <>{children}</>;
}

/** Hides a page from administrators without the permission (the API also refuses the calls). */
export function RequirePermission({ permission, children }: { permission?: string; children: ReactNode }) {
  const { can } = useAuth();
  if (permission && !can(permission)) return <ForbiddenPage />;
  return <>{children}</>;
}

'use client';

import { type ReactNode } from 'react';
import { Shell } from '@/components/atom/Shell';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';

/**
 * Layout de /admin/jobs/*. Mismo chrome que /admin/quipux: JWT + Shell.
 * Solo SuperAdmin (middleware /admin/*).
 */
export default function AdminJobsLayout({ children }: { children: ReactNode }) {
  const { authed, hydrated, logout } = useAuthGate();

  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);

  if (!hydrated || !authed) return null;

  return (
    <Shell onLogout={logout} visibleModuleCodes={modulesLoading ? [] : accessibleCodes}>
      <div className="app-bg min-h-screen w-full">{children}</div>
    </Shell>
  );
}

'use client';

import { type ReactNode } from 'react';
import { Shell } from '@/components/atom/Shell';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';

/**
 * Layout de /admin/causales-rechazo. Mismo patrón que /admin/documents: replica el chrome de la
 * SPA para que el dock no desaparezca al entrar al catálogo.
 */
export default function AdminRejectionReasonsLayout({ children }: { children: ReactNode }) {
  const { authed, hydrated, logout } = useAuthGate();

  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);

  if (!hydrated || !authed) return null;

  return (
    <Shell
      onLogout={logout}
      visibleModuleCodes={modulesLoading ? [] : accessibleCodes}
    >
      <div className="app-bg min-h-screen w-full">{children}</div>
    </Shell>
  );
}

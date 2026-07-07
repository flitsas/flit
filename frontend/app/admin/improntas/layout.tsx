'use client';

import { type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { Shell, type ModuleId } from '@/components/atom/Shell';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';

/**
 * Layout de /admin/improntas/*. Copia exacta del patrón de /admin/transit-offices y
 * /admin/companies: replica el chrome de la SPA (auth real por JWT + Shell con dock)
 * para que el dock NO desaparezca al entrar al módulo "Generación de improntas". El
 * dock navega de vuelta a la SPA (/?m=…) o a /tramites; el botón "Improntas" del dock
 * queda resaltado por la ruta (HU #10469).
 */
export default function AdminImprontasLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { authed, hydrated, logout } = useAuthGate();

  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);

  const handleNav = (m: ModuleId) => {
    if (m === 'tramites') router.push('/tramites');
    else router.push(`/?m=${m}`);
  };

  if (!hydrated || !authed) return null;

  return (
    <Shell active="dashboard" onNav={handleNav} onLogout={logout} visibleModuleCodes={modulesLoading ? [] : accessibleCodes}>
      <div className="app-bg min-h-screen w-full">{children}</div>
    </Shell>
  );
}

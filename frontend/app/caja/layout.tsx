'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { Shell } from '@/components/atom/Shell';
import { ToastProvider } from '@/components/admin/Toast';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';
import { getToken } from '@/lib/api/client';
import { accessDeniedPath } from '@/lib/auth/guard';
import { canUseCaja, decodeJwtPayload } from '@/lib/auth/jwt';

/**
 * Layout de /caja: JWT + Shell con el dock. Solo la persona de caja (`ot_caja`) y el Admin OT entran; cualquier otro
 * rol vuelve al inicio con «No tienes acceso a este módulo.». La autorización real la hace la API.
 */
export default function CajaLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { authed, hydrated, logout } = useAuthGate();
  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);
  const [permitido, setPermitido] = useState<boolean | null>(null);

  useEffect(() => {
    if (!hydrated || !authed) return;
    const ok = canUseCaja(decodeJwtPayload(getToken()));
    // eslint-disable-next-line react-hooks/set-state-in-effect -- el token solo existe en el navegador
    setPermitido(ok);
    if (!ok) router.replace(accessDeniedPath('caja'));
  }, [hydrated, authed, router]);

  if (!hydrated || !authed || !permitido) return null;

  return (
    <ToastProvider>
      <Shell onLogout={logout} visibleModuleCodes={modulesLoading ? [] : accessibleCodes}>
        <div className="app-bg flex min-h-screen flex-col gap-4 px-6 pt-6 pb-10 text-foreground">{children}</div>
      </Shell>
    </ToastProvider>
  );
}

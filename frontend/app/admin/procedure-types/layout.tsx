'use client';

import { type ReactNode, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Shell, type ModuleId } from '@/components/atom/Shell';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';

/**
 * Layout de /admin/procedure-types/* (FE-07, HU #10842). Igual que el de /admin/documents:
 * replica el chrome de la SPA (auth real por JWT + Shell con dock) para que el dock NO
 * desaparezca al entrar al Configurador. El dock navega de vuelta a la SPA (/?m=…) o a
 * /tramites; la entrada "Parametrización de trámites" queda resaltada por la ruta actual.
 *
 * Gating: el Configurador es SuperAdmin-only (mismo criterio que su entrada en el dock,
 * Shell.tsx). Un usuario autenticado que NO es SuperAdmin y llega por URL directa se
 * redirige a /403 (defensa en profundidad; el backend además exige el JWT SuperAdmin).
 * Envuelve el contenido en un contenedor de alto completo (h-full) para que el
 * ParametrizationWizard, que usa `h-full`, resuelva su alto y gestione su scroll interno.
 */
export default function AdminProcedureTypesLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { authed, hydrated, logout } = useAuthGate();
  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);

  // El claim SuperAdmin no cambia durante la sesión: se lee de forma perezosa (no reactiva).
  const [superAdmin] = useState<boolean>(() => isSuperAdmin(decodeJwtPayload(getToken())));

  const handleNav = (m: ModuleId) => {
    if (m === 'tramites') router.push('/tramites');
    else router.push(`/?m=${m}`);
  };

  if (!hydrated || !authed) return null;
  if (!superAdmin) {
    router.replace('/403');
    return null;
  }

  return (
    <Shell active="dashboard" onNav={handleNav} onLogout={logout} visibleModuleCodes={modulesLoading ? [] : accessibleCodes}>
      <div className="app-bg h-full w-full">{children}</div>
    </Shell>
  );
}

'use client';

import { type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { ShieldX } from 'lucide-react';
import { Shell, type ModuleId } from '@/components/atom/Shell';
import { canSeeGeneracionDocumental } from '@/components/admin/generacion-documental/generacion-documental-nav';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';

/**
 * Layout de /admin/generacion-documental/* (HU-01, Feature #12201).
 *
 * Mismo patrón de chrome que /admin/improntas: auth real por JWT + `Shell` con dock, para
 * que el dock no desaparezca dentro del módulo.
 *
 * Diferencia deliberada (R12): el acceso NO se resuelve por rol sino por los MÓDULOS
 * ACCESIBLES del usuario. Un AdminCompany con `generacion-documental.read` entra; un
 * usuario sin el módulo ve el estado de acceso denegado y no se monta ninguna vista del
 * módulo — no se pide el historial ni se filtra un solo dato.
 *
 * `ready` (no `!loading`) es lo que distingue «todavía no pregunté» de «pregunté y no hay
 * módulos»: sin esa distinción se denegaría el acceso durante el primer render.
 */
export default function AdminGeneracionDocumentalLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { authed, hydrated, logout } = useAuthGate();

  const { modules: accessibleModules, loading: modulesLoading, ready } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);
  const allowed = canSeeGeneracionDocumental(accessibleCodes);

  const handleNav = (m: ModuleId) => {
    if (m === 'tramites') router.push('/tramites');
    else router.push(`/?m=${m}`);
  };

  if (!hydrated || !authed) return null;

  return (
    <Shell
      active="dashboard"
      onNav={handleNav}
      onLogout={logout}
      visibleModuleCodes={modulesLoading ? [] : accessibleCodes}
    >
      <div className="app-bg min-h-screen w-full">
        {!ready ? (
          <div
            className="flex min-h-[40vh] items-center justify-center px-6 text-xs"
            role="status"
            aria-busy="true"
            aria-live="polite"
          >
            Verificando tus permisos del módulo…
          </div>
        ) : allowed ? (
          children
        ) : (
          <section
            role="alert"
            className="mx-auto flex min-h-[40vh] max-w-md flex-col items-center justify-center gap-3 px-6 text-center"
          >
            <ShieldX className="h-10 w-10" style={{ color: '#FF4E00' }} aria-hidden="true" />
            <h1 className="text-lg font-bold" style={{ color: '#162744' }}>
              Acceso restringido
            </h1>
            <p className="text-xs opacity-70">
              No tienes habilitado el módulo de generación documental. Si crees que es un error,
              contacta al administrador de tu compañía.
            </p>
            <button
              type="button"
              onClick={() => router.push('/')}
              className="rounded-xl px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
              style={{ background: '#557EFF' }}
            >
              Volver al inicio
            </button>
          </section>
        )}
      </div>
    </Shell>
  );
}

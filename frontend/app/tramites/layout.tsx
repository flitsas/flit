'use client';

import { type ReactNode } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { Shell, type ModuleId } from '@/components/atom/Shell';
import { ToastProvider } from '@/components/admin/Toast';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';

/**
 * Track B — layout de las rutas /tramites/*. Replica el chrome de la SPA atom
 * (auth real por JWT + Shell con dock) pero como segmento de ruta propio:
 *
 * - Auth: sin sesión activa (useAuthGate), navega a /login en vez de renderizar.
 * - Dock: "Trámites" navega a /tramites; el resto vuelve a / (allí viven por
 *   setState; deep-link por módulo queda para después — fuera de alcance).
 * - Contenedor hijo: en listado y detalle el scroll vive en `main` del Shell
 *   (`[data-shell-scroll]`). En detalle (modo inmersivo) no se clipa el viewport:
 *   el contenido crece y el asistente de seguimiento es sticky flotante.
 */
export default function TramitesLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { authed, hydrated, logout } = useAuthGate();

  // Modo inmersivo: ruta /tramites/[instanceId] (2 segmentos, el 2º no es
  // "nuevo"). /tramites → 1 segmento; /tramites/nuevo/x → 3 segmentos.
  const segments = pathname.split('/').filter(Boolean);
  const immersive =
    segments.length === 2 && segments[0] === 'tramites' && segments[1] !== 'nuevo';

  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);

  const handleNav = (m: ModuleId) => {
    if (m === 'tramites') router.push('/tramites');
    else router.push(`/?m=${m}`);
  };

  if (!hydrated || !authed) return null;

  return (
    // ToastProvider envuelve todas las rutas /tramites/*: el layout no se
    // desmonta al ir de /tramites/[id] → /tramites, así el toast de "enviado a
    // tránsito" sigue visible tras la redirección que dispara Finalizar.
    <ToastProvider>
    <Shell active="tramites" onNav={handleNav} onLogout={logout} visibleModuleCodes={modulesLoading ? [] : accessibleCodes}>
      <div
        className={
          immersive
            ? 'app-bg flex min-h-full flex-col gap-3 px-6 pt-4 pb-24 text-[#162744] dark:text-white'
            : 'app-bg flex min-h-screen flex-col gap-4 px-6 pt-6 pb-10 text-[#162744] dark:text-white'
        }
      >
        {children}
      </div>
    </Shell>
    </ToastProvider>
  );
}

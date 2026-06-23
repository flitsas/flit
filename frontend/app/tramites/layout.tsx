'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { List } from 'lucide-react';
import { Login } from '@/components/atom/Login';
import { Shell, type ModuleId } from '@/components/atom/Shell';
import { ModuleTitle } from '@/components/atom/modules/ModuleTitle';
import { getToken } from '@/lib/api/client';
import { clearToken, getRememberedEmail } from '@/lib/auth/session';

/**
 * Track B — layout de las rutas /tramites/*. Replica el chrome de la SPA atom
 * (auth real por JWT + Shell con dock) pero como segmento de ruta propio:
 *
 * - Auth: si no hay token, renderiza <Login> en lugar del Shell (igual que /).
 * - Dock: "Trámites" navega a /tramites; el resto vuelve a / (allí viven por
 *   setState; deep-link por módulo queda para después — fuera de alcance).
 * - Chrome compartido (ModuleTitle + tab Operación) se pinta aquí salvo en modo
 *   inmersivo: /tramites/[instanceId] da todo el viewport al wizard (Track A).
 * - Contenedor hijo: scroll único (overflow-y-auto) + pb-24 para el dock.
 */
export default function TramitesLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const [authed, setAuthed] = useState(false);
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    // El token solo está disponible en cliente; se lee tras el montaje.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setAuthed(Boolean(getToken()));
    setHydrated(true);
  }, []);

  // Modo inmersivo: ruta /tramites/[instanceId] (2 segmentos, el 2º no es
  // "nuevo"). /tramites → 1 segmento; /tramites/nuevo/x → 3 segmentos.
  const segments = pathname.split('/').filter(Boolean);
  const immersive =
    segments.length === 2 && segments[0] === 'tramites' && segments[1] !== 'nuevo';

  const handleNav = (m: ModuleId) => {
    if (m === 'tramites') router.push('/tramites');
    else router.push('/');
  };

  const handleLogout = () => {
    clearToken();
    setAuthed(false);
  };

  if (!hydrated) return null;

  if (!authed) {
    return (
      <Login onAuthenticated={() => setAuthed(true)} defaultEmail={getRememberedEmail()} />
    );
  }

  return (
    <Shell active="tramites" onNav={handleNav} onLogout={handleLogout}>
      <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-y-auto">
        {!immersive && (
          <>
            <ModuleTitle
              title="Gestión Integral de Trámites"
              subtitle="Embudo de tus trámites vehiculares"
            />
            <div className="flex items-center gap-2 shrink-0">
              <button
                onClick={() => router.push('/tramites')}
                className="flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold border transition"
                style={{
                  borderColor: '#557EFF',
                  background: 'rgba(85,126,255,0.10)',
                  color: '#557EFF',
                }}
              >
                <List className="h-3.5 w-3.5" />
                Operación
              </button>
            </div>
          </>
        )}
        {children}
      </div>
    </Shell>
  );
}

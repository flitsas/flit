'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { Login } from '@/components/atom/Login';
import { Shell, type ModuleId } from '@/components/atom/Shell';
import { getToken } from '@/lib/api/client';
import { clearToken, getRememberedEmail } from '@/lib/auth/session';

/**
 * Layout de /admin/transit-offices/*. Igual que el de /admin/companies: replica el chrome
 * de la SPA (auth real por JWT + Shell con dock) para que el dock NO desaparezca al entrar
 * a la consola de organismos de tránsito ni a sus subpantallas. El dock navega de vuelta
 * a la SPA (/?m=…) o a /tramites; el botón "Tránsito" del dock queda resaltado por la ruta.
 */
export default function AdminTransitOfficesLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [authed, setAuthed] = useState(false);
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    // El token solo está disponible en cliente; se lee tras el montaje.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setAuthed(Boolean(getToken()));
    setHydrated(true);
  }, []);

  const handleNav = (m: ModuleId) => {
    if (m === 'tramites') router.push('/tramites');
    else router.push(`/?m=${m}`);
  };

  const handleLogout = () => {
    clearToken();
    setAuthed(false);
  };

  if (!hydrated) return null;

  if (!authed) {
    return <Login onAuthenticated={() => setAuthed(true)} defaultEmail={getRememberedEmail()} />;
  }

  return (
    <Shell active="dashboard" onNav={handleNav} onLogout={handleLogout}>
      <div className="h-full w-full overflow-y-auto pb-24">{children}</div>
    </Shell>
  );
}

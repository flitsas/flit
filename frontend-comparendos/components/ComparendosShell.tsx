"use client";

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { SuiteShell, type ShellUser } from "@flit/shell/SuiteShell";
import { apiFetch } from "@/lib/api";
import { comparendosNav } from "@/lib/nav";
import { capacidades, type SesionComparendos } from "@/lib/sesion";

export interface UsuarioShell extends ShellUser {
  permissions: string[];
  roles: string[];
  isSuperAdmin: boolean;
}

interface EstadoSesion {
  sesion: SesionComparendos | null;
  cargando: boolean;
  error: string | null;
}

const SesionContext = createContext<EstadoSesion>({ sesion: null, cargando: true, error: null });

/** Sesión de Comparendos (permisos y capacidades de la empresa) para cualquier pantalla. */
export function useSesion(): EstadoSesion {
  return useContext(SesionContext);
}

/**
 * Barra y dock de la suite para Comparendos. El catálogo depende de las capacidades de la empresa, que llegan de la API
 * al abrir; mientras tanto el dock muestra solo lo que no depende de ellas.
 */
export function ComparendosShell({ user, tramitesUrl, children }: { user: UsuarioShell; tramitesUrl: string; children: ReactNode }) {
  const [estado, setEstado] = useState<EstadoSesion>({ sesion: null, cargando: true, error: null });

  useEffect(() => {
    const controller = new AbortController();
    apiFetch<SesionComparendos>("/sesion", { signal: controller.signal })
      .then((sesion) => setEstado({ sesion, cargando: false, error: null }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setEstado({ sesion: null, cargando: false, error: e instanceof Error ? e.message : "No se pudo abrir Comparendos." });
      });
    return () => controller.abort();
  }, []);

  const nav = useMemo(() => comparendosNav(capacidades(estado.sesion)), [estado.sesion]);

  return (
    <SesionContext.Provider value={estado}>
      <SuiteShell productCode="comparendos" productName="Comparendos" nav={nav} user={user} accountUrl={tramitesUrl} layout="app">
        <div className="app-bg flex min-h-full flex-col gap-4 px-4 pt-6 pb-28 text-flit-primary md:px-6 dark:text-white">{children}</div>
      </SuiteShell>
    </SesionContext.Provider>
  );
}

"use client";

import { createContext, useCallback, useContext, useEffect, useEffectEvent, useMemo, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Settings } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { SuiteShell, type NavCatalog, type ShellUser } from "@flit/shell/SuiteShell";
import { ApiError, apiFetch } from "@/lib/api";
import type { SesionDiagnostico } from "@/lib/sesion";

export interface UsuarioShell extends ShellUser {
  permissions: string[];
  roles: string[];
  isSuperAdmin: boolean;
}

export interface EstadoSesion {
  sesion: SesionDiagnostico | null;
  cargando: boolean;
  error: string | null;
  recargar: () => void;
}

const SesionContext = createContext<EstadoSesion>({ sesion: null, cargando: true, error: null, recargar: () => {} });

/** Sesión de Diagnóstico (GET /api/v1/diagnostico/sesion) para cualquier pantalla dentro del shell. */
export function useSesion(): EstadoSesion {
  return useContext(SesionContext);
}

// El catálogo del dock crece con los módulos del producto. La pantalla 10 (HU #13744) la ve quien puede leer la
// configuración de su compañía o editar contratos desde el backoffice FLIT; la API vuelve a exigir cada permiso.
const NAV: NavCatalog = {
  sections: [{ id: "ajustes", label: "Ajustes", icon: Settings, side: "right" }],
  items: [
    {
      key: "configuracion",
      label: "Configuración",
      href: "/configuracion",
      section: "ajustes",
      icon: Settings,
      permission: ["diagnostico.configuracion.ver", "diagnostico.configuracion.contrato.editar"],
    },
  ],
};

/** Código de la 403 cuando la API niega el acceso sin decir por qué (usuario sin ningún permiso diagnostico.*). */
const SIN_PERMISOS = "SIN_PERMISOS_DIAGNOSTICO";

/**
 * Barra y dock de la suite para Diagnóstico. Al abrir pide la sesión del producto: mientras llega muestra el loader de
 * marca; si la API la niega (403: compañía sin Diagnóstico activo, suscripción no verificable o usuario sin permisos)
 * lleva a la página 403, que está fuera del middleware y no vuelve al login; ante cualquier otra falla, el error con
 * Reintentar. La compañía es la de la sesión: una sesión = una compañía, sin selector.
 */
export function DiagnosticoShell({ user, tramitesUrl, children }: { user: UsuarioShell; tramitesUrl: string; children: ReactNode }) {
  const router = useRouter();
  const [intento, setIntento] = useState(0);
  const [estado, setEstado] = useState<Omit<EstadoSesion, "recargar">>({ sesion: null, cargando: true, error: null });

  const irA403 = useEffectEvent((code: string) => router.replace(`/403?code=${encodeURIComponent(code)}`));

  useEffect(() => {
    const controller = new AbortController();
    apiFetch<SesionDiagnostico>("/sesion", { signal: controller.signal })
      .then((sesion) => setEstado({ sesion, cargando: false, error: null }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        if (e instanceof ApiError && e.status === 403) {
          // Se queda en «cargando» mientras navega: la pantalla base nunca se muestra.
          irA403(e.code || SIN_PERMISOS);
          return;
        }
        setEstado({ sesion: null, cargando: false, error: e instanceof Error ? e.message : "No se pudo abrir Diagnóstico." });
      });
    return () => controller.abort();
  }, [intento]);

  const recargar = useCallback(() => {
    setEstado({ sesion: null, cargando: true, error: null });
    setIntento((i) => i + 1);
  }, []);

  const valor = useMemo(() => ({ ...estado, recargar }), [estado, recargar]);
  const usuario = useMemo(() => ({ ...user, tenantName: estado.sesion?.empresa.nombre || user.tenantName }), [user, estado.sesion]);

  let contenido: ReactNode = children;
  if (estado.cargando) contenido = <VehicleLoader label="Abriendo Diagnóstico…" />;
  else if (estado.error)
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo abrir Diagnóstico"
        action={
          <button
            type="button"
            onClick={recargar}
            className="rounded-full border border-current px-3 py-1 text-xs font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--nav-focus)]"
          >
            Reintentar
          </button>
        }
      >
        {estado.error}
      </InlineAlert>
    );

  return (
    <SesionContext.Provider value={valor}>
      <SuiteShell productCode="diagnostico" productName="Diagnóstico" nav={NAV} user={usuario} accountUrl={tramitesUrl} layout="app">
        <div className="flex min-h-full flex-col gap-4 px-4 pt-6 text-flit-primary md:px-6 dark:text-white">{contenido}</div>
      </SuiteShell>
    </SesionContext.Provider>
  );
}

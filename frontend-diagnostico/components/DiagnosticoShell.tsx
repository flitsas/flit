"use client";

import { createContext, useCallback, useContext, useEffect, useEffectEvent, useMemo, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { SuiteShell, type NavCatalog, type ShellUser } from "@flit/shell/SuiteShell";
import { rutaKit } from "@flit/ui/iconos-mapas";
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
  /** Origen de Trámites (TRAMITES_URL): enlaces «Ver trámite #N» del tablero (Punto 4). Vacío fuera del shell. */
  tramitesUrl?: string;
}

const SesionContext = createContext<EstadoSesion>({ sesion: null, cargando: true, error: null, recargar: () => {}, tramitesUrl: "" });

/** Sesión de Diagnóstico (GET /api/v1/diagnostico/sesion) para cualquier pantalla dentro del shell. */
export function useSesion(): EstadoSesion {
  return useContext(SesionContext);
}

// El catálogo del dock crece con los módulos del producto. Cada grupo y entrada lleva su ilustración (HU #14487), solo
// presentación. La pantalla 10 (HU #13744) la ve quien puede leer la
// configuración de su compañía o editar contratos desde el backoffice FLIT; la API vuelve a exigir cada permiso.
const NAV: NavCatalog = {
  sections: [{ id: "ajustes", label: "Ajustes", icon: iconoComo("ajustes"), side: "right", ilustracion: rutaKit("modulo/configuracion") }],
  items: [
    {
      key: "configuracion",
      label: "Configuración",
      href: "/configuracion",
      section: "ajustes",
      icon: iconoComo("ajustes"),
      ilustracion: rutaKit("modulo/configuracion"),
      permission: ["diagnostico.configuracion.ver", "diagnostico.configuracion.contrato.editar"],
    },
  ],
};

/**
 * Dock del backoffice (HU #13802, F06-C7 y ADR-0078): solo con la capacidad `backoffice` de GET /sesion, que la API da al
 * SuperAdmin de cualquier compañía o al usuario de la compañía FLIT con diagnostico.backoffice.ver (ola 2026-11-11, punto 1).
 * El admin de una compañía que no es SuperAdmin nunca la ve; la ruta, además, lo lleva a la 403 (AccesoBackoffice). El
 * override del dictamen y el llenado manual exigen además `backofficeOperacion` (solo FLIT). «Fuentes obligatorias» vive solo aquí: `NAV`
 * de la compañía no la lleva.
 */
const NAV_BACKOFFICE: NavCatalog = {
  sections: [{ id: "backoffice", label: "Backoffice", icon: iconoComo("escudo"), side: "left", ilustracion: rutaKit("modulo/diagnostico") }, ...NAV.sections],
  items: [
    { key: "backoffice-diagnosticos", label: "Backoffice", href: "/backoffice/diagnosticos", section: "backoffice", icon: iconoComo("escudo"), ilustracion: rutaKit("modulo/diagnostico") },
    { key: "backoffice-fuentes-obligatorias", label: "Fuentes obligatorias", href: "/backoffice/fuentes-obligatorias", section: "backoffice", icon: iconoComo("lista"), ilustracion: rutaKit("modulo/fuentes-oficiales") },
    ...NAV.items,
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

  const valor = useMemo(() => ({ ...estado, recargar, tramitesUrl }), [estado, recargar, tramitesUrl]);
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
            className="rounded-full border border-current px-3 py-1 text-xs font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
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
      <SuiteShell productCode="diagnostico" productName="Diagnóstico" nav={estado.sesion?.capacidades.backoffice ? NAV_BACKOFFICE : NAV} user={usuario} accountUrl={tramitesUrl} layout="app">
        <div className="flex min-h-full flex-col gap-4 px-4 pt-6 text-flit-primary md:px-6">{contenido}</div>
      </SuiteShell>
    </SesionContext.Provider>
  );
}

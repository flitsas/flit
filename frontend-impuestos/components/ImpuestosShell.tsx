"use client";

import { createContext, useCallback, useContext, useEffect, useEffectEvent, useMemo, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { rutaKit } from "@flit/ui/iconos-mapas";
import { SuiteShell, type NavCatalog, type ShellUser } from "@flit/shell/SuiteShell";
import { BannerEstadoProducto } from "@/components/BannerEstadoProducto";
import { CampanaAvisos } from "@/components/CampanaAvisos";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { ApiError, RUTAS, apiFetch } from "@/lib/api";
import { mensajeErrorFijo } from "@/lib/errores";
import { PERMISOS, bannersDeSesion, type SesionImpuestos } from "@/lib/sesion";

export interface UsuarioShell extends ShellUser {
  permissions: string[];
  roles: string[];
  isSuperAdmin: boolean;
}

export interface EstadoSesion {
  sesion: SesionImpuestos | null;
  cargando: boolean;
  error: string | null;
  recargar: () => void;
}

const SesionContext = createContext<EstadoSesion>({ sesion: null, cargando: true, error: null, recargar: () => {} });

/** Sesión de Impuestos (GET /api/v1/impuestos/sesion) para cualquier pantalla dentro del shell. */
export function useSesion(): EstadoSesion {
  return useContext(SesionContext);
}

/**
 * Módulos del producto (F6-CF3 / AC-FE-09, plan §5.2 y §6.1). El Dashboard es el botón de inicio (/); cada módulo es una
 * sección de una sola entrada, así el dock la pinta como enlace directo. Cada entrada nombra el permiso que la muestra:
 * Gestor → Impuestos y Reportes; Administrador → además Usuarios y Configuración; el Super Admin ve todo (canSee de
 * @flit/shell), incluido el Backoffice. La API vuelve a exigir cada permiso en cada ruta.
 */
export const NAV: NavCatalog = {
  sections: [
    { id: "liquidaciones", label: "Impuestos", icon: iconoComo("pesos"), side: "left", ilustracion: rutaKit("modulo/pagos") },
    { id: "reportes", label: "Reportes", icon: iconoComo("reportes"), side: "left", ilustracion: rutaKit("modulo/reportes") },
    { id: "backoffice", label: "Backoffice", icon: iconoComo("escudo"), side: "left", ilustracion: rutaKit("modulo/empresas") },
    { id: "usuarios", label: "Usuarios", icon: iconoComo("usuarios"), side: "right", ilustracion: rutaKit("modulo/colaboradores") },
    { id: "configuracion", label: "Configuración", icon: iconoComo("ajustes"), side: "right", ilustracion: rutaKit("modulo/configuracion") },
  ],
  items: [
    {
      key: "liquidaciones",
      label: "Impuestos",
      href: "/liquidaciones",
      section: "liquidaciones",
      icon: iconoComo("pesos"),
      ilustracion: rutaKit("modulo/pagos"),
      permission: PERMISOS.liquidacionesVer,
    },
    { key: "reportes", label: "Reportes", href: "/reportes", section: "reportes", icon: iconoComo("reportes"), ilustracion: rutaKit("modulo/reportes"), permission: PERMISOS.reportesVer },
    {
      key: "backoffice",
      label: "Backoffice",
      href: "/backoffice/empresas",
      section: "backoffice",
      icon: iconoComo("escudo"),
      ilustracion: rutaKit("modulo/empresas"),
      permission: PERMISOS.backofficeVer,
    },
    {
      key: "usuarios",
      label: "Usuarios",
      href: "/usuarios",
      section: "usuarios",
      icon: iconoComo("usuarios"),
      ilustracion: rutaKit("modulo/colaboradores"),
      permission: PERMISOS.usuariosGestionar,
    },
    {
      key: "configuracion",
      label: "Configuración",
      href: "/configuracion",
      section: "configuracion",
      icon: iconoComo("ajustes"),
      ilustracion: rutaKit("modulo/configuracion"),
      permission: PERMISOS.configuracionVer,
    },
  ],
};

/** Código de la 403 cuando la API niega el acceso sin decir por qué (usuario sin ningún permiso impuestos.*). */
const SIN_PERMISOS = "SIN_PERMISOS_IMPUESTOS";

/**
 * Barra y dock de la suite para Impuestos. Al abrir pide la sesión del producto: mientras llega muestra el loader de
 * marca y ningún módulo; si la API la niega (403: sin rol, compañía sin el producto o suspendida) lleva a la 403 con el
 * motivo, que está fuera del middleware y no vuelve al login; ante cualquier otra falla, el error fijo con Reintentar.
 * Los módulos salen de los permisos de la sesión (no del token). Con la sesión, la campana entra por `headerActions` y,
 * solo para el Super Admin, el selector de compañía; los banners de estado van sobre la pantalla.
 */
export function ImpuestosShell({ user, tramitesUrl, children }: { user: UsuarioShell; tramitesUrl: string; children: ReactNode }) {
  const router = useRouter();
  const [intento, setIntento] = useState(0);
  const [estado, setEstado] = useState<Omit<EstadoSesion, "recargar">>({ sesion: null, cargando: true, error: null });

  const irA403 = useEffectEvent((code: string) => router.replace(`/403?code=${encodeURIComponent(code)}`));

  useEffect(() => {
    const controller = new AbortController();
    apiFetch<SesionImpuestos>(RUTAS.sesion, { signal: controller.signal })
      .then((sesion) => setEstado({ sesion, cargando: false, error: null }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        if (e instanceof ApiError && e.status === 403) {
          // Se queda en «cargando» mientras navega: la pantalla base nunca se muestra.
          irA403(e.code || SIN_PERMISOS);
          return;
        }
        // Texto fijo; nunca el mensaje del servidor ni el de una excepción.
        setEstado({ sesion: null, cargando: false, error: mensajeErrorFijo(e, "No se pudo abrir Impuestos.") });
      });
    return () => controller.abort();
  }, [intento]);

  const recargar = useCallback(() => {
    setEstado({ sesion: null, cargando: true, error: null });
    setIntento((i) => i + 1);
  }, []);

  const sesion = estado.sesion;
  const valor = useMemo(() => ({ ...estado, recargar }), [estado, recargar]);
  // Sin sesión, ningún permiso: el dock no muestra módulos mientras carga ni ante un 403 (AC-FE-08).
  const usuario = useMemo(
    () => ({
      ...user,
      tenantName: sesion?.empresa.nombre || user.tenantName,
      permissions: (sesion?.permisos ?? []).map((p) => p.toLowerCase()),
      roles: sesion?.roles ?? [],
      isSuperAdmin: sesion?.esSuperAdmin === true,
    }),
    [user, sesion],
  );
  const banners = useMemo(() => bannersDeSesion(sesion), [sesion]);

  let contenido: ReactNode;
  if (estado.cargando) contenido = <VehicleLoader label="Abriendo Impuestos…" />;
  else if (estado.error)
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo abrir Impuestos"
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
  else
    contenido = (
      <>
        <BannerEstadoProducto banners={banners} />
        {children}
      </>
    );

  return (
    <SesionContext.Provider value={valor}>
      <SuiteShell
        productCode="impuestos"
        productName="Impuestos"
        nav={NAV}
        user={usuario}
        accountUrl={tramitesUrl}
        layout="app"
        headerActions={
          sesion ? (
            <>
              {sesion.esSuperAdmin && <SelectorEmpresa />}
              <CampanaAvisos />
            </>
          ) : undefined
        }
      >
        <div className="flex min-h-full flex-col gap-4 px-4 pt-6 text-flit-primary md:px-6">{contenido}</div>
      </SuiteShell>
    </SesionContext.Provider>
  );
}

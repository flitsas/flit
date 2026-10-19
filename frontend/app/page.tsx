"use client";

import { Suspense, useEffect, useMemo, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Shell, type ModuleId } from "@/components/atom/Shell";
import { Dashboard } from "@/components/atom/modules/Dashboard";
import { OtDashboard } from "@/components/atom/modules/OtDashboard";
import { Tramites } from "@/components/atom/modules/Tramites";
import { Reportes } from "@/components/atom/modules/Reportes";
import { Validaciones } from "@/components/atom/modules/Validaciones";
import { HistorialPlaca } from "@/components/atom/modules/HistorialPlaca";
import { Usuarios } from "@/components/atom/modules/Usuarios";
import { Ayuda } from "@/components/atom/modules/Ayuda";
import { RbacAdmin } from "@/components/atom/modules/RbacAdmin";
import { Auditoria } from "@/components/atom/modules/Auditoria";
import { LogQx } from "@/components/atom/modules/LogQx";
import { IctLogs } from "@/components/atom/modules/IctLogs";
import { IctReports } from "@/components/atom/modules/IctReports";
import { IctTrazabilidad } from "@/components/atom/modules/IctTrazabilidad";
import { ToastProvider } from "@/components/admin/Toast";
import { AccesoDenegadoAlert } from "@/components/shared/AccesoDenegadoAlert";
import { ACCESS_DENIED_PARAM, ACCESS_DENIED_VALUE } from "@/lib/auth/guard";
import { useAccessibleModules } from "@/hooks/useAccessibleModules";
import { useAuthGate } from "@/hooks/useAuthGate";
import {
  legacyModuleRedirect,
  planSpaModuleAccess,
  puedeDecidirAccesoAModulo,
  resolveNavigableModuleIds,
} from "@/lib/nav/modules";
import { trackModuleView } from "@/lib/telemetry"; // Reportes2 HU-A
import { getToken } from "@/lib/api/client";
import {
  canReadIctLogs,
  canReadIctReportes,
  canReadIctTrazabilidad,
  canReadLogQx,
  decodeJwtPayload,
  type JwtPayload,
  isOtUser,
  isSuperAdmin,
} from "@/lib/auth/jwt";

function HomeContent() {
  const router = useRouter();
  const params = useSearchParams();
  const { authed, hydrated, logout } = useAuthGate();
  const {
    modules: accessibleModules,
    loading: modulesLoading,
    ready: modulesReady,
    error: modulesError,
  } = useAccessibleModules(authed);
  // Claims JWT: lectura perezosa (no reactiva) — el token no cambia en la sesión SPA.
  const [claims] = useState<JwtPayload | null>(() => decodeJwtPayload(getToken()));
  const isSuperAdminUser = isSuperAdmin(claims);
  const isOtAdminUser = isOtUser(claims);
  const canLogQx = canReadLogQx(claims);
  // ICT (Bug #13445): Log solo SuperAdmin; Trazabilidad y Reportes por su permiso propio.
  const canIctLogs = canReadIctLogs(claims);
  const canIctTrazabilidad = canReadIctTrazabilidad(claims);
  const canIctReportes = canReadIctReportes(claims);

  const accessibleCodes = useMemo(
    () => accessibleModules.map((m) => m.code) as ModuleId[],
    [accessibleModules],
  );

  // Fuente única dock ≡ URL (HU #11507 / #11508): la decide la tabla de acceso (ADR-0072, HU #13649).
  // Hasta que el catálogo de módulos resuelve no se abre nada protegido (deny-by-default).
  const navigableIds = useMemo(
    () => resolveNavigableModuleIds({ accessibleCodes: modulesReady ? accessibleCodes : null, claims }),
    [accessibleCodes, claims, modulesReady],
  );

  // Clave estable: evita re-ejecutar el effect por nueva referencia de array.
  const navigableKey = useMemo(() => navigableIds.join("\0"), [navigableIds]);
  // Depender del string `m`, no del objeto params (referencia inestable en App Router).
  const rawModule = params.get("m");

  // Hold: null hasta !modulesLoading — evita flash del módulo denegado.
  const [module, setModule] = useState<ModuleId | null>(null);
  // HU #13650 — aviso «No tienes acceso a este módulo.»: lo dispara un `?m=` sin permiso o la marca
  // `?acceso=denegado` con la que el middleware devuelve al inicio al que abrió /admin/* o /empresa/* sin permiso.
  const [accesoDenegado, setAccesoDenegado] = useState(false);
  const accessMark = params.get(ACCESS_DENIED_PARAM);
  const lastDeniedRef = useRef<string | null>(null);
  const replaceIssuedRef = useRef<string | null>(null);

  // La marca del middleware se convierte en el aviso y se limpia de la URL (un solo replace; sin loop).
  useEffect(() => {
    if (!hydrated || !authed || accessMark !== ACCESS_DENIED_VALUE) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setAccesoDenegado(true);
    router.replace("/", { scroll: false });
  }, [accessMark, hydrated, authed, router]);

  // Sync / revalidar ?m= cuando cambian rawModule O navigableIds (post-RBAC).
  // Importante: un solo replace por módulo denegado — sin loop de router.replace.
  useEffect(() => {
    // Cuándo se puede decidir vive en `puedeDecidirAccesoAModulo`, con el porqué documentado allí:
    // esperar solo a `modulesLoading` evaluaba el permiso contra una lista vacía.
    if (!puedeDecidirAccesoAModulo({ hydrated, authed, modulesReady })) return;

    // HU #13582 — un `?m=` retirado (p. ej. Reportes Detallados, hoy pestaña de Reportes) se lleva a su
    // dirección nueva antes de evaluar permisos; ahí decide el gate normal. Va después de la espera de
    // sesión: antes, sin sesión, este replace le ganaba al envío al login y la página quedaba en blanco.
    const legacy = legacyModuleRedirect(rawModule);
    if (legacy) {
      router.replace(legacy, { scroll: false });
      return;
    }

    const plan = planSpaModuleAccess(rawModule, navigableIds);

    if (plan.denied) {
      const deniedKey = rawModule ?? "";
      if (lastDeniedRef.current !== deniedKey) {
        lastDeniedRef.current = deniedKey;
        setAccesoDenegado(true);
      }
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setModule((prev) => (prev === "dashboard" ? prev : "dashboard"));
      if (
        plan.shouldReplaceUrl &&
        plan.replaceTo &&
        replaceIssuedRef.current !== deniedKey
      ) {
        replaceIssuedRef.current = deniedKey;
        router.replace(plan.replaceTo, { scroll: false });
      }
      return;
    }

    lastDeniedRef.current = null;
    replaceIssuedRef.current = null;
    // Navegar a otro módulo permitido retira el aviso (quedarse en Inicio lo conserva hasta cerrarlo).
    if (rawModule && rawModule !== "dashboard") setAccesoDenegado(false);
    setModule((prev) => (prev === plan.module ? prev : plan.module));
  }, [rawModule, navigableKey, navigableIds, modulesReady, hydrated, authed, router]);

  // Reportes2 HU-A — telemetría solo tras módulo autorizado (no durante hold).
  useEffect(() => {
    if (authed && module) trackModuleView(module);
  }, [module, authed]);

  // Track B — Trámites vive en /tramites (ruta propia): el CTA del dashboard navega allá. El dock ya no pasa por
  // aquí (B-13): sus entradas son enlaces a /tramites o /?m=…, y el efecto de arriba lee el módulo de la URL.
  function handleNav(m: ModuleId) {
    if (m === "tramites") {
      router.push("/tramites");
      return;
    }
    setModule(m);
    router.replace(`/?m=${m}`, { scroll: false });
  }

  if (!hydrated || !authed) return null;

  const moduleReady = !modulesLoading && module !== null;

  return (
    <Shell
      onLogout={logout}
      visibleModuleCodes={modulesLoading ? [] : accessibleCodes}
      search={params.toString()}
    >
      {modulesError ? (
        <div
          role="alert"
          className="mx-6 mt-4 rounded-xl border px-4 py-3 text-xs font-medium"
          style={{
            borderColor: "#FF4E00",
            background: "rgba(255, 78, 0, 0.08)",
            color: "#162744",
          }}
        >
          No se pudieron cargar los permisos de módulos. Acceso restringido hasta
          reintentar.
        </div>
      ) : null}

      {accesoDenegado ? <AccesoDenegadoAlert onClose={() => setAccesoDenegado(false)} /> : null}

      {/* Hold / loading RBAC: no montar módulo pedido (sin flash). */}
      {/* El inicio se bifurca por rol (HU #11940): el tablero del gestor mide producción de una
          empresa y consulta endpoints acotados al tenant de la empresa cliente, donde el organismo
          no tiene ni una fila. Cualquier usuario de un organismo (isOtUser) ve su propia cola. */}
      {moduleReady && module === "dashboard" && isOtAdminUser && <OtDashboard />}
      {moduleReady && module === "dashboard" && !isOtAdminUser && (
        <Dashboard onNewTramite={() => handleNav("tramites")} />
      )}
      {moduleReady && module === "tramites" && <Tramites />}
      {moduleReady && module === "reportes" && <Reportes />}
      {moduleReady && module === "validaciones" && <Validaciones />}
      {/* HU #12194 — el gate real es RBAC (`historial-placa` en accessibleCodes, ya aplicado por
          resolveNavigableModuleIds); `isSuperAdmin` aquí solo decide si se pinta la columna de
          compañía, porque solo él recibe filas de más de una. */}
      {/* HU #12196 — `placa` en la URL precarga la consulta: es como entra el atajo "Ver historial
          de la placa" del listado de trámites. Mismo patrón que `log-qx` con `instanceId`. */}
      {moduleReady && module === "historial-placa" && (
        <HistorialPlaca isSuperAdmin={isSuperAdminUser} initialPlaca={params.get("placa")} />
      )}
      {moduleReady && module === "usuarios" && <Usuarios />}
      {moduleReady && module === "ayuda" && <Ayuda />}
      {moduleReady && module === "rbac" && <RbacAdmin />}
      {moduleReady && module === "auditoria" && isSuperAdminUser && <Auditoria />}
      {moduleReady && module === "log-qx" && canLogQx && (
        <LogQx initialInstanceId={params.get("instanceId") ?? undefined} />
      )}
      {moduleReady && module === "ict-logs" && canIctLogs && <IctLogs />}
      {moduleReady && module === "ict-reportes" && canIctReportes && <IctReports />}
      {moduleReady && module === "ict-trazabilidad" && canIctTrazabilidad && <IctTrazabilidad />}
    </Shell>
  );
}

export default function HomePage() {
  return (
    <Suspense fallback={null}>
      <ToastProvider>
        <HomeContent />
      </ToastProvider>
    </Suspense>
  );
}

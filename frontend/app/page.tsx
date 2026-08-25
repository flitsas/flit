"use client";

import { Suspense, useEffect, useMemo, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Shell, type ModuleId } from "@/components/atom/Shell";
import { Dashboard } from "@/components/atom/modules/Dashboard";
import { Tramites } from "@/components/atom/modules/Tramites";
import { Reportes } from "@/components/atom/modules/Reportes";
import { ReportesDetallados } from "@/components/atom/modules/ReportesDetallados";
import { Validaciones } from "@/components/atom/modules/Validaciones";
import { Usuarios } from "@/components/atom/modules/Usuarios";
import { Ayuda } from "@/components/atom/modules/Ayuda";
import { RbacAdmin } from "@/components/atom/modules/RbacAdmin";
import { Auditoria } from "@/components/atom/modules/Auditoria";
import { LogQx } from "@/components/atom/modules/LogQx";
import { IctLogs } from "@/components/atom/modules/IctLogs";
import { IctReports } from "@/components/atom/modules/IctReports";
import { IctTrazabilidad } from "@/components/atom/modules/IctTrazabilidad";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { useAccessibleModules } from "@/hooks/useAccessibleModules";
import { useAuthGate } from "@/hooks/useAuthGate";
import { planSpaModuleAccess, resolveNavigableModuleIds } from "@/lib/nav/modules";
import { trackModuleView } from "@/lib/telemetry"; // Reportes2 HU-A
import { getToken } from "@/lib/api/client";
import {
  canReadIctLogs,
  canReadLogQx,
  decodeJwtPayload,
  isOtAdmin,
  isSuperAdmin,
} from "@/lib/auth/jwt";

function HomeContent() {
  const router = useRouter();
  const params = useSearchParams();
  const { show: showToast } = useToast();
  const { authed, hydrated, logout } = useAuthGate();
  const {
    modules: accessibleModules,
    loading: modulesLoading,
    error: modulesError,
  } = useAccessibleModules(authed);
  // Claims JWT: lectura perezosa (no reactiva) — el token no cambia en la sesión SPA.
  const [isSuperAdminUser] = useState<boolean>(() => isSuperAdmin(decodeJwtPayload(getToken())));
  const [isOtAdminUser] = useState<boolean>(() => isOtAdmin(decodeJwtPayload(getToken())));
  const [canLogQx] = useState<boolean>(() => canReadLogQx(decodeJwtPayload(getToken())));
  const [canIctLogs] = useState<boolean>(() => canReadIctLogs(decodeJwtPayload(getToken())));

  const accessibleCodes = useMemo(
    () => accessibleModules.map((m) => m.code) as ModuleId[],
    [accessibleModules],
  );

  // Fuente única dock ≡ URL (HU #11507 / #11508).
  const navigableIds = useMemo(
    () =>
      resolveNavigableModuleIds({
        accessibleCodes,
        isSuperAdmin: isSuperAdminUser,
        isOtAdmin: isOtAdminUser,
        canReadLogQx: canLogQx,
        canReadIctLogs: canIctLogs,
      }),
    [accessibleCodes, isSuperAdminUser, isOtAdminUser, canLogQx, canIctLogs],
  );

  // Clave estable: evita re-ejecutar el effect por nueva referencia de array.
  const navigableKey = useMemo(() => navigableIds.join("\0"), [navigableIds]);
  // Depender del string `m`, no del objeto params (referencia inestable en App Router).
  const rawModule = params.get("m");

  // Hold: null hasta !modulesLoading — evita flash del módulo denegado.
  const [module, setModule] = useState<ModuleId | null>(null);
  const lastDeniedRef = useRef<string | null>(null);
  const replaceIssuedRef = useRef<string | null>(null);

  // Sync / revalidar ?m= cuando cambian rawModule O navigableIds (post-RBAC).
  // Importante: un solo replace por módulo denegado — sin loop de router.replace.
  useEffect(() => {
    if (modulesLoading) return;

    const plan = planSpaModuleAccess(rawModule, navigableIds);

    if (plan.denied) {
      const deniedKey = rawModule ?? "";
      if (lastDeniedRef.current !== deniedKey) {
        lastDeniedRef.current = deniedKey;
        showToast("No tienes acceso a ese módulo.", "error");
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
    setModule((prev) => (prev === plan.module ? prev : plan.module));
  }, [rawModule, navigableKey, navigableIds, modulesLoading, router, showToast]);

  // Reportes2 HU-A — telemetría solo tras módulo autorizado (no durante hold).
  useEffect(() => {
    if (authed && module) trackModuleView(module);
  }, [module, authed]);

  // Track B — Trámites vive en /tramites (ruta propia): el dock y el CTA del
  // dashboard navegan allá. El resto de módulos viven en esta SPA y sincronizan
  // su estado con la URL (?m=) para soportar back/forward y deep-links.
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
      active={module ?? "dashboard"}
      onNav={handleNav}
      onLogout={logout}
      visibleModuleCodes={modulesLoading ? [] : accessibleCodes}
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

      {/* Hold / loading RBAC: no montar módulo pedido (sin flash). */}
      {moduleReady && module === "dashboard" && (
        <Dashboard onNewTramite={() => handleNav("tramites")} />
      )}
      {moduleReady && module === "tramites" && <Tramites />}
      {moduleReady && module === "reportes" && <Reportes />}
      {moduleReady && module === "reportes-detallados" && <ReportesDetallados />}
      {moduleReady && module === "validaciones" && <Validaciones />}
      {moduleReady && module === "usuarios" && <Usuarios />}
      {moduleReady && module === "ayuda" && <Ayuda />}
      {moduleReady && module === "rbac" && <RbacAdmin />}
      {moduleReady && module === "auditoria" && isSuperAdminUser && <Auditoria />}
      {moduleReady && module === "log-qx" && canLogQx && (
        <LogQx initialInstanceId={params.get("instanceId") ?? undefined} />
      )}
      {moduleReady && module === "ict-logs" && canIctLogs && <IctLogs />}
      {moduleReady && module === "ict-reportes" && canIctLogs && <IctReports />}
      {moduleReady && module === "ict-trazabilidad" && canIctLogs && <IctTrazabilidad />}
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

"use client";

import { Suspense, useEffect, useState } from "react";
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
import { useAccessibleModules } from "@/hooks/useAccessibleModules";
import { useAuthGate } from "@/hooks/useAuthGate";
import { buildValidModules, parseModule } from "@/lib/nav/modules";
import { trackModuleView } from "@/lib/telemetry"; // Reportes2 HU-A
import { getToken } from "@/lib/api/client";
import { canReadLogQx, decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";

function HomeContent() {
  const router = useRouter();
  const params = useSearchParams();
  const { authed, hydrated, logout } = useAuthGate();
  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  // "Auditoría" (HU #10680) es SuperAdmin-only y no tiene fila en el catálogo RBAC de
  // módulos (se gatea por el claim SuperAdmin del JWT, igual que su entrada en el dock —
  // Shell.tsx, bloque `currentUser?.isSuperAdmin`), no por `accessibleCodes`. Se re-lee de
  // forma perezosa (no reactiva) porque el JWT no cambia durante la sesión de la SPA.
  const [isSuperAdminUser] = useState<boolean>(() => isSuperAdmin(decodeJwtPayload(getToken())));
  // LOG QX (HU #10795) — gate por permiso `logqx.read` (o SuperAdmin). Se re-lee de forma
  // perezosa (no reactiva) igual que `isSuperAdminUser`: el JWT no cambia durante la sesión.
  const [canLogQx] = useState<boolean>(() => canReadLogQx(decodeJwtPayload(getToken())));

  const accessibleCodes = accessibleModules.map((m) => m.code) as ModuleId[];
  // "ayuda" es soporte universal (no es un módulo RBAC): siempre navegable, aunque no
  // venga en los permisos. Así el dock lo muestra y abre en todas las pantallas.
  const validModules = buildValidModules(accessibleCodes);

  const [module, setModule] = useState<ModuleId>(() => parseModule(params.get("m"), []));

  // Sync active module with URL so the browser back/forward and deep-links work.
  useEffect(() => {
    const fromUrl = parseModule(params.get("m"), validModules);
    if (fromUrl !== module) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setModule(fromUrl);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

  // Reportes2 HU-A — telemetría module_view: una vez por sesión y módulo al abrir
  // un módulo del dock (fire-and-forget; requiere sesión para enviarse).
  useEffect(() => {
    if (authed) trackModuleView(module);
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

  return (
    <Shell
      active={module}
      onNav={handleNav}
      onLogout={logout}
      visibleModuleCodes={modulesLoading ? [] : accessibleCodes}
    >
      {module === "dashboard"    && <Dashboard onNewTramite={() => handleNav("tramites")} />}
      {module === "tramites"     && <Tramites />}
      {module === "reportes"     && <Reportes />}
      {module === "reportes-detallados" && <ReportesDetallados />}
      {module === "validaciones" && <Validaciones />}
      {module === "usuarios"     && <Usuarios />}
      {module === "ayuda"        && <Ayuda />}
      {module === "rbac"         && <RbacAdmin />}
      {module === "auditoria"    && isSuperAdminUser && <Auditoria />}
      {module === "log-qx"       && canLogQx && <LogQx initialInstanceId={params.get("instanceId") ?? undefined} />}
    </Shell>
  );
}

export default function HomePage() {
  return (
    <Suspense fallback={null}>
      <HomeContent />
    </Suspense>
  );
}

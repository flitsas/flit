"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Login } from "@/components/atom/Login";
import { Shell, type ModuleId } from "@/components/atom/Shell";
import { Dashboard } from "@/components/atom/modules/Dashboard";
import { Tramites } from "@/components/atom/modules/Tramites";
import { Reportes } from "@/components/atom/modules/Reportes";
import { Validaciones } from "@/components/atom/modules/Validaciones";
import { Usuarios } from "@/components/atom/modules/Usuarios";
import { Ayuda } from "@/components/atom/modules/Ayuda";
import { RbacAdmin } from "@/components/atom/modules/RbacAdmin";
import { getToken } from "@/lib/api/client";
import { clearToken, getRememberedEmail } from "@/lib/auth/session";
import { useAccessibleModules } from "@/hooks/useAccessibleModules";
import { buildValidModules, parseModule } from "@/lib/nav/modules";

function HomeContent() {
  const router = useRouter();
  const params = useSearchParams();
  const [authed, setAuthed] = useState(false);
  const [hydrated, setHydrated] = useState(false);
  const { modules: accessibleModules } = useAccessibleModules(authed);

  const accessibleCodes = accessibleModules.map((m) => m.code) as ModuleId[];
  // "ayuda" es soporte universal (no es un módulo RBAC): siempre navegable, aunque no
  // venga en los permisos. Así el dock lo muestra y abre en todas las pantallas.
  const validModules = buildValidModules(accessibleCodes);

  const [module, setModule] = useState<ModuleId>(() => parseModule(params.get("m"), []));

  useEffect(() => {
    // Token solo disponible en cliente; se lee tras el montaje.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setAuthed(Boolean(getToken()));
    setHydrated(true);
  }, []);

  // Sync active module with URL so the browser back/forward and deep-links work.
  useEffect(() => {
    const fromUrl = parseModule(params.get("m"), validModules);
    if (fromUrl !== module) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setModule(fromUrl);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

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

  function handleLogout() {
    clearToken();
    setAuthed(false);
    handleNav("dashboard");
  }

  if (!hydrated) return null;

  if (!authed) {
    return (
      <Login
        onAuthenticated={() => {
          setAuthed(true);
          handleNav(module);
        }}
        defaultEmail={getRememberedEmail()}
      />
    );
  }

  return (
    <Shell
      active={module}
      onNav={handleNav}
      onLogout={handleLogout}
      visibleModuleCodes={accessibleCodes.length > 0 ? accessibleCodes : undefined}
    >
      {module === "dashboard"    && <Dashboard onNewTramite={() => handleNav("tramites")} />}
      {module === "tramites"     && <Tramites />}
      {module === "reportes"     && <Reportes />}
      {module === "validaciones" && <Validaciones />}
      {module === "usuarios"     && <Usuarios />}
      {module === "ayuda"        && <Ayuda />}
      {module === "rbac"         && <RbacAdmin />}
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

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
import { getToken } from "@/lib/api/client";
import { clearToken, getRememberedEmail } from "@/lib/auth/session";

const VALID_MODULES: ModuleId[] = ["dashboard", "tramites", "reportes", "validaciones", "usuarios", "ayuda"];

function parseModule(raw: string | null): ModuleId {
  return VALID_MODULES.includes(raw as ModuleId) ? (raw as ModuleId) : "dashboard";
}

function HomeContent() {
  const router = useRouter();
  const params = useSearchParams();
  const [authed, setAuthed] = useState(false);
  const [module, setModule] = useState<ModuleId>(() => parseModule(params.get("m")));
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    setAuthed(Boolean(getToken()));
    setHydrated(true);
  }, []);

  // Sync active module with URL so the browser back/forward and deep-links work.
  useEffect(() => {
    const fromUrl = parseModule(params.get("m"));
    if (fromUrl !== module) setModule(fromUrl);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

  function handleNav(m: ModuleId) {
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
    <Shell active={module} onNav={handleNav} onLogout={handleLogout}>
      {module === "dashboard"    && <Dashboard onNewTramite={() => handleNav("tramites")} />}
      {module === "tramites"     && <Tramites />}
      {module === "reportes"     && <Reportes />}
      {module === "validaciones" && <Validaciones />}
      {module === "usuarios"     && <Usuarios />}
      {module === "ayuda"        && <Ayuda />}
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

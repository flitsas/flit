"use client";

import { useEffect, useState } from "react";
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

export default function HomePage() {
  const [authed, setAuthed] = useState(false);
  const [module, setModule] = useState<ModuleId>("dashboard");
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    // La autenticación se deriva del JWT real (cookie/localStorage), no de un flag
    // mock. El token solo está disponible en cliente; se lee tras el montaje.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setAuthed(Boolean(getToken()));
    setHydrated(true);
  }, []);

  function handleLogout() {
    clearToken();
    setAuthed(false);
    setModule("dashboard");
  }

  if (!hydrated) {
    return null;
  }

  if (!authed) {
    return <Login onAuthenticated={() => setAuthed(true)} defaultEmail={getRememberedEmail()} />;
  }

  return (
    <Shell active={module} onNav={setModule} onLogout={handleLogout}>
      {module === "dashboard" && <Dashboard onNewTramite={() => setModule("tramites")} />}
      {module === "tramites" && <Tramites />}
      {module === "reportes" && <Reportes />}
      {module === "validaciones" && <Validaciones />}
      {module === "usuarios" && <Usuarios />}
      {module === "ayuda" && <Ayuda />}
    </Shell>
  );
}

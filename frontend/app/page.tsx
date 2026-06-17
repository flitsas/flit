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

export default function HomePage() {
  const [authed, setAuthed] = useState(false);
  const [module, setModule] = useState<ModuleId>("dashboard");
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    setAuthed(window.localStorage.getItem("flit:authed") === "1");
    setHydrated(true);
  }, []);

  useEffect(() => {
    if (!hydrated) return;
    if (authed) window.localStorage.setItem("flit:authed", "1");
    else window.localStorage.removeItem("flit:authed");
  }, [authed, hydrated]);

  if (!hydrated) {
    return null;
  }

  if (!authed) {
    return <Login onBypass={() => setAuthed(true)} />;
  }

  return (
    <Shell active={module} onNav={setModule} onLogout={() => setAuthed(false)}>
      {module === "dashboard" && <Dashboard onNewTramite={() => setModule("tramites")} />}
      {module === "tramites" && <Tramites />}
      {module === "reportes" && <Reportes />}
      {module === "validaciones" && <Validaciones />}
      {module === "usuarios" && <Usuarios />}
      {module === "ayuda" && <Ayuda />}
    </Shell>
  );
}

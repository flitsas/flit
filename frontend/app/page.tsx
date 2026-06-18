"use client";

import { useState, useSyncExternalStore } from "react";
import { Login } from "@/components/atom/Login";
import { Shell, type ModuleId } from "@/components/atom/Shell";
import { Dashboard } from "@/components/atom/modules/Dashboard";
import { Tramites } from "@/components/atom/modules/Tramites";
import { Reportes } from "@/components/atom/modules/Reportes";
import { Validaciones } from "@/components/atom/modules/Validaciones";
import { Usuarios } from "@/components/atom/modules/Usuarios";
import { Ayuda } from "@/components/atom/modules/Ayuda";

const AUTH_KEY = "flit:authed";

// Store externo (localStorage) leído con useSyncExternalStore: sin setState síncrono en efectos
// y sin desajuste de hidratación (el snapshot de servidor es siempre "no autenticado").
const authStore = {
  subscribe(callback: () => void) {
    window.addEventListener("storage", callback);
    window.addEventListener(AUTH_KEY, callback);
    return () => {
      window.removeEventListener("storage", callback);
      window.removeEventListener(AUTH_KEY, callback);
    };
  },
  getSnapshot() {
    return window.localStorage.getItem(AUTH_KEY) === "1";
  },
  getServerSnapshot() {
    return false;
  },
  set(value: boolean) {
    if (value) window.localStorage.setItem(AUTH_KEY, "1");
    else window.localStorage.removeItem(AUTH_KEY);
    window.dispatchEvent(new Event(AUTH_KEY));
  },
};

export default function HomePage() {
  const authed = useSyncExternalStore(
    authStore.subscribe,
    authStore.getSnapshot,
    authStore.getServerSnapshot,
  );
  const [module, setModule] = useState<ModuleId>("dashboard");

  if (!authed) {
    return <Login onBypass={() => authStore.set(true)} />;
  }

  return (
    <Shell active={module} onNav={setModule} onLogout={() => authStore.set(false)}>
      {module === "dashboard" && <Dashboard onNewTramite={() => setModule("tramites")} />}
      {module === "tramites" && <Tramites />}
      {module === "reportes" && <Reportes />}
      {module === "validaciones" && <Validaciones />}
      {module === "usuarios" && <Usuarios />}
      {module === "ayuda" && <Ayuda />}
    </Shell>
  );
}

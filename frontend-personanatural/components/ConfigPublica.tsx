"use client";

import { createContext, useContext } from "react";
import { resolverProveedor, type ConfigCaptcha } from "@/lib/captcha";

// Configuración pública que el servidor resuelve por petición (layout) y entrega al navegador en tiempo de ejecución.
// Hoy solo el captcha. Sin proveedor de contexto (pruebas), se aplica la regla por defecto según NODE_ENV.

const porDefecto = (): ConfigCaptcha => ({ proveedor: resolverProveedor(undefined, process.env.NODE_ENV) });

const Contexto = createContext<ConfigCaptcha | null>(null);

export function ConfigPublica({ captcha, children }: { captcha: ConfigCaptcha; children: React.ReactNode }) {
  return <Contexto.Provider value={captcha}>{children}</Contexto.Provider>;
}

export function useConfigCaptcha(): ConfigCaptcha {
  return useContext(Contexto) ?? porDefecto();
}

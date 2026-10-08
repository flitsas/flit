// Configuración del hub leída en el SERVIDOR al atender cada petición (B-09). La misma imagen corre en DEV, QA
// y PDN: nada de esto se hornea en el build ni lleva prefijo NEXT_PUBLIC_, así que cambiar un valor solo
// exige recrear el contenedor.
//
// Uso de ejemplo:
//   const { apiOrigin } = hubConfig();
import "server-only";
import { tramitesUrlFor } from "./tramites-url";

export interface HubConfig {
  /** Gateway que atiende /api/v1/* (red interna de Docker en los servidores). */
  apiOrigin: string;
  /** Clave compartida con el gateway para que acepte el sello X-Flit-Domain del hub. Vacía: no se envía. */
  internalApiKey: string | undefined;
  /**
   * URL de Trámites en este ambiente: la administración de plataforma sigue allí hasta B-12. Con el host de la petición,
   * la de su raíz (TRAMITES_URLS: app.flitsas.com → tramites.flitsas.com).
   */
  tramitesUrl: string;
  /** Adónde lleva «Iniciar sesión»: la sesión del hub como cliente plataforma (A-09), que pasa por su login (A-06). */
  loginUrl: string;
}

export function hubConfig(env: NodeJS.ProcessEnv = process.env, host?: string | null): HubConfig {
  return {
    apiOrigin: trimSlash(env.CORE_API_ORIGIN || "http://localhost:4002"),
    internalApiKey: env.FLIT_INTERNAL_API_KEY || undefined,
    loginUrl: env.HUB_LOGIN_URL || "/auth/login",
    tramitesUrl: tramitesUrlFor(host, env),
  };
}

function trimSlash(url: string): string {
  return url.replace(/\/+$/, "");
}

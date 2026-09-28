// Configuración del hub leída en el SERVIDOR al atender cada petición (B-09). La misma imagen corre en DEV, QA
// y PDN: nada de esto se hornea en el build ni lleva prefijo NEXT_PUBLIC_, así que cambiar un valor solo
// exige recrear el contenedor.
//
// Uso de ejemplo:
//   const { apiOrigin } = hubConfig();
import "server-only";

export interface HubConfig {
  /** Gateway que atiende /api/v1/* (red interna de Docker en los servidores). */
  apiOrigin: string;
  /** Clave compartida con el gateway para que acepte el sello X-Flit-Domain del hub. Vacía: no se envía. */
  internalApiKey: string | undefined;
  /** Adónde lleva «Iniciar sesión» mientras el login del hub (A-06) no exista: el de Trámites. */
  loginUrl: string;
}

export function hubConfig(env: NodeJS.ProcessEnv = process.env): HubConfig {
  const tramitesUrl = trimSlash(env.TRAMITES_URL || "http://localhost:3000");
  return {
    apiOrigin: trimSlash(env.CORE_API_ORIGIN || "http://localhost:4002"),
    internalApiKey: env.FLIT_INTERNAL_API_KEY || undefined,
    loginUrl: env.HUB_LOGIN_URL || `${tramitesUrl}/login`,
  };
}

function trimSlash(url: string): string {
  return url.replace(/\/+$/, "");
}

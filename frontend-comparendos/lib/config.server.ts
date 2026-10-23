// Configuración leída en el SERVIDOR al atender cada petición: la misma imagen corre en DEV, QA y PDN, así que nada
// de esto se hornea en el build ni lleva prefijo NEXT_PUBLIC_.
import "server-only";

export interface ComparendosConfig {
  /** Gateway que atiende /api/v1/* (red interna en los servidores). Lo usan las páginas públicas sin sesión. */
  apiOrigin: string;
  /** Clave compartida con el gateway para el sello del dominio. Vacía: no se envía. */
  internalApiKey: string | undefined;
  /** Hub de la suite: «Ir a mis productos» y el menú de productos. */
  hubUrl: string;
  /** Trámites: Ayuda y Cambiar contraseña de la barra viven allí. */
  tramitesUrl: string;
}

export function comparendosConfig(env: NodeJS.ProcessEnv = process.env): ComparendosConfig {
  return {
    apiOrigin: trimSlash(env.CORE_API_ORIGIN || "http://localhost:4002"),
    internalApiKey: env.FLIT_INTERNAL_API_KEY || undefined,
    hubUrl: trimSlash(env.FLIT_HUB_URL || "http://127.0.0.1:4022"),
    tramitesUrl: trimSlash(env.TRAMITES_URL || "http://127.0.0.1:3000"),
  };
}

function trimSlash(url: string): string {
  return url.replace(/\/+$/, "");
}

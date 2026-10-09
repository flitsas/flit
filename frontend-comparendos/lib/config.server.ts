// Configuración leída en el SERVIDOR al atender cada petición: la misma imagen corre en DEV, QA y PDN, así que nada
// de esto se hornea en el build ni lleva prefijo NEXT_PUBLIC_.
import "server-only";

export interface ComparendosConfig {
  /** Hub de la suite: «Ir a mis productos» y el menú de productos. */
  hubUrl: string;
  /** Trámites: Ayuda y Cambiar contraseña de la barra viven allí. */
  tramitesUrl: string;
}

export function comparendosConfig(env: NodeJS.ProcessEnv = process.env): ComparendosConfig {
  return {
    hubUrl: trimSlash(env.FLIT_HUB_URL || "http://127.0.0.1:4022"),
    tramitesUrl: trimSlash(env.TRAMITES_URL || "http://127.0.0.1:3000"),
  };
}

function trimSlash(url: string): string {
  return url.replace(/\/+$/, "");
}

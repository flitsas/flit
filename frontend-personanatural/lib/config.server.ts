// Configuración leída en el SERVIDOR al atender cada petición: la misma imagen corre en DEV, QA y PDN, así que nada
// de esto se hornea en el build ni lleva prefijo NEXT_PUBLIC_. El front no conoce ninguna URL de FLIT 2: solo la del
// gateway, que enruta /api/v1/persona-natural/* al BFF del portal.
import "server-only";
import { resolverProveedor, type ConfigCaptcha } from "@/lib/captcha";
import { parsearSaltosConfianza } from "@/lib/server/ip";

export interface PortalConfig {
  /** Gateway que atiende /api/v1/persona-natural/* (red interna en los servidores). */
  apiOrigin: string;
  /** Las cookies llevan `Secure` salvo en desarrollo local sin HTTPS. */
  cookieSegura: boolean;
  /** Proxies propios delante del portal (nginx = 1); define qué salto de X-Forwarded-For es la IP real. */
  saltosProxyConfianza: number;
  /** Captcha de los endpoints públicos: proveedor (`CAPTCHA_PROVIDER`) y clave pública del sitio (`CAPTCHA_SITE_KEY`). */
  captcha: ConfigCaptcha;
}

export function portalConfig(env: NodeJS.ProcessEnv = process.env): PortalConfig {
  return {
    apiOrigin: trimSlash(env.CORE_API_ORIGIN || "http://localhost:4002"),
    cookieSegura: env.NODE_ENV === "production",
    saltosProxyConfianza: parsearSaltosConfianza(env.TRUSTED_PROXY_HOPS),
    captcha: { proveedor: resolverProveedor(env.CAPTCHA_PROVIDER, env.NODE_ENV), siteKey: env.CAPTCHA_SITE_KEY || undefined },
  };
}

function trimSlash(url: string): string {
  return url.replace(/\/+$/, "");
}

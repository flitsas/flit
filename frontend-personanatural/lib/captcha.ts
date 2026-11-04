// Proveedor del captcha y clave pública del sitio. Se resuelven en el SERVIDOR (lib/config.server.ts: CAPTCHA_PROVIDER y
// CAPTCHA_SITE_KEY) y llegan al navegador en tiempo de ejecución por el layout, no incrustados al compilar: la misma
// imagen sirve en todos los ambientes. La clave pública del sitio no es un secreto.

export type ProveedorCaptcha = "turnstile" | "recaptcha" | "dev" | "ninguno";

export interface ConfigCaptcha {
  proveedor: ProveedorCaptcha;
  siteKey?: string;
}

/** Sin proveedor válido: `dev` fuera de producción (casilla local) y `ninguno` en producción (no se entrega token). */
export function resolverProveedor(valor: string | undefined, entorno: string | undefined): ProveedorCaptcha {
  if (valor === "turnstile" || valor === "recaptcha" || valor === "dev") return valor;
  return entorno === "production" ? "ninguno" : "dev";
}

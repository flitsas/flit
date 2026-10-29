// Único punto de los documentos legales del portal. El documento por defecto es el mismo que usa FLIT 2
// (`ProcedureTermsOptions.DefaultUrl` / `FLIT_PRIVACY_POLICY_URL`). La configuración publicada puede sustituir la URL.

export interface DocumentoLegal {
  clave: string;
  titulo: string;
  /** URL oficial del documento. `null` mientras no exista. */
  url: string | null;
  disponible: boolean;
}

/** Página pública de la política de privacidad de FLIT, la misma que abre el wizard de FLIT 2. */
export const URL_POLITICA_FLIT = "https://flitsas.com.co/privacy-policy/";

export const DOCUMENTOS_LEGALES = {
  politicas: {
    clave: "politicas",
    titulo: "Políticas de Privacidad y Términos de Uso",
    url: URL_POLITICA_FLIT,
    disponible: true,
  },
} as const satisfies Record<string, DocumentoLegal>;

/** Título del enlace legal del pie; sale del único registro de documentos. */
export const TEXTO_POLITICAS = DOCUMENTOS_LEGALES.politicas.titulo;

/** Mensaje del diálogo mientras un documento no tiene texto oficial. */
export const TEXTO_DOCUMENTO_NO_DISPONIBLE = "El documento oficial aún no está disponible.";

// ── Enlaces publicados (HU #14073) ───────────────────────────────────────────────────────────────────────────────
// Este módulo sigue siendo la única fuente de los documentos legales: la configuración publicada solo aporta la URL.

export const TEXTO_POLITICA_DATOS = "Política de tratamiento de datos personales";
export const TEXTO_TERMINOS = "Términos de uso";

/**
 * Devuelve la URL solo si es http(s) bien formada y sin credenciales; en cualquier otro caso `null`. Rechaza `javascript:`,
 * `data:`, `ftp:`, rutas relativas y texto con espacios o caracteres de control.
 */
export function urlPublicable(valor: unknown): string | null {
  if (typeof valor !== "string") return null;
  const limpio = valor.trim();
  if (limpio === "" || limpio.length > 2048 || /[\s\u0000-\u001f\u007f]/.test(limpio)) return null;
  try {
    const url = new URL(limpio);
    if (url.protocol !== "http:" && url.protocol !== "https:") return null;
    if (url.username !== "" || url.password !== "" || url.hostname === "") return null;
    return url.href;
  } catch {
    return null;
  }
}

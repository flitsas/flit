// Validación en cliente del logo y del banner del portal del OT (Épica #14297, HU #14377). Mismas reglas que el
// contrato: PNG, JPEG o WebP; logo hasta 512 KB y banner hasta 2 MB. El servidor revisa además el contenido real
// (`PORTAL_MARCA_ARCHIVO_INVALIDO`); aquí solo se evita subir lo que de entrada no sirve, con el mismo tono del servidor.

export type PortalImageKind = "logo" | "banner";

export const PORTAL_IMAGE_TYPES = ["image/png", "image/jpeg", "image/webp"] as const;
export const PORTAL_IMAGE_ACCEPT = PORTAL_IMAGE_TYPES.join(",");
export const PORTAL_LOGO_MAX_BYTES = 512 * 1024;
export const PORTAL_BANNER_MAX_BYTES = 2 * 1024 * 1024;

const LABEL: Record<PortalImageKind, string> = { logo: "El logo", banner: "El banner" };
const MAX_LABEL: Record<PortalImageKind, string> = { logo: "512 KB", banner: "2 MB" };

/** Motivo exacto si el archivo no sirve; `null` si puede subirse. */
export function validatePortalImage(file: File, kind: PortalImageKind): string | null {
  if (!PORTAL_IMAGE_TYPES.includes(file.type as (typeof PORTAL_IMAGE_TYPES)[number])) {
    return `${LABEL[kind]} debe ser PNG, JPEG o WebP.`;
  }
  const max = kind === "logo" ? PORTAL_LOGO_MAX_BYTES : PORTAL_BANNER_MAX_BYTES;
  if (file.size > max) {
    return `${LABEL[kind]} pesa más de ${MAX_LABEL[kind]}. Elige una imagen más liviana.`;
  }
  return null;
}

export function portalImageHint(kind: PortalImageKind): string {
  return `PNG, JPEG o WebP · máx. ${MAX_LABEL[kind]}`;
}

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

/** Imagen del carrusel: PNG, JPEG o WebP, hasta 512 KB. */
export function validatePortalCarouselImage(file: File): string | null {
  if (!PORTAL_IMAGE_TYPES.includes(file.type as (typeof PORTAL_IMAGE_TYPES)[number])) {
    return "La imagen debe ser PNG, JPEG o WebP.";
  }
  if (file.size > PORTAL_LOGO_MAX_BYTES) {
    return "La imagen pesa más de 512 KB. Elige una imagen más liviana.";
  }
  return null;
}

/** Proporción recomendada del banner (ancho / alto): 1600 × 366 px. */
export const PORTAL_BANNER_RATIO = 1600 / 366;

/**
 * Aviso (no bloquea) si el banner tiene una proporción muy distinta a la recomendada. Si no se puede medir la
 * imagen, no avisa.
 */
export function avisoProporcionBanner(file: File): Promise<string | null> {
  return new Promise((resolve) => {
    if (typeof Image === "undefined" || typeof URL.createObjectURL !== "function") return resolve(null);
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onload = () => {
      URL.revokeObjectURL(url);
      const ratio = img.naturalHeight ? img.naturalWidth / img.naturalHeight : 0;
      resolve(
        ratio && Math.abs(ratio - PORTAL_BANNER_RATIO) / PORTAL_BANNER_RATIO > 0.25
          ? "La proporción de esta imagen es distinta a la recomendada (1600 × 366 px): puede verse recortada o con bordes."
          : null,
      );
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      resolve(null);
    };
    img.src = url;
  });
}

/** Enlace opcional del carrusel: vacío, o una dirección http/https de hasta 500 caracteres. */
export function validatePortalCarouselLink(value: string): string | null {
  const link = value.trim();
  if (!link) return null;
  if (link.length > 500) return "El enlace es demasiado largo (máximo 500 caracteres).";
  try {
    const url = new URL(link);
    if (url.protocol === "http:" || url.protocol === "https:") return null;
  } catch {
    // cae al mensaje de abajo
  }
  return "El enlace debe empezar por http:// o https://.";
}

export function portalImageHint(kind: PortalImageKind): string {
  return `PNG, JPEG o WebP · máx. ${MAX_LABEL[kind]}`;
}

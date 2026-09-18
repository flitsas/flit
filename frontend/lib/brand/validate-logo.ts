// Validación en cliente del logotipo de marca — HU #12414 AC2.
// Mismas reglas que `UploadBrandLogoHandler` (#12413): formato PNG/JPEG/WebP, peso ≤ 512 KB,
// dimensiones 120x40..2000x2000. Los códigos coinciden con `BrandingErrors.cs` para que
// `brandingErrorMessage` muestre el MISMO texto que un rechazo del servidor (AC2).
//
// Uso de ejemplo:
//   const result = await validateLogoFile(file);
//   if (!result.ok) { showError(brandingErrorMessage(result.code)); }

export const ALLOWED_LOGO_MIME_TYPES = ["image/png", "image/jpeg", "image/webp"] as const;
export const MAX_LOGO_BYTES = 524_288; // 512 KB — Branding:Logo:MaxBytes (default backend)
export const MIN_LOGO_WIDTH = 120;
export const MIN_LOGO_HEIGHT = 40;
export const MAX_LOGO_WIDTH = 2000;
export const MAX_LOGO_HEIGHT = 2000;

export type LogoValidationCode =
  | "BRANDING_LOGO_FORMAT"
  | "BRANDING_LOGO_TOO_LARGE"
  | "BRANDING_LOGO_DIMENSIONS";

export type LogoValidationResult =
  | { ok: true; width: number; height: number }
  | { ok: false; code: LogoValidationCode };

/**
 * Lee dimensiones de una imagen en el navegador (sin decodificar el archivo, vía `Image`).
 * Se apoya en `URL.createObjectURL`; si el entorno no tiene `Image` (SSR), rechaza con dimensiones 0.
 */
function readImageDimensions(file: File): Promise<{ width: number; height: number }> {
  return new Promise((resolve, reject) => {
    if (typeof window === "undefined" || typeof window.Image === "undefined") {
      reject(new Error("Lectura de dimensiones no disponible en este entorno."));
      return;
    }
    const objectUrl = URL.createObjectURL(file);
    const img = new window.Image();
    img.onload = () => {
      URL.revokeObjectURL(objectUrl);
      resolve({ width: img.naturalWidth, height: img.naturalHeight });
    };
    img.onerror = () => {
      URL.revokeObjectURL(objectUrl);
      reject(new Error("No se pudo leer la imagen."));
    };
    img.src = objectUrl;
  });
}

/** Valida formato, peso y dimensiones de un logotipo antes de subirlo (AC2). */
export async function validateLogoFile(file: File): Promise<LogoValidationResult> {
  if (!ALLOWED_LOGO_MIME_TYPES.includes(file.type as (typeof ALLOWED_LOGO_MIME_TYPES)[number])) {
    return { ok: false, code: "BRANDING_LOGO_FORMAT" };
  }

  if (file.size > MAX_LOGO_BYTES) {
    return { ok: false, code: "BRANDING_LOGO_TOO_LARGE" };
  }

  let dimensions: { width: number; height: number };
  try {
    dimensions = await readImageDimensions(file);
  } catch {
    // No se pudieron leer las dimensiones (archivo corrupto o no es realmente una imagen):
    // se trata como formato inválido, mismo código que el servidor usaría al no poder decodificarla.
    return { ok: false, code: "BRANDING_LOGO_FORMAT" };
  }

  const { width, height } = dimensions;
  if (
    width < MIN_LOGO_WIDTH ||
    height < MIN_LOGO_HEIGHT ||
    width > MAX_LOGO_WIDTH ||
    height > MAX_LOGO_HEIGHT
  ) {
    return { ok: false, code: "BRANDING_LOGO_DIMENSIONS" };
  }

  return { ok: true, width, height };
}

/** Texto legible de la restricción de dimensiones, para el hint del input de archivo. */
export function logoDimensionsHint(): string {
  return `PNG, JPEG o WebP · máx. 512 KB · ${MIN_LOGO_WIDTH}x${MIN_LOGO_HEIGHT} a ${MAX_LOGO_WIDTH}x${MAX_LOGO_HEIGHT} px`;
}

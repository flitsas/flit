// Mapa código → texto de la identidad de marca — HU #12414 AC2.
// Mismos códigos que `Flit.Admin.Domain/Companies/Branding/BrandingErrors.cs` (fuente de verdad
// del backend). La validación en cliente (`validate-logo.ts`, `BrandingColorPicker`) y el rechazo
// del servidor (422/409 de `lib/api/branding.ts`) usan ESTE mismo texto por código — AC2: "un
// rechazo del servidor se muestra por su código de error, con el mismo texto que la validación
// en cliente".
//
// Uso de ejemplo:
//   brandingErrorMessage("BRANDING_LOGO_TOO_LARGE") // → "El logotipo pesa más de 512 KB..."
//   brandingErrorMessage("CODIGO_DESCONOCIDO") // → mensaje genérico de fallback

export const BRANDING_ERROR_MESSAGES: Record<string, string> = {
  BRANDING_NOT_FOUND: "Esta compañía aún no tiene identidad de marca configurada.",
  BRANDING_TENANT_NOT_MARCA_BLANCA: "Esta compañía no es una cabeza de red Marca Blanca.",
  CONCURRENCY_CONFLICT: "Alguien más modificó esta configuración. Vuelve a cargarla e inténtalo de nuevo.",
  BRANDING_INCOMPLETE: "Completa el logotipo y los colores antes de publicar.",
  BRANDING_NAME_LENGTH: "El nombre de la plataforma debe tener entre 2 y 40 caracteres.",
  BRANDING_NAME_MARKUP: "El nombre de la plataforma no puede contener etiquetas ni código.",
  BRANDING_COLOR_FORMAT: "El color debe tener el formato #RRGGBB.",
  BRANDING_CONTRAST_TOO_LOW: "El contraste entre el color de texto y el color principal es insuficiente para publicar (mínimo 4.5:1).",
  BRANDING_LOGO_NOT_FOUND: "El logotipo indicado no existe o no pertenece a esta compañía.",
  BRANDING_LOGO_FORMAT: "El logotipo debe ser PNG, JPEG o WebP.",
  BRANDING_LOGO_TOO_LARGE: "El logotipo pesa más de 512 KB. Reduce el tamaño del archivo.",
  BRANDING_LOGO_DIMENSIONS: "El logotipo debe medir entre 120x40 y 2000x2000 píxeles.",
};

const FALLBACK_MESSAGE = "No se pudo completar la solicitud. Inténtalo de nuevo.";

/** Texto único por código de error de marca — mismo texto en cliente y servidor (AC2). */
export function brandingErrorMessage(code: string | null | undefined): string {
  if (!code) return FALLBACK_MESSAGE;
  return BRANDING_ERROR_MESSAGES[code] ?? FALLBACK_MESSAGE;
}

// Reglas de validación de caracteres por TIPO de campo, espejo del backend
// (Flit.Admin.Application.Common.TextFieldPatterns y DocumentTypeValidator).
// Centraliza patrones + sanitizadores para que el frontend valide igual que el
// backend y no haya deriva entre capas.
//
// Estrategia de INGRESO: los `sanitize*` se aplican en onChange para que el campo
// simplemente NO acepte caracteres fuera del conjunto permitido (también al pegar).
// Estrategia de BÚSQUEDA: solo se sanea (trim + maxLength + quitar < >), sin bloquear
// por patrón estricto, para no estorbar la búsqueda parcial; los comodines LIKE los
// escapa el backend.

// --- Patrones (allow-list) -------------------------------------------------

/** Nombres legibles (razón social, nombre de tipo de documento): letras Unicode
 *  (con tildes/ñ), dígitos, espacios y puntuación básica `. , & ( ) / ' ° -`. */
export const NAME_PATTERN = /^[\p{L}\p{N}\s.,&()/'°-]+$/u;
/** NIT / identificador tributario: dígitos, puntos y guiones. */
export const TAX_ID_PATTERN = /^[0-9.\-]+$/;
/** Código de tenant: alfanumérico con guion y guion bajo. */
export const TENANT_CODE_PATTERN = /^[A-Za-z0-9_-]+$/;
/** Código de tipo de documento: alfanumérico con guion. */
export const DOC_CODE_PATTERN = /^[A-Za-z0-9-]+$/;

// --- Sanitizadores (quitan lo no permitido) --------------------------------

/** Quita todo lo que no sea letra/dígito/espacio/puntuación básica de nombres. */
export const sanitizeName = (v: string): string => v.replace(/[^\p{L}\p{N}\s.,&()/'°-]/gu, "");
/** Deja solo dígitos, puntos y guiones (NIT). */
export const sanitizeTaxId = (v: string): string => v.replace(/[^0-9.\-]/g, "");
/** Deja solo alfanumérico, guion y guion bajo (código de tenant). */
export const sanitizeTenantCode = (v: string): string => v.replace(/[^A-Za-z0-9_-]/g, "");
/** Deja solo alfanumérico y guion (código de tipo de documento). */
export const sanitizeDocCode = (v: string): string => v.replace(/[^A-Za-z0-9-]/g, "");
/** Texto libre seguro: quita `<` y `>` (anti-XSS). Usado en descripciones y búsquedas. */
export const sanitizeNoAngleBrackets = (v: string): string => v.replace(/[<>]/g, "");

// --- Reglas de contenido mínimo (evitar valores de pura puntuación) ----------

/** ¿Contiene al menos una letra o dígito? Evita nombres/códigos de solo puntuación (p.ej. `'--.,/()`). */
export const hasLetterOrDigit = (v: string): boolean => /[\p{L}\p{N}]/u.test(v);
/** ¿Contiene al menos un dígito? Para NIT (evita un NIT de solo puntos/guiones). */
export const hasDigit = (v: string): boolean => /[0-9]/.test(v);

/**
 * Valida la "fuerza" de un nombre legible (razón social, nombre de tipo de documento), espejo del backend
 * (TextFieldPatterns.ValidateReadableName): empieza con alfanumérico, contiene al menos una letra y no
 * tiene puntuación repetida seguida (`&&`, `//`, `((`). Asume el valor ya recortado (trim).
 * Devuelve un mensaje de error (con `fieldLabel`) o `null` si es válido.
 */
export const validateReadableName = (value: string, fieldLabel: string): string | null => {
  if (!/^[\p{L}\p{N}]/u.test(value)) return `${fieldLabel} debe empezar con una letra o un número.`;
  if (!/\p{L}/u.test(value)) return `${fieldLabel} debe contener al menos una letra.`;
  if (/[.,&()/'°-]{2,}/.test(value))
    return `${fieldLabel} no debe tener símbolos especiales repetidos seguidos (p.ej. && // (( ).`;
  return null;
};

/** Tope de longitud de los términos de texto libre de BÚSQUEDA (espejo del backend). */
export const SEARCH_TEXT_MAX_LENGTH = 100;

// --- Trámites: VIN, placa y número de documento ----------------------------

/**
 * VIN (ISO 3779): 17 caracteres, letras A–Z EXCLUYENDO I, O, Q (se confunden con
 * 1/0) y dígitos 0–9. NO se valida el dígito verificador (posición 9): es un
 * requisito norteamericano (ISO 3780/FMVSS) y muchos VIN importados no lo cumplen.
 */
export const VIN_PATTERN = /^[A-HJ-NPR-Z0-9]{17}$/;
/** Deja solo el charset válido de VIN, en mayúsculas y tope de 17. */
export const sanitizeVin = (v: string): string =>
  v.toUpperCase().replace(/[^A-HJ-NPR-Z0-9]/g, "").slice(0, 17);
export const validateVin = (value: string): string | null =>
  VIN_PATTERN.test(value)
    ? null
    : "El VIN debe tener 17 caracteres (letras y números, sin I, O ni Q).";

/**
 * Placa colombiana (unión de formatos): carro/público `AAA123`, moto actual
 * `AAA12A` o antigua `AAA12`, remolque/semirremolque `R12345`/`S12345` o de 6 dígitos
 * `R123456`/`S123456` (HU #10999 — así el paso 1 no bloquea la consulta de remolques,
 * igual que maquinaria), y maquinaria (agrícola/industrial/construcción) `AA123456`
 * (2 letras + 6 dígitos, p. ej. `MC029554`). La placa la valida en definitiva el RUNT;
 * este patrón es un chequeo anti-error de tipeo.
 */
export const PLATE_PATTERN = /^([A-Z]{3}[0-9]{3}|[A-Z]{3}[0-9]{2}[A-Z]?|[RS][0-9]{5,6}|[A-Z]{2}[0-9]{6})$/;
/** Deja solo alfanumérico en mayúsculas, tope de 8 (maquinaria `AA123456`). */
export const sanitizePlate = (v: string): string =>
  v.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 8);
export const validatePlate = (value: string): string | null =>
  PLATE_PATTERN.test(value)
    ? null
    : "Placa inválida. Ej: ABC123 (carro), ABC12D (moto), R12345 (remolque) o MC029554 (maquinaria).";

/** ¿El tipo de documento es pasaporte? (admite letras y números). */
export const isPassport = (docType: string): boolean => docType.trim().toUpperCase() === "PAS";
/**
 * Número de documento por tipo: pasaporte admite letras y números; el resto
 * (CC, CE, TI, NIT) solo dígitos. Se sanea en onChange según el tipo actual.
 */
export const sanitizeDocNumber = (v: string, docType: string): string =>
  isPassport(docType) ? v.replace(/[^A-Za-z0-9]/g, "") : v.replace(/[^0-9]/g, "");
export const validateDocNumber = (value: string, docType: string): string | null => {
  if (isPassport(docType))
    return /^[A-Za-z0-9]+$/.test(value) ? null : "El pasaporte solo admite letras y números.";
  return /^[0-9]+$/.test(value) ? null : "El número de documento solo admite dígitos.";
};

// HU #14076 — reglas de forma del contenido del portal (las mismas que aplica la API, HU #14073): texto plano con
// límite de longitud y sin HTML, y enlaces absolutos http/https (el contacto de ventas admite además mailto y tel).
import {
  BANNER_IMAGEN_MAX_BYTES,
  BANNER_IMAGEN_TIPOS,
  ENLACE_MAX,
  type CodigoEnlace,
} from "@/lib/api/admin-pn-contenido";

const ETIQUETA_HTML = /<[A-Za-z/!?]/;

/** Texto obligatorio, plano y acotado. Devuelve el mensaje de error o `null` si es válido. */
export function validarTextoPlano(texto: string, nombre: string, max: number): string | null {
  const limpio = texto.trim();
  if (limpio === "") return `${nombre} es obligatorio.`;
  if (limpio.length > max) return `${nombre} admite hasta ${max} caracteres.`;
  if (ETIQUETA_HTML.test(limpio)) return `${nombre} es texto simple: no se acepta HTML.`;
  return null;
}

/** Enlace opcional (vacío = sin enlace). Devuelve el mensaje de error o `null`. */
export function validarEnlace(valor: string, codigo?: CodigoEnlace): string | null {
  const url = valor.trim();
  if (url === "") return null;
  if (url.length > ENLACE_MAX) return `El enlace admite hasta ${ENLACE_MAX} caracteres.`;
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return "El enlace debe ser una URL válida, por ejemplo https://flit.com.co/politica.";
  }
  const permitido =
    parsed.protocol === "http:" ||
    parsed.protocol === "https:" ||
    (codigo === "CONTACTO_VENTAS" && (parsed.protocol === "mailto:" || parsed.protocol === "tel:"));
  return permitido
    ? null
    : codigo === "CONTACTO_VENTAS"
      ? "El enlace debe empezar por http://, https://, mailto: o tel:."
      : "El enlace debe empezar por http:// o https://.";
}

/** Valida la imagen de un banner: JPG, PNG o WEBP de hasta 2 MB. */
export function validarImagenBanner(archivo: File): string | null {
  if (!(BANNER_IMAGEN_TIPOS as readonly string[]).includes(archivo.type)) {
    return "La imagen debe ser JPG, PNG o WEBP.";
  }
  if (archivo.size > BANNER_IMAGEN_MAX_BYTES) return "La imagen no puede pesar más de 2 MB.";
  return null;
}

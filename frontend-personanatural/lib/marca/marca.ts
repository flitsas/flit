// HU #14379 (Feature #14374) — Marca del portal del OT en el punto. Sin dependencias de Next: se prueba con Vitest.
//
// La marca llega del BFF (`GET /punto/marca/{codigo}`) y es dato editable por el organismo: nada de lo que trae se
// inyecta tal cual. Los colores se validan como #RRGGBB antes de entrar a una hoja de estilos, el nombre se trata como
// texto plano (React lo escapa; aquí además se limpian controles y se acota) y las imágenes solo se aceptan si apuntan
// a las rutas de imágenes de marca del propio BFF (mismo origen, así que la CSP `img-src 'self'` basta).

export interface ColoresMarca {
  primario: string;
  secundario: string;
  sobrePrimario: string;
}

/** Lo que el portal aplica de una marca ya validada. Los campos de imagen en `null` usan la imagen FLIT. */
export interface MarcaAplicable {
  nombre: string | null;
  colores: ColoresMarca;
  logoUrl: string | null;
  bannerUrl: string | null;
  version: number;
}

const HEX = /^#[0-9A-Fa-f]{6}$/;
// Ruta de una imagen publicada: `/api/v1/persona-natural/marca/imagenes/{id}` (el prefijo es opcional en lo que llega).
const RUTA_IMAGEN = /^(?:\/api\/v1\/persona-natural)?\/marca\/imagenes\/([0-9A-Za-z-]{1,64})$/;
const PREFIJO_IMAGEN = "/api/v1/persona-natural/marca/imagenes/";
const MAX_NOMBRE = 80;

export const esHex = (valor: unknown): valor is string => typeof valor === "string" && HEX.test(valor);

/** Ruta relativa y segura de una imagen de marca, o `null` si la URL no es del BFF del portal. */
export function rutaImagenMarca(valor: unknown): string | null {
  if (typeof valor !== "string") return null;
  const coincidencia = RUTA_IMAGEN.exec(valor.trim());
  return coincidencia ? `${PREFIJO_IMAGEN}${coincidencia[1]}` : null;
}

/** Nombre como texto plano: sin caracteres de control ni espacios repetidos, acotado. Vacío → `null`. */
export function nombreSeguro(valor: unknown): string | null {
  if (typeof valor !== "string") return null;
  // Se descartan los caracteres de control y los invisibles (ancho cero, marcas de dirección) por su código.
  const sinInvisibles = Array.from(valor, (c) => {
    const k = c.codePointAt(0) ?? 0;
    return k <= 0x1f || (k >= 0x7f && k <= 0x9f) || (k >= 0x200b && k <= 0x200f) || (k >= 0x2028 && k <= 0x202e) ? " " : c;
  }).join("");
  const limpio = sinInvisibles.replace(/\s+/g, " ").trim();
  return limpio ? limpio.slice(0, MAX_NOMBRE) : null;
}

/**
 * Valida lo que respondió el BFF. Devuelve `null` (marca FLIT, el portal queda como hoy) si el origen no es `organismo`,
 * si falta algún color o si alguno no es #RRGGBB: una marca a medias no se aplica.
 */
export function marcaAplicable(crudo: unknown, origenes: readonly string[] = ["organismo"]): MarcaAplicable | null {
  if (crudo === null || typeof crudo !== "object") return null;
  const m = crudo as Record<string, unknown>;
  if (typeof m.origen !== "string" || !origenes.includes(m.origen)) return null;
  const c = m.colores;
  if (c === null || typeof c !== "object") return null;
  const { primario, secundario, sobrePrimario } = c as Record<string, unknown>;
  if (!esHex(primario) || !esHex(secundario) || !esHex(sobrePrimario)) return null;
  return {
    nombre: nombreSeguro(m.nombre),
    colores: { primario: primario.toUpperCase(), secundario: secundario.toUpperCase(), sobrePrimario: sobrePrimario.toUpperCase() },
    logoUrl: rutaImagenMarca(m.logoUrl),
    bannerUrl: rutaImagenMarca(m.bannerUrl),
    version: typeof m.version === "number" && Number.isFinite(m.version) ? m.version : 0,
  };
}

/** `true` si el blanco se lee sobre el color (≥ 4,5:1): los botones blancos del portal usan ese texto. */
function luminancia(hex: string): number {
  const canal = (i: number) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * canal(1) + 0.7152 * canal(3) + 0.0722 * canal(5);
}

function textoSobreBlanco(colores: ColoresMarca): string {
  const lum = luminancia(colores.primario);
  return (1 + 0.05) / (lum + 0.05) >= 4.5 ? colores.primario : colores.sobrePrimario;
}

/**
 * Hoja de estilos con los tokens de la marca. Solo contiene valores #RRGGBB ya validados (ni el nombre ni las URL entran
 * aquí), así que no hay forma de colar CSS. `html:root` gana a `:root` de globals.css sin depender del orden de carga.
 *  - primario → `--primary` (títulos, botones, pasos); sobrePrimario → `--primary-foreground`.
 *  - secundario → `--dashboard-action-background` (tarjeta de acción) y cierre del degradado.
 */
export function cssDeMarca(marca: MarcaAplicable): string {
  const { primario, secundario, sobrePrimario } = marca.colores;
  if (!esHex(primario) || !esHex(secundario) || !esHex(sobrePrimario)) return "";
  const degradado = `linear-gradient(135deg, ${secundario}, ${primario})`;
  return [
    `html:root{--primary:${primario};--primary-foreground:${sobrePrimario};--ring:${primario};--sidebar-primary:${primario};--sidebar-ring:${primario};`,
    `--dashboard-action-background:${secundario};--gradient-flit:${degradado};--gradient-enterprise:${degradado};--marca-texto-boton:${textoSobreBlanco(marca.colores)}}`,
    `html:root .dashboard-institutional-card .dashboard-submit{color:var(--marca-texto-boton)}`,
    `html:root .dashboard-institutional-card .dashboard-title{color:var(--primary-foreground)}`,
  ].join("");
}

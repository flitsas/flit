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

/** Imagen del carrusel del OT (solo punto del organismo): ruta del propio BFF, texto alternativo y enlace http/https opcional. */
export interface ItemCarrusel {
  imagenUrl: string;
  alt: string;
  enlace: string | null;
}

const MAX_ITEMS_CARRUSEL = 6;
const MAX_ALT = 120;
const MAX_ENLACE = 500;

/** Lo que el portal aplica de una marca ya validada. Los campos de imagen en `null` usan la imagen FLIT. */
export interface MarcaAplicable {
  nombre: string | null;
  colores: ColoresMarca;
  logoUrl: string | null;
  bannerUrl: string | null;
  version: number;
  /** Carrusel de imágenes del OT. Solo existe en la marca del organismo; ausente = se muestran los servicios de siempre. */
  carrusel?: readonly ItemCarrusel[];
  /**
   * Banners principales del OT (imagen, texto alternativo y enlace opcional). Solo existen en la marca del organismo;
   * ausentes = rige `bannerUrl` (banner único) o los banners de siempre.
   */
  banners?: readonly ItemCarrusel[];
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
export function nombreSeguro(valor: unknown, maximo: number = MAX_NOMBRE): string | null {
  if (typeof valor !== "string") return null;
  // Se descartan los caracteres de control y los invisibles (ancho cero, marcas de dirección) por su código.
  const sinInvisibles = Array.from(valor, (c) => {
    const k = c.codePointAt(0) ?? 0;
    return k <= 0x1f || (k >= 0x7f && k <= 0x9f) || (k >= 0x200b && k <= 0x200f) || (k >= 0x2028 && k <= 0x202e) ? " " : c;
  }).join("");
  const limpio = sinInvisibles.replace(/\s+/g, " ").trim();
  return limpio ? limpio.slice(0, maximo) : null;
}

/** Enlace solo http/https, acotado y sin credenciales; cualquier otra cosa → `null` (la imagen se muestra sin enlace). */
function enlaceSeguro(valor: unknown): string | null {
  if (typeof valor !== "string" || valor.length === 0 || valor.length > MAX_ENLACE) return null;
  try {
    const url = new URL(valor.trim());
    if ((url.protocol !== "https:" && url.protocol !== "http:") || url.username || url.password) return null;
    return url.toString();
  } catch {
    return null;
  }
}

/** Ítems válidos del carrusel: imagen del BFF y texto alternativo obligatorios; el resto se descarta. Máximo 6. */
function carruselSeguro(valor: unknown): ItemCarrusel[] {
  if (!Array.isArray(valor)) return [];
  const items: ItemCarrusel[] = [];
  for (const crudo of valor) {
    if (items.length >= MAX_ITEMS_CARRUSEL) break;
    if (crudo === null || typeof crudo !== "object") continue;
    const i = crudo as Record<string, unknown>;
    const imagenUrl = rutaImagenMarca(i.imagenUrl);
    const alt = nombreSeguro(i.alt, MAX_ALT);
    if (imagenUrl && alt) items.push({ imagenUrl, alt, enlace: enlaceSeguro(i.enlace) });
  }
  return items;
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
    // El carrusel es solo del punto del organismo: la web de persona no lo trae ni lo usa.
    ...(m.origen === "organismo" && carruselSeguro(m.carrusel).length > 0 ? { carrusel: carruselSeguro(m.carrusel) } : {}),
    // Igual que el carrusel: solo el punto del organismo trae banners; la web de persona no los usa.
    ...(m.origen === "organismo" && carruselSeguro(m.banners).length > 0 ? { banners: carruselSeguro(m.banners) } : {}),
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

/** Contraste del color contra el blanco (1 a 21). */
const contrasteConBlanco = (hex: string): number => 1.05 / (luminancia(hex) + 0.05);

/** Mezcla `hex` con negro (`parte` = 0..1 de negro). Para el trazo oscuro de los íconos de los pasos. */
function oscurecer(hex: string, parte: number): string {
  const canal = (i: number) => Math.round(parseInt(hex.slice(i, i + 2), 16) * (1 - parte)).toString(16).padStart(2, "0");
  return `#${canal(1)}${canal(3)}${canal(5)}`.toUpperCase();
}

/** El color tal cual si se lee sobre blanco (≥ 4,5:1); si no, se oscurece de a poco hasta que se lea. Para texto corrido. */
function legibleSobreBlanco(hex: string): string {
  let color = hex;
  for (let parte = 0.1; contrasteConBlanco(color) < 4.5 && parte <= 0.9; parte += 0.1) color = oscurecer(hex, parte);
  return color;
}

/** Blanco si se lee (≥ 4,5:1, como en el diseño); si no, blanco o negro, el que mejor contraste dé. */
const textoSobre = (hex: string): string => (contrasteConBlanco(hex) >= 4.5 || contrasteConBlanco(hex) >= (luminancia(hex) + 0.05) / 0.05 ? "#FFFFFF" : "#000000");

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
    // Paleta base del diseño del OT (prototipo): el texto toma el primario (oscurecido si no se lee), el secundario es el
    // acento (`--secondary`, `--accent`) y bordes, fondo y texto atenuado pasan a los neutros del diseño. Así el wizard
    // (pasos, títulos, textos, campos) deja de verse con el navy/azul de FLIT.
    `html:root{--foreground:${legibleSobreBlanco(primario)};--card-foreground:${legibleSobreBlanco(primario)};--popover-foreground:${legibleSobreBlanco(primario)};--sidebar-foreground:${legibleSobreBlanco(primario)};--flit-navy:${legibleSobreBlanco(primario)};--flit-blue:${primario};`,
    `--secondary:${secundario};--secondary-foreground:${textoSobre(secundario)};--accent:${secundario};--accent-foreground:${textoSobre(secundario)};--flit-teal:${secundario};`,
    `--background:#F8F9FA;--flit-soft:#F8F9FA;--muted:#F8F9FA;--border:#D7D7D7;--input:#D7D7D7;--flit-line:#D7D7D7;--muted-foreground:#62666C;`,
    `--shadow-float:3px 3px 3px color-mix(in oklab,${primario} 30%,transparent);--shadow-select:0 4px 12px color-mix(in oklab,${primario} 12%,transparent);--shadow-action:0 2px 8px color-mix(in oklab,${primario} 12%,transparent)}`,
    // Íconos de los 4 pasos con los colores de la marca.
    `html:root{--paso-claro:${primario};--paso-oscuro:${oscurecer(primario, 0.55)}}`,
    // Números 01/02/03 de «Somos»: texto grande, con el secundario si se lee (≥ 3:1) y si no con el primario.
    `html:root .somos-card-num{color:${contrasteConBlanco(secundario) >= 3 ? secundario : primario}}`,
    // El color secundario también es el acento de títulos y números de paso, solo si se lee sobre blanco (≥ 3:1, texto grande).
    ...(contrasteConBlanco(secundario) >= 3
      ? [
          `html:root{--gradient-heading:linear-gradient(to right,${secundario},${secundario})}`,
          `html:root .dashboard-card .dashboard-title:not(.flit-gradient-title),html:root .somos-kicker{color:${secundario}}`,
          `html:root .paso-num{background:${secundario};color:${contrasteConBlanco(secundario) >= 4.5 ? "#FFFFFF" : "#000000"}}`,
        ]
      : []),
  ].join("");
}

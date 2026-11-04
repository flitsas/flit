// Configuración publicada del portal (HU #14073) convertida en contenido seguro para la landing. Todo texto del servidor se
// trata como TEXTO plano (React lo escapa; aquí solo se limpia y se acota), las URL se validan (solo http/https) y la imagen
// de un banner solo se acepta si es una ruta del propio BFF. Si algo no cumple, esa pieza se descarta y rige el contenido fijo.
import type { ConfiguracionPortal } from "@/lib/api/cliente";
import { urlPublicable } from "@/lib/legal";

export interface PreguntaVista {
  id: string;
  pregunta: string;
  respuesta: string;
}

export interface BannerVista {
  id: string;
  src: string;
  alt: string;
  /** URL http(s) validada a la que lleva el banner, si tiene. */
  enlace: string | null;
}

export interface SoporteVista {
  correo: string | null;
  telefono: string | null;
  horario: string | null;
}

/** Cada campo es `null` cuando no hay nada publicado (o no pasó la validación): ahí rige el contenido fijo. */
export interface ContenidoPublicado {
  preguntas: PreguntaVista[] | null;
  banners: BannerVista[] | null;
  /** URL válida por código de enlace con documento (POLITICA_DATOS, TERMINOS, CONTACTO_VENTAS, VIDEO). */
  enlaces: Readonly<Record<string, string | null>>;
  soporte: SoporteVista | null;
}

export const SIN_PUBLICAR: ContenidoPublicado = { preguntas: null, banners: null, enlaces: {}, soporte: null };

const IMAGEN_BANNER = /^\/api\/v1\/persona-natural\/configuracion\/banners\/[A-Za-z0-9-]{1,64}\/imagen(\?v=[A-Za-z0-9]{1,128})?$/;
const CORREO = /^[^\s@<>"']{1,64}@[A-Za-z0-9.-]{1,190}\.[A-Za-z]{2,}$/;
const TELEFONO = /^\+?[\d\s().-]{5,25}$/;

/** Texto plano: sin caracteres de control y acotado. Nunca se interpreta como HTML. */
export function textoPlano(valor: unknown, max = 2000): string {
  if (typeof valor !== "string") return "";
  return valor.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, "").trim().slice(0, max);
}

function lista<T>(valor: unknown): T[] {
  return Array.isArray(valor) ? (valor as T[]) : [];
}

function objeto(valor: unknown): Record<string, unknown> | null {
  return valor !== null && typeof valor === "object" && !Array.isArray(valor) ? (valor as Record<string, unknown>) : null;
}

/** Hay configuración publicada solo si la versión existe y no es «0». */
export function estaPublicada(configuracion: ConfiguracionPortal | undefined): configuracion is ConfiguracionPortal {
  return typeof configuracion?.version === "string" && configuracion.version !== "" && configuracion.version !== "0";
}

export function resolverContenido(configuracion: ConfiguracionPortal | undefined): ContenidoPublicado {
  if (!estaPublicada(configuracion)) return SIN_PUBLICAR;

  const preguntas = lista<unknown>(configuracion.preguntas)
    .map(objeto)
    .filter((p): p is Record<string, unknown> => p !== null)
    .sort((a, b) => Number(a.orden ?? 0) - Number(b.orden ?? 0))
    .map((p): PreguntaVista => ({ id: textoPlano(p.id, 64), pregunta: textoPlano(p.pregunta, 300), respuesta: textoPlano(p.respuesta, 4000) }))
    .filter((p) => p.pregunta !== "" && p.respuesta !== "");

  const banners = lista<unknown>(configuracion.banners)
    .map(objeto)
    .filter((b): b is Record<string, unknown> => b !== null)
    .sort((a, b) => Number(a.orden ?? 0) - Number(b.orden ?? 0))
    .flatMap((b): BannerVista[] => {
      const src = typeof b.imagenUrl === "string" ? b.imagenUrl : "";
      const alt = textoPlano(b.textoAlternativo, 500);
      if (!IMAGEN_BANNER.test(src) || alt === "") return [];
      return [{ id: textoPlano(b.id, 64), src, alt, enlace: urlPublicable(b.enlaceUrl) }];
    });

  const enlaces: Record<string, string | null> = {};
  for (const e of lista<unknown>(configuracion.enlaces)) {
    const enlace = objeto(e);
    if (!enlace || typeof enlace.codigo !== "string") continue;
    enlaces[enlace.codigo] = enlace.conDocumento === true ? urlPublicable(enlace.url) : null;
  }

  const s = objeto(configuracion.soporte);
  const correo = s && CORREO.test(textoPlano(s.correo, 254)) ? textoPlano(s.correo, 254) : null;
  const telefono = s && TELEFONO.test(textoPlano(s.telefono, 25)) ? textoPlano(s.telefono, 25) : null;
  const horario = s ? textoPlano(s.horario, 200) || null : null;

  return {
    preguntas: preguntas.length > 0 ? preguntas : null,
    banners: banners.length > 0 ? banners : null,
    enlaces,
    soporte: correo || telefono || horario ? { correo, telefono, horario } : null,
  };
}

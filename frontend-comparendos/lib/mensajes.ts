// Lectura de los destinatarios que guarda el backend en cada comunicación (§7.7): un JSON {para:[{email,nombre}], cc:[…], bcc:[…]}.
// Funciones puras para el Centro de Mensajes y sus pruebas.

export interface Destinatarios {
  para: string[];
  cc: string[];
  bcc: string[];
}

const VACIO: Destinatarios = { para: [], cc: [], bcc: [] };

function correos(valor: unknown): string[] {
  if (!Array.isArray(valor)) return [];
  return valor
    .map((x) => {
      if (typeof x === "string") return x;
      if (x && typeof x === "object" && "email" in x && typeof x.email === "string") {
        const nombre = "nombre" in x && typeof x.nombre === "string" && x.nombre.trim() ? x.nombre.trim() : null;
        return nombre ? `${nombre} <${x.email}>` : x.email;
      }
      return null;
    })
    .filter((x): x is string => !!x);
}

/** Interpreta el JSON de destinatarios; si no es JSON (datos viejos) lo trata como una lista separada por comas. */
export function leerDestinatarios(texto: string | null | undefined): Destinatarios {
  if (!texto?.trim()) return VACIO;
  try {
    const v: unknown = JSON.parse(texto);
    if (!v || typeof v !== "object" || Array.isArray(v)) return VACIO;
    const o = v as Record<string, unknown>;
    return { para: correos(o.para), cc: correos(o.cc), bcc: correos(o.bcc) };
  } catch {
    return { para: texto.split(",").map((x) => x.trim()).filter(Boolean), cc: [], bcc: [] };
  }
}

/** Resumen corto para la tabla: el primer destinatario y cuántos más hay (sin contar las copias ocultas). */
export function resumenDestinatarios(d: Destinatarios): string {
  const visibles = [...d.para, ...d.cc];
  if (visibles.length === 0) return "—";
  return visibles.length === 1 ? visibles[0]! : `${visibles[0]} y ${visibles.length - 1} más`;
}

/** Solo un correo FALLIDO se reintenta: un rebote significa que la dirección no existe y hay que corregirla. */
export const sePuedeReintentar = (estado: string, esSuperAdmin: boolean) => estado === "Fallido" && !esSuperAdmin;

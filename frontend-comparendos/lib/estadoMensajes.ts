// Estado del Centro de mensajes en la URL (rediseño S1): al volver del detalle de un comparendo queda igual.
import { ESTADO_COMUNICACION, opciones } from "@/lib/etiquetas";
import type { LectorParams } from "@/lib/filtros";

export interface EstadoMensajes {
  empresa: string | null;
  internas: boolean;
  tipo: string;
  estado: string;
  canal: string;
  desde: string;
  hasta: string;
  comparendo: string;
  placa: string;
  pagina: number;
  tamano: number;
  /** El mensaje abierto (`?mensaje=`, también el enlace desde el timeline del comparendo). */
  mensaje: string | null;
}

export const TAMANO_MENSAJES = 25;

export const MENSAJES_POR_DEFECTO: EstadoMensajes = {
  empresa: null,
  internas: false,
  tipo: "",
  estado: "",
  canal: "",
  desde: "",
  hasta: "",
  comparendo: "",
  placa: "",
  pagina: 1,
  tamano: TAMANO_MENSAJES,
  mensaje: null,
};

const FECHA = /^\d{4}-\d{2}-\d{2}$/;

/** Solo lo que difiere de fábrica: el Centro recién abierto deja la URL limpia. */
export function mensajesAParams(e: EstadoMensajes): URLSearchParams {
  const p = new URLSearchParams();
  if (e.internas) p.set("internas", "true");
  else if (e.empresa) p.set("empresa", e.empresa);
  for (const clave of ["tipo", "estado", "canal", "desde", "hasta"] as const) if (e[clave]) p.set(clave, e[clave]);
  if (e.comparendo.trim()) p.set("comparendo", e.comparendo.trim());
  if (e.placa.trim()) p.set("placa", e.placa.trim());
  if (e.pagina > 1) p.set("pagina", String(e.pagina));
  if (e.tamano !== TAMANO_MENSAJES) p.set("tamano", String(e.tamano));
  if (e.mensaje) p.set("mensaje", e.mensaje);
  return p;
}

/** La inversa; lo que no se reconoce (fecha mal escrita, página negativa) se ignora. */
export function mensajesDeParams(p: LectorParams): EstadoMensajes {
  const fecha = (clave: string) => {
    const v = p.get(clave) ?? "";
    return FECHA.test(v) ? v : "";
  };
  const entero = (clave: string, minimo: number, maximo: number, defecto: number) => {
    const n = Number(p.get(clave));
    return Number.isInteger(n) && n >= minimo && n <= maximo ? n : defecto;
  };
  const internas = p.get("internas") === "true";
  return {
    empresa: internas ? null : p.get("empresa") || null,
    internas,
    tipo: p.get("tipo") ?? "",
    estado: p.get("estado") ?? "",
    canal: p.get("canal") ?? "",
    desde: fecha("desde"),
    hasta: fecha("hasta"),
    comparendo: (p.get("comparendo") ?? "").trim(),
    placa: (p.get("placa") ?? "").trim().toUpperCase(),
    pagina: entero("pagina", 1, 100_000, 1),
    tamano: entero("tamano", 1, 100, TAMANO_MENSAJES),
    mensaje: p.get("mensaje") || null,
  };
}

/** Combinaciones con nombre propio: la tarjeta «Correos fallidos» del Inicio abre `?estado=Fallido,Rebotado`. */
const COMBINACIONES: Record<string, string> = { "Fallido,Rebotado": "Fallidos o rebotados" };

/**
 * Opciones del filtro de Estado. El backend acepta varios estados separados por coma, así que el <select> debe poder
 * mostrar el que trae la URL: las combinaciones conocidas tienen su nombre y cualquier otra se nombra con sus partes.
 */
export function opcionesEstadoMensajes(actual: string): { valor: string; texto: string }[] {
  const base = [...opciones(ESTADO_COMUNICACION), ...Object.entries(COMBINACIONES).map(([valor, texto]) => ({ valor, texto }))];
  if (!actual || base.some((o) => o.valor === actual)) return base;
  const partes = actual.split(",").map((v) => v.trim()).filter(Boolean);
  const texto = partes.map((v) => ESTADO_COMUNICACION[v]?.texto ?? v).join(" o ");
  return [...base, { valor: actual, texto }];
}

// Configuración de consultas por empresa (HU #13522 sobre la API de la HU #13521). El SuperAdmin define por canal el
// proveedor y la agenda; la empresa solo ve la agenda (la API no le devuelve proveedores).

export type ModoAgenda = "HorasFijas" | "Intervalo";
export type EstadoConsultas = "Borrador" | "Activa" | "Pausada";

export interface CanalConsulta {
  canal: string;
  etiqueta: string;
  configurado: boolean;
  encendido: boolean;
  modo: ModoAgenda;
  horas: string[];
  intervaloMinutos: number | null;
  desde: string | null;
  hasta: string | null;
  soloDiasHabiles: boolean;
  descripcion: string | null;
  ultimaConsulta: string | null;
  ultimoResultado: string | null;
  /** Hora de Colombia sin zona («2026-10-13T13:00:00»). */
  proximaConsulta: string | null;
  /** Solo viene para el SuperAdmin. */
  proveedor?: string | null;
}

export interface ProveedorConsulta {
  clave: string;
  nombre: string;
  canales: string[];
  conectado: boolean;
  motivo: string | null;
}

export interface ConfiguracionConsultasVista {
  estado: EstadoConsultas;
  activadaEn: string | null;
  canales: CanalConsulta[];
  productoHabilitado?: boolean;
  proveedores?: ProveedorConsulta[];
}

export interface FormCanal {
  encendido: boolean;
  proveedor: string;
  modo: ModoAgenda;
  horas: string[];
  intervaloMinutos: string;
  desde: string;
  hasta: string;
  soloDiasHabiles: boolean;
}

export const INTERVALO_MINIMO = 30;

const HORA = /^([01]?\d|2[0-3]):([0-5]\d)$/;

/** «7:00» → «07:00»; null si no es una hora válida. */
export function normalizarHora(texto: string): string | null {
  const m = HORA.exec(texto.trim());
  return m ? `${m[1].padStart(2, "0")}:${m[2]}` : null;
}

/** Agrega una hora a la lista (normalizada, sin repetir y ordenada). Devuelve null si no es válida. */
export function agregarHora(horas: string[], texto: string): string[] | null {
  const h = normalizarHora(texto);
  if (!h) return null;
  return [...new Set([...horas, h])].sort();
}

export function formDeCanal(c: CanalConsulta): FormCanal {
  return {
    encendido: c.encendido,
    proveedor: c.proveedor ?? "",
    modo: c.modo,
    horas: [...c.horas].sort(),
    intervaloMinutos: c.intervaloMinutos ? String(c.intervaloMinutos) : "60",
    desde: c.desde ?? "06:00",
    hasta: c.hasta ?? "20:00",
    soloDiasHabiles: c.soloDiasHabiles,
  };
}

/** Lo que falta o está mal antes de guardar (los mismos límites que la API). */
export function erroresCanal(f: FormCanal, proveedores: ProveedorConsulta[], canal: string): string[] {
  const errores: string[] = [];
  const p = proveedores.find((x) => x.clave === f.proveedor);
  if (!f.proveedor) errores.push("Elige el proveedor.");
  else if (!p) errores.push("El proveedor ya no existe en este ambiente.");
  else if (!p.canales.includes(canal)) errores.push(`${p.nombre} no atiende este canal.`);
  else if (f.encendido && !p.conectado) errores.push(`${p.nombre} no está conectado: no se puede encender.`);
  if (f.modo === "HorasFijas") {
    if (f.horas.length === 0) errores.push("Agrega al menos una hora.");
    if (f.horas.length > 24) errores.push("Máximo 24 horas al día.");
  } else {
    const n = Number(f.intervaloMinutos);
    if (!Number.isInteger(n) || n < INTERVALO_MINIMO || n > 1440) errores.push(`El intervalo va de ${INTERVALO_MINIMO} minutos a 24 horas.`);
    const d = normalizarHora(f.desde);
    const h = normalizarHora(f.hasta);
    if (!d || !h) errores.push("Indica desde y hasta (HH:mm).");
    else if (h < d) errores.push("«Hasta» debe ser posterior a «desde» en el mismo día.");
  }
  return errores;
}

/** Cuerpo del PUT /admin/empresas/{empresa}/consultas/canales/{canal}. */
export function cuerpoCanal(f: FormCanal) {
  return f.modo === "HorasFijas"
    ? { encendido: f.encendido, proveedor: f.proveedor, modo: f.modo, horas: f.horas, intervaloMinutos: null, desde: null, hasta: null, soloDiasHabiles: f.soloDiasHabiles }
    : {
        encendido: f.encendido,
        proveedor: f.proveedor,
        modo: f.modo,
        horas: [],
        intervaloMinutos: Number(f.intervaloMinutos),
        desde: normalizarHora(f.desde),
        hasta: normalizarHora(f.hasta),
        soloDiasHabiles: f.soloDiasHabiles,
      };
}

/** Proveedores que atienden un canal (los no conectados también, para mostrarlos deshabilitados con su motivo). */
export function proveedoresDelCanal(proveedores: ProveedorConsulta[], canal: string): ProveedorConsulta[] {
  return proveedores.filter((p) => p.canales.includes(canal));
}

const MESES = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

/** «2026-10-13T13:00:00» (hora de Colombia, sin zona) → «13 oct 2026, 13:00». */
export function horaColombia(valor: string | null | undefined): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(valor ?? "");
  if (!m) return "—";
  return `${Number(m[3])} ${MESES[Number(m[2]) - 1]} ${m[1]}, ${m[4]}:${m[5]}`;
}

export const ESTADO_CONSULTAS: Record<EstadoConsultas, { etiqueta: string; tono: "success" | "warning" | "neutral" }> = {
  Activa: { etiqueta: "Activa", tono: "success" },
  Pausada: { etiqueta: "Pausada", tono: "warning" },
  Borrador: { etiqueta: "Sin activar", tono: "neutral" },
};

export const RESULTADO_CICLO: Record<string, string> = {
  Exitoso: "Exitosa",
  Parcial: "Parcial (algunas claves fallaron)",
  Fallido: "Fallida",
  EnCurso: "En curso",
};

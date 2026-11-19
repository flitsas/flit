// Lógica pura del panel «Qué se puede alegar» (HU #14613). La API decide; aquí solo se traduce a palabras de la gestora.
import { formatoFecha } from "@/lib/format/date";

export type MotivoDecisionDp =
  | "Inactivo"
  | "Pagado"
  | "YaTieneDp"
  | "VentanaDescuento"
  | "SinTipologiaAplicable"
  | "SinTipologiaDisponible";

export interface TipologiaAplicableDp {
  codigo: string;
  nombre: string;
  tipologiaId: string | null;
  razon: string;
  /** false: aplica, pero la empresa no la tiene habilitada (o no tiene versión de plantilla publicada). */
  disponible: boolean;
}

/** GET /comparendos/{id}/dp/decision */
export interface DecisionDp {
  elegible: boolean;
  elegida: string | null;
  tipologiaIdElegida: string | null;
  motivo: MotivoDecisionDp | string | null;
  aplicables: TipologiaAplicableDp[];
}

export interface MensajeMotivo {
  titulo: string;
  detalle: string;
  /** info: hay que esperar o no hay nada que hacer; warning: la gestora puede actuar. */
  tono: "info" | "warning";
}

const MOTIVOS: Record<MotivoDecisionDp, MensajeMotivo> = {
  Inactivo: { titulo: "El comparendo está inactivo", detalle: "No se puede enviar un derecho de petición sobre un comparendo inactivo.", tono: "info" },
  Pagado: { titulo: "El comparendo ya está pagado", detalle: "Ya no hay nada que alegar: el pago cerró el caso.", tono: "info" },
  YaTieneDp: { titulo: "Ya tiene derecho de petición", detalle: "Un comparendo tiene un solo derecho de petición. Mira su estado más arriba.", tono: "info" },
  VentanaDescuento: {
    titulo: "Esperar: se puede trasladar al conductor con descuento",
    detalle: "Mientras el descuento siga vigente conviene pasarle el comparendo al conductor en vez de alegar. Cuando el descuento termine, vuelve a mirar.",
    tono: "info",
  },
  SinTipologiaAplicable: { titulo: "Todavía no aplica ningún argumento", detalle: "Con las fechas y datos de hoy no hay nada que alegar. Se revisa de nuevo cada día.", tono: "info" },
  SinTipologiaDisponible: {
    titulo: "Hay argumentos, pero tu empresa no puede usarlos todavía",
    detalle: "Los argumentos que aplican no están habilitados para tu empresa. Pídele al administrador de FLIT que los habilite.",
    tono: "warning",
  },
};

/** Motivo de la API → título y explicación para la gestora. Un motivo nuevo que la pantalla no conozca no muestra el enum. */
export function mensajeMotivo(motivo: string | null | undefined): MensajeMotivo | null {
  if (!motivo) return null;
  return (
    (MOTIVOS as Record<string, MensajeMotivo>)[motivo] ?? {
      titulo: "Hoy no se puede enviar un derecho de petición",
      detalle: "Si crees que sí aplica, consulta con el administrador de FLIT.",
      tono: "info",
    }
  );
}

/**
 * Pasa la razón de la API a lectura humana: las fechas aaaa-mm-dd salen como «09 de oct de 2026» y cada código de infracción
 * del comparendo (C35…) sale con su descripción, no solo el código.
 */
export function razonLegible(razon: string, infraccion: { codigo: string | null; descripcion: string | null } | null | undefined): string {
  const conFechas = razon.replace(/\b\d{4}-\d{2}-\d{2}\b/g, (f) => formatoFecha(f));
  const codigo = infraccion?.codigo?.trim();
  const descripcion = infraccion?.descripcion?.trim();
  if (!codigo || !descripcion) return conFechas;
  const patron = new RegExp(`\\b${codigo.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\b(?!\\s*\\()`, "gi");
  return conFechas.replace(patron, (m) => `${m} (${descripcion.replace(/\.$/, "")})`);
}

/** «C29 — Exceso de velocidad»; solo el código si no hay descripción; null si no hay código. */
export function infraccionConDescripcion(codigo: string | null | undefined, descripcion: string | null | undefined): string | null {
  const c = codigo?.trim();
  if (!c) return null;
  const d = descripcion?.trim();
  return d ? `${c} — ${d}` : c;
}

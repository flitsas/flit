// Etiquetas del saneamiento (plan §7.1, spec-ui §1.1): única fuente de texto y tono del estado, de las fuentes, de los
// hallazgos, de los resultados de un renglón y de los motivos de no elegibilidad. Los componentes arman la etiqueta con
// `etiquetaKit` (punto + texto); el estado nunca depende solo del color y nunca se muestra un código.
import type { StatusTone } from "@flit/ui/StatusBadge";
import type {
  EstadoSaneamiento,
  FuenteSaneamiento,
  HallazgoRenglon,
  MotivoNoElegible,
  RenglonSaneamiento,
  ResultadoRenglon,
} from "@/lib/saneamiento/api";

export interface EtiquetaSaneamiento {
  texto: string;
  tono: StatusTone;
}

/** Los cinco estados del contrato, en el orden de la bandeja del backoffice (filtros de PS-4). */
export const ESTADOS_SANEAMIENTO: readonly EstadoSaneamiento[] = ["SOLICITADO", "EN_GESTION", "SANEADO", "NO_PROCEDE", "ANULADO"];

const ETIQUETAS: Record<EstadoSaneamiento, EtiquetaSaneamiento> = {
  SOLICITADO: { texto: "Saneamiento solicitado", tono: "info" },
  EN_GESTION: { texto: "En saneamiento", tono: "warning" },
  SANEADO: { texto: "Saneado", tono: "success" },
  NO_PROCEDE: { texto: "Saneamiento no procede", tono: "neutral" },
  // D-06: solo el backoffice (PS-4 y PS-5) rotula los anulados; para la compañía un anulado es «sin saneamiento».
  ANULADO: { texto: "Anulado", tono: "neutral" },
};

/**
 * Etiqueta del estado. Sin saneamiento, o ANULADO fuera del backoffice, no hay etiqueta (null). Un estado desconocido
 * tampoco se pinta: nunca se muestra el código.
 */
export function etiquetaSaneamiento(
  estado: EstadoSaneamiento | null | undefined,
  opciones: { backoffice?: boolean } = {},
): EtiquetaSaneamiento | null {
  if (!estado || !Object.prototype.hasOwnProperty.call(ETIQUETAS, estado)) return null;
  if (estado === "ANULADO" && !opciones.backoffice) return null;
  return ETIQUETAS[estado];
}

export const ROTULO_FUENTE: Record<FuenteSaneamiento, string> = {
  SIMIT_VEHICULO: "SIMIT del vehículo",
  SIMIT_PROPIETARIO: "SIMIT del propietario",
  SIMIT_LOCATARIO: "SIMIT del locatario",
  IMPUESTO: "Impuesto vehicular",
  SOAT: "SOAT",
  RTM: "Revisión técnico-mecánica",
  OTRO: "Otro",
};

export const ROTULO_HALLAZGO: Record<HallazgoRenglon, string> = {
  DEUDA: "Con deuda",
  NO_VIGENTE: "No vigente",
  SIN_VERIFICAR: "Sin verificar",
};

export const ROTULO_RESULTADO: Record<ResultadoRenglon, string> = {
  PENDIENTE: "Pendiente",
  SANEADO: "Saneado",
  NO_PROCEDE: "No procede",
};

export const ROTULO_MOTIVO_NO_ELEGIBLE: Record<MotivoNoElegible, string> = {
  NO_TERMINADO: "Faltan consultas por terminar",
  YA_SOLICITADO: "Ya tiene un saneamiento",
};

/** Concepto de un renglón (nombre de la fila en PS-2 y PS-5): la fuente, o «Otro: {descripción}». */
export function rotuloConcepto(renglon: Pick<RenglonSaneamiento, "fuente" | "descripcion">): string {
  const fuente = ROTULO_FUENTE[renglon.fuente] ?? ROTULO_FUENTE.OTRO;
  const descripcion = renglon.descripcion?.trim();
  return renglon.fuente === "OTRO" && descripcion ? `${fuente}: ${descripcion}` : fuente;
}

/**
 * Nombre accesible del vehículo de una fila (UA-5, UA-10): «la placa ABC123» (o el VIN, o el identificador, como el tablero);
 * sin ninguno, «el vehículo sin placa». Nunca queda «… la placa » vacío ni un id interno.
 */
export const laPlaca = (identificador: string): string => (identificador ? `la placa ${identificador}` : "el vehículo sin placa");
/** «de la placa ABC123» o «del vehículo sin placa». */
export const deLaPlaca = (identificador: string): string => (identificador ? `de la placa ${identificador}` : "del vehículo sin placa");

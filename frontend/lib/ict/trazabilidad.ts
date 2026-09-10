// Vocabulario compartido de la Trazabilidad ICT (Feature #11814).
//
// Vive aparte del componente porque lo consumen la bandeja, el detalle y la exportación: si cada
// pantalla tradujera los estados por su cuenta, el XLSX que se reenvía por correo podría decir algo
// distinto de lo que el analista tenía en la pantalla.
import {
  Ban,
  BadgeCheck,
  CircleDashed,
  FileCheck2,
  RefreshCw,
  Search,
  TriangleAlert,
  type LucideIcon,
} from "lucide-react";
import { ESTADO_CHIP_STYLES, type EstadoChipStyle } from "@/lib/tramites/estados";

/** Los siete estados de `IctEstado`, en el orden en que ocurren. */
export const ESTADOS_ICT = [
  "recibido",
  "en_validacion_negocio",
  "en_validacion_externa",
  "procesado",
  "borrador_creado",
  "con_novedades",
  "anulado",
] as const;

export type EstadoIct = (typeof ESTADOS_ICT)[number];

export function esEstadoIct(value: string | null | undefined): value is EstadoIct {
  return !!value && (ESTADOS_ICT as readonly string[]).includes(value);
}

/**
 * Etiqueta, color e icono de cada estado.
 *
 * La paleta se toma de `ESTADO_CHIP_STYLES` (la misma de Trámites) y NO de los cinco tonos
 * semánticos de `StatusBadge`: con siete estados, esos cinco obligarían a que tres compartieran
 * color, y el contador de la tira dejaría de distinguirse de un vistazo, que es su única razón de
 * ser. Cada estado usa un estilo distinto; hay exactamente siete disponibles.
 *
 * Los iconos son de `lucide`. Nada de glifos sueltos (○ ✓ ✕): el navegador los pinta como emoji y
 * desentonan con el resto de la consola.
 */
export const ESTADO_ICT: Record<EstadoIct, { label: string; style: EstadoChipStyle; Icon: LucideIcon }> = {
  recibido: {
    label: "Recibido",
    style: ESTADO_CHIP_STYLES.borrador,
    Icon: CircleDashed,
  },
  en_validacion_negocio: {
    label: "Validando negocio",
    style: ESTADO_CHIP_STYLES.preparado,
    Icon: RefreshCw,
  },
  en_validacion_externa: {
    label: "Consultando fuentes",
    style: ESTADO_CHIP_STYLES.subsanacion,
    Icon: Search,
  },
  procesado: {
    label: "Procesado",
    style: ESTADO_CHIP_STYLES.entregado,
    Icon: FileCheck2,
  },
  borrador_creado: {
    label: "Borrador creado",
    style: ESTADO_CHIP_STYLES.aprobado,
    Icon: BadgeCheck,
  },
  con_novedades: {
    label: "Con novedades",
    style: ESTADO_CHIP_STYLES.rechazado,
    Icon: TriangleAlert,
  },
  anulado: {
    label: "Anulado",
    style: ESTADO_CHIP_STYLES.anulado,
    Icon: Ban,
  },
};

/** Etiqueta con reserva para un estado que la API empiece a emitir y el front no conozca todavía. */
export function estadoIctLabel(value: string | null | undefined): string {
  if (!value) return "—";
  if (esEstadoIct(value)) return ESTADO_ICT[value].label;
  const texto = value.replace(/_/g, " ");
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

/**
 * Convierte segundos a una duración legible: «4 min 25 s», «4 h 12 min», «2 d 3 h».
 *
 * Se muestran como mucho dos unidades. Un trámite atascado tres días no gana nada informando los
 * segundos, y la cifra larga hace que la columna deje de leerse de un vistazo.
 */
export function formatearDuracion(segundos: number | null | undefined): string {
  if (segundos === null || segundos === undefined) return "—";
  if (segundos < 0) return "—";
  if (segundos < 60) return `${segundos} s`;

  const minutos = Math.floor(segundos / 60);
  if (minutos < 60) {
    const resto = segundos % 60;
    return resto === 0 ? `${minutos} min` : `${minutos} min ${resto} s`;
  }

  const horas = Math.floor(minutos / 60);
  if (horas < 24) {
    const resto = minutos % 60;
    return resto === 0 ? `${horas} h` : `${horas} h ${resto} min`;
  }

  const dias = Math.floor(horas / 24);
  const resto = horas % 24;
  return resto === 0 ? `${dias} d` : `${dias} d ${resto} h`;
}

/** Igual que `formatearDuracion`, para las esperas que la bandeja recibe ya en minutos. */
export function formatearEspera(minutos: number | null | undefined): string {
  return minutos === null || minutos === undefined ? "—" : formatearDuracion(minutos * 60);
}

/**
 * Umbral a partir del cual una espera se pinta como aviso.
 *
 * Una hora, y no es arbitrario: la cadencia más lenta de los jobs del pipeline es de 45 segundos
 * (`IctJobOptions`), así que un trámite parado más de una hora no está esperando su turno, está
 * atascado.
 */
export const MINUTOS_ESPERA_ALTA = 60;

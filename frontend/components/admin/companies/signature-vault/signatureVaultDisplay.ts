import { formatFechaCalendario } from "@/lib/format/date";

// Helpers de presentación del Baúl de Firmas (HU #10644): etiquetas de estado,
// estilos del badge y formateo de fechas (YYYY-MM-DD → dd/mm/aaaa) sin dependencias.
import type { SignatureVaultEstado } from "@/lib/api/admin-signature-vault";

export const ESTADO_LABELS: Record<SignatureVaultEstado, string> = {
  activa: "Activa",
  revocada: "Revocada",
  vencida: "Vencida",
};

/** Colores del badge de estado (tokens FLIT): activa teal, revocada naranja, vencida ámbar. */
export const ESTADO_BADGE: Record<SignatureVaultEstado, { color: string; border: string; bg: string }> = {
  activa: { color: "#0a8f8b", border: "#8fdedb", bg: "rgba(0,219,213,0.12)" },
  revocada: { color: "#FF4E00", border: "#f0b49a", bg: "rgba(255,78,0,0.08)" },
  vencida: { color: "#8a6000", border: "#f0d38e", bg: "rgba(249,172,0,0.10)" },
};

/** Formatea una fecha ISO / YYYY-MM-DD a dd/mm/aaaa en es-CO, robusto ante valores vacíos. */
/**
 * Vigencia del baúl: es una fecha de CALENDARIO, no un instante (excepción RN-08 de la
 * Épica #12552). Delega en el formateador compartido, que ya conserva el día tal cual y no
 * lo convierte de zona — antes esto se lograba aquí formateando en UTC a mano.
 */
export function formatDate(value: string | null | undefined): string {
  return formatFechaCalendario(value, "—");
}

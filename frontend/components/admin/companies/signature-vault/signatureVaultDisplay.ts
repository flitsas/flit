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
  activa: { color: "var(--flit-success-ink)", border: "var(--badge-success-border)", bg: "var(--badge-success-bg)" },
  revocada: { color: "var(--destructive)", border: "var(--badge-danger-border)", bg: "var(--badge-danger-bg)" },
  vencida: { color: "var(--badge-warning-fg)", border: "var(--badge-warning-border)", bg: "var(--badge-warning-bg)" },
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

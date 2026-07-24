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
export function formatDate(value: string | null | undefined): string {
  if (!value) return "—";
  // Fechas puras (YYYY-MM-DD) se parsean como UTC para evitar corrimientos por zona horaria.
  const iso = /^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T00:00:00Z` : value;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return value;
  return d.toLocaleDateString("es-CO", { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "UTC" });
}

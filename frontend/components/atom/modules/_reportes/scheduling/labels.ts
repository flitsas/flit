// Etiquetas en español + validación client-side espejo del backend (Reportes 2.0, HU-D §4.7).
import type {
  AlertMetric,
  AlertOperator,
  ReportType,
  ScheduleFormat,
  ScheduleFrequency,
} from "@/lib/api/analytics-scheduling";

export const REPORT_TYPE_LABELS: Record<ReportType, string> = {
  resumen: "Resumen general",
  operacion: "Operación / Trámites",
  ot: "Organismo de Tránsito",
  uso: "Uso del aplicativo",
  productividad: "Productividad",
  // Solo se crea desde "Programar este informe" en una consulta guardada — no aparece en el
  // selector de "Nuevo informe programado" (ver ScheduleForm), pero sí en el listado.
  consulta: "Consulta personalizada",
  // Alcance Organismo de Tránsito (Reportes 2.0, HU-D, tercera ola): único tipo que se ofrece
  // en el panel de "Programación y alertas" del propio organismo (ver SchedulesSection).
  ot_operativo: "Mi organismo de tránsito",
};

export const FREQUENCY_LABELS: Record<ScheduleFrequency, string> = {
  daily: "Diaria",
  weekly: "Semanal",
  monthly: "Mensual",
};

export const FORMAT_LABELS: Record<ScheduleFormat, string> = {
  excel: "Excel",
  pdf: "PDF",
};

export const DAY_OF_WEEK_LABELS = [
  "Domingo",
  "Lunes",
  "Martes",
  "Miércoles",
  "Jueves",
  "Viernes",
  "Sábado",
] as const;

export const METRIC_LABELS: Record<AlertMetric, string> = {
  rejection_rate_pct: "Tasa de rechazo (%)",
  stuck_count: "Trámites atascados",
  external_api_errors: "Errores de integraciones externas",
  pending_identity_validations: "Validaciones de identidad pendientes",
  ict_stuck_in_validation: "ICT · Atascados en validación",
  ict_novelty_rate_pct: "ICT · Tasa de novedades (%)",
  ict_webhook_delivery_failures: "ICT · Fallos de entrega de webhook",
  ict_jobs_out_of_sla: "ICT · Jobs fuera de SLA",
  ot_rejection_rate_pct: "Tasa de rechazo del organismo (%)",
  ot_stuck_count: "Trámites atascados en el organismo",
};

export const METRIC_DESCRIPTIONS: Record<AlertMetric, string> = {
  rejection_rate_pct:
    "Porcentaje de trámites rechazados sobre los decididos (aprobados + rechazados) dentro de la ventana.",
  stuck_count:
    "Trámites en estado no final sin cambios hace más de 7 días.",
  external_api_errors:
    "Cantidad de llamadas con error a integraciones externas (OT) dentro de la ventana.",
  pending_identity_validations:
    "Validaciones biométricas cuyo estado actual es pendiente o en proceso.",
  ict_stuck_in_validation:
    "Pre-trámites ICT aún en validación (sin materializar) creados hace más de la ventana.",
  ict_novelty_rate_pct:
    "Porcentaje de pre-trámites ICT que cayeron en novedad sobre los creados en la ventana.",
  ict_webhook_delivery_failures:
    "Webhooks ICT entregados con respuesta no satisfactoria dentro de la ventana.",
  ict_jobs_out_of_sla:
    "Corridas de los jobs ICT que incumplieron su SLA (error o duración excedida) en la ventana.",
  ot_rejection_rate_pct:
    "Porcentaje de trámites rechazados sobre los decididos por el organismo dentro de la ventana.",
  ot_stuck_count:
    "Trámites pendientes de revisión en el organismo hace más de 7 días.",
};

export const OPERATOR_LABELS: Record<AlertOperator, string> = {
  gt: "Mayor que (>)",
  gte: "Mayor o igual que (≥)",
  lt: "Menor que (<)",
  lte: "Menor o igual que (≤)",
};

const EMAIL_REGEX = /^[^@\s]+@[^@\s]+\.[^@\s]{2,}$/;

export const MAX_RECIPIENTS = 10;

export function isValidEmail(value: string): boolean {
  return EMAIL_REGEX.test(value.trim());
}

/** Espejo de SchedulingValidation.ValidateRecipients (backend): 1..10 correos válidos. */
export function validateRecipients(recipients: string[]): string | null {
  if (recipients.length === 0) return "Debe indicar al menos un destinatario de correo.";
  if (recipients.length > MAX_RECIPIENTS)
    return `No puede indicar más de ${MAX_RECIPIENTS} destinatarios.`;
  const invalid = recipients.find((r) => !isValidEmail(r));
  if (invalid) return `El correo '${invalid}' no es una dirección válida.`;
  return null;
}

export function validateName(name: string): string | null {
  if (!name.trim()) return "El nombre es obligatorio.";
  if (name.trim().length > 120) return "El nombre no puede superar los 120 caracteres.";
  return null;
}

export function formatDateTime(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return d.toLocaleString("es-CO", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

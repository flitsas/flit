import type { StatusTone } from "@/components/atom/StatusBadge";
import type { LogQxBandejaEstado } from "@/lib/api/admin-log-qx";

/**
 * Traducción de todo lo que el LOG QX muestra (Feature #11784). Vive en un solo sitio porque lo
 * comparten la bandeja, la línea de hitos y el log completo: si cada pantalla tradujera por su
 * cuenta, el mismo código acabaría con tres nombres distintos.
 */

/**
 * Códigos de negocio de QUIPUX. No son códigos HTTP — la pantalla anterior los rotulaba como
 * "HTTP 81", que es incorrecto y desorienta a quien hace soporte: 81 es "almacenado correctamente"
 * en el contrato de Quipux, no un estado de la respuesta HTTP.
 */
export const CODIGO_QX: Record<number, string> = {
  81: "Almacenado correctamente",
  72: "Tiempo de espera agotado",
  76: "Error interno de la secretaría",
};

/** Estado que devuelve Quipux en `estadoTramite.codigo`. */
export const ESTADO_TRAMITE_QX: Record<number, string> = {
  1: "En trámite (sin cambios)",
  2: "Aprobado",
  3: "Rechazado",
};

/** Worker que originó el evento. */
export const ORIGEN: Record<string, string> = {
  quipux_register: "Radicación",
  quipux_status_poll: "Consulta de estado",
  manual: "Acción manual",
};

/** Etapas conocidas de la bitácora; las desconocidas se muestran tal cual. */
export const ETAPA: Record<string, string> = {
  claimed: "Tomado de la cola",
  consolidado_generado: "Consolidado generado",
  s3_subido: "Documento subido a Quipux",
  registro_enviado: "Radicación enviada",
  registro_respuesta: "Radicado en Quipux",
  registro_error: "Error al radicar",
  consulta_enviada: "Consulta de estado enviada",
  consulta_respuesta: "Respuesta de consulta",
  consulta_error: "Error al consultar",
  aprobado: "Aprobado por la secretaría",
  rechazado: "Rechazado por la secretaría",
  dead_letter: "Enviado a dead-letter",
  reintento_manual: "Reintento manual",
  cancelado_manual: "Cancelado manualmente",
};

/** Resultado de un evento. */
export const RESULTADO: Record<string, { label: string; tone: StatusTone }> = {
  ok: { label: "OK", tone: "success" },
  error_transitorio: { label: "Error transitorio", tone: "warning" },
  error_definitivo: { label: "Error definitivo", tone: "danger" },
  omitido: { label: "Omitido", tone: "neutral" },
};

/** Estados de la bandeja, en el orden en que se muestran los contadores. */
export const ESTADO_BANDEJA: Record<LogQxBandejaEstado, { label: string; tone: StatusTone }> = {
  sin_radicar: { label: "Sin radicar", tone: "neutral" },
  pendiente: { label: "Pendiente", tone: "neutral" },
  radicado: { label: "Radicado", tone: "info" },
  en_tramite: { label: "En trámite", tone: "warning" },
  aprobado: { label: "Aprobado", tone: "success" },
  rechazado: { label: "Rechazado", tone: "danger" },
  fallido: { label: "Fallido", tone: "danger" },
};

export const ESTADOS_BANDEJA: LogQxBandejaEstado[] = [
  "sin_radicar", "pendiente", "radicado", "en_tramite", "aprobado", "rechazado", "fallido",
];

export function etapa(stage: string): string {
  return ETAPA[stage] ?? stage;
}

export function resultado(outcome: string): { label: string; tone: StatusTone } {
  return RESULTADO[outcome] ?? { label: outcome, tone: "neutral" };
}

export function origen(value: string | null): string {
  if (!value) return "—";
  return ORIGEN[value] ?? value;
}

/** Código con su significado. Nunca se rotula como HTTP. */
export function codigoQx(code: number | null | undefined): string {
  if (code == null) return "—";
  const texto = CODIGO_QX[code];
  return texto ? `${code} · ${texto}` : String(code);
}

export function estadoTramiteQx(code: number | null | undefined): string {
  if (code == null) return "—";
  return ESTADO_TRAMITE_QX[code] ?? String(code);
}

/**
 * Nombre de la secretaría listo para incrustar en una frase.
 *
 * Los nombres del catálogo ya vienen con su tipo delante («SECRETARIA DISTRITAL DE MOVILIDAD DE
 * BOGOTA», «SECRETARÍA DE TRÁNSITO DE IBAGUÉ»), así que anteponer «la Secretaría de» produce
 * «la Secretaría de SECRETARIA DISTRITAL…». Se antepone solo cuando el nombre NO lo trae.
 */
export function secretaria(nombre: string): string {
  const limpio = nombre.trim();
  const empiezaConTipo = /^secretar[ií]a\b/i.test(limpio);
  return empiezaConTipo ? `la ${limpio}` : `la Secretaría de ${limpio}`;
}

/**
 * Igual que {@link secretaria} pero en inicio de oración, con la mayúscula donde toca.
 */
export function Secretaria(nombre: string): string {
  const frase = secretaria(nombre);
  return frase.charAt(0).toUpperCase() + frase.slice(1);
}

/**
 * Fragmento « el {fecha}» para incrustar en una frase, o cadena vacía si no hay fecha.
 *
 * Existe para no escribir «lo aprobó el —.»: el guión del formateador sirve en una tabla, donde
 * marca una celda sin dato, pero en mitad de una oración la rompe.
 */
export function fragmentoFecha(iso: string | null | undefined): string {
  if (!iso) return "";
  const formateada = formatFecha(iso);
  return formateada === "—" ? "" : ` el ${formateada}`;
}

export function formatFecha(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short" }).format(d);
}

export function formatDuracion(ms: number | null | undefined): string | null {
  if (ms == null) return null;
  return ms >= 1000 ? `${(ms / 1000).toFixed(2)} s` : `${ms} ms`;
}

/**
 * Espera en días y horas. Se recibe ya calculada del servidor, así que no depende del reloj ni de
 * la zona horaria del navegador.
 */
export function formatEspera(horas: number | null | undefined): string | null {
  if (horas == null) return null;
  if (horas < 1) return "menos de 1 h";
  if (horas < 24) return `${Math.floor(horas)} h`;
  const dias = Math.floor(horas / 24);
  const resto = Math.floor(horas % 24);
  return resto === 0 ? `${dias} d` : `${dias} d ${resto} h`;
}

/**
 * Umbral a partir del cual una espera se destaca. Dos días es lo que la operación considera
 * razonable para que una secretaría responda; por encima, soporte quiere verlo de un vistazo.
 */
export const UMBRAL_ESPERA_HORAS = 48;

export function esperaEsAlta(horas: number | null | undefined): boolean {
  return horas != null && horas >= UMBRAL_ESPERA_HORAS;
}

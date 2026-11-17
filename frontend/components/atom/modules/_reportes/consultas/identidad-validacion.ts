// Validación de identidad por actor: la ÚNICA tabla de etiquetas, compartida por la pantalla
// (Consulta personalizada y Reporte detallado) y por el Excel y el CSV. Si una etiqueta cambia, cambia
// en los tres sitios a la vez.
//
// Regla de producto: el usuario nunca ve cómo se validó. Aquí no hay fuente, proveedor ni modalidad;
// los estados se nombran por lo que significan para quien opera el trámite.

import type {
  ActorValidacionDto,
  EstadoValidacion,
  MaxActoresPorRol,
  RolActor,
} from "@/lib/api/company-queries";

export const ESTADO_VALIDACION_LABEL: Record<EstadoValidacion, string> = {
  aprobado: "Aprobado",
  rechazado: "Rechazado",
  en_proceso: "En proceso",
  enviado: "Enviado",
  pendiente_envio: "Pendiente de envío",
  error_envio: "Error de envío",
  expirado: "Expirado",
  esperando_captura: "Esperando captura del cliente",
  pendiente_revision: "Pendiente de revisión",
  sin_validacion: "Sin validar",
};

/** Color semántico por estado, con los tokens de estado del rediseño y, donde aún no existen (staging), el badge equivalente. */
export const ESTADO_VALIDACION_COLOR: Record<EstadoValidacion, string> = {
  aprobado: "var(--estado-aprobado-fg, var(--badge-success-fg))",
  rechazado: "var(--estado-rechazado-fg, var(--badge-danger-fg))",
  en_proceso: "var(--estado-preparado-fg, var(--badge-info-fg))",
  enviado: "var(--estado-entregado-fg, var(--badge-info-fg))",
  pendiente_envio: "var(--estado-borrador-fg, var(--badge-pending-fg))",
  error_envio: "var(--estado-anulado-fg, var(--badge-danger-fg))",
  expirado: "var(--estado-revocado-fg, var(--badge-warning-fg))",
  esperando_captura: "var(--estado-asignado-fg, var(--badge-pending-fg))",
  pendiente_revision: "var(--estado-subsanacion-fg, var(--badge-warning-fg))",
  sin_validacion: "var(--border)",
};

/**
 * Fondo y color de TEXTO del chip. Los estados con color propio usan su tinta sobre un fondo tenue
 * de la misma; «Sin validar» (y cualquier estado desconocido) usa el par neutro del rediseño, porque
 * `--border` es un color de línea y como texto sobre blanco es casi invisible.
 */
export function estiloChipValidacion(estado: string): { background: string; color: string } {
  if (!(estado in ESTADO_VALIDACION_COLOR) || estado === "sin_validacion") {
    return { background: "var(--badge-neutral-bg)", color: "var(--badge-neutral-fg)" };
  }
  const color = ESTADO_VALIDACION_COLOR[estado as EstadoValidacion];
  return { background: `color-mix(in srgb, ${color} 10%, transparent)`, color };
}

/** Un estado que el front no conoce (backend más nuevo) se ve como «Sin validar», nunca crudo. */
export function etiquetaValidacion(estado: string): string {
  return ESTADO_VALIDACION_LABEL[estado as EstadoValidacion] ?? ESTADO_VALIDACION_LABEL.sin_validacion;
}

// Defensa extra: la normalización real la hace el servidor, pero si algún texto con el nombre de la
// fuente se colara, no debe llegar al usuario ni al archivo.
const MOTIVO_PROHIBIDO = /kyverum|manual|proveedor|simulad|migraci[oó]n|mock|webhook|scorer|fuente/i;

/** Motivo apto para mostrar, o `null`. */
export function motivoVisible(motivo: string | null | undefined): string | null {
  const m = motivo?.trim();
  if (!m) return null;
  return MOTIVO_PROHIBIDO.test(m) ? "Validación rechazada" : m;
}

/** «Tipo y número de documento» en una sola cadena corta. */
export function documentoActor(a: ActorValidacionDto): string {
  return [a.tipoDocumento, a.documento].filter(Boolean).join(" ").trim();
}

/** Los dos grupos de columnas: el propietario (matrícula, OTROS) va con los compradores. */
export type GrupoPersonas = "comprador" | "vendedor";

const ROLES_DEL_GRUPO: Record<GrupoPersonas, RolActor[]> = {
  comprador: ["comprador", "propietario"],
  vendedor: ["vendedor"],
};

/**
 * Los actores de un grupo, en el orden de la pantalla y del archivo: por rol (comprador antes que
 * propietario) y dentro del rol por ordinal. La línea i de las cuatro columnas del grupo es el
 * mismo actor.
 */
export function actoresDelGrupo(
  actores: ActorValidacionDto[] | null | undefined,
  grupo: GrupoPersonas,
): ActorValidacionDto[] {
  const roles = ROLES_DEL_GRUPO[grupo];
  return (actores ?? [])
    .filter((a) => roles.includes(a.rol))
    .sort((a, b) => roles.indexOf(a.rol) - roles.indexOf(b.rol) || a.ordinal - b.ordinal);
}

/**
 * Cuántos bloques de persona lleva un grupo en el archivo: el máximo del servidor (sobre todo el
 * filtro), con un mínimo de 1 porque la columna «Comprador»/«Vendedor» siempre existió. El grupo
 * comprador cuenta también los propietarios.
 */
export function bloquesDelGrupo(max: MaxActoresPorRol | undefined, grupo: GrupoPersonas): number {
  if (!max) return 1;
  // Cada campo con `?? 0`: un objeto incompleto daría NaN y la columna desaparecería del archivo.
  const n =
    grupo === "comprador"
      ? Math.max(max.comprador ?? 0, max.propietario ?? 0)
      : (max.vendedor ?? 0);
  return Math.max(1, n);
}

/**
 * Encabezado plano de un bloque: «Comprador», «Validación comprador», «Motivo rechazo vendedor 2»…
 * El primer bloque va sin número; desde el segundo, con él.
 */
export function encabezadoBloque(base: string | null, grupo: GrupoPersonas, n: number): string {
  const sufijo = n > 1 ? ` ${n}` : "";
  return base === null
    ? `${grupo === "comprador" ? "Comprador" : "Vendedor"}${sufijo}`
    : `${base} ${grupo}${sufijo}`;
}

/** Una línea por actor del grupo para el valor de respaldo de una columna; «—» si no hay ninguno. */
export function textoDelGrupo(
  actores: ActorValidacionDto[] | null | undefined,
  grupo: GrupoPersonas,
  f: (a: ActorValidacionDto) => string,
): string {
  const lista = actoresDelGrupo(actores, grupo);
  return lista.length === 0 ? "—" : lista.map(f).join("\n");
}

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

/** Color semántico por estado, con los tokens de estado del rediseño (cero hex sueltos). */
export const ESTADO_VALIDACION_COLOR: Record<EstadoValidacion, string> = {
  aprobado: "var(--estado-aprobado-fg)",
  rechazado: "var(--estado-rechazado-fg)",
  en_proceso: "var(--estado-preparado-fg)",
  enviado: "var(--estado-entregado-fg)",
  pendiente_envio: "var(--estado-borrador-fg)",
  error_envio: "var(--estado-anulado-fg)",
  expirado: "var(--estado-revocado-fg)",
  esperando_captura: "var(--estado-asignado-fg)",
  pendiente_revision: "var(--estado-subsanacion-fg)",
  sin_validacion: "var(--border)",
};

export const ROLES_ACTOR: RolActor[] = ["comprador", "vendedor", "propietario"];

export const ROL_LABEL: Record<RolActor, string> = {
  comprador: "Comprador",
  vendedor: "Vendedor",
  propietario: "Propietario",
};

/** Un estado que el front no conoce (backend más nuevo) se ve como «Sin validar», nunca crudo. */
export function etiquetaValidacion(estado: string): string {
  return ESTADO_VALIDACION_LABEL[estado as EstadoValidacion] ?? ESTADO_VALIDACION_LABEL.sin_validacion;
}

export function colorValidacion(estado: string): string {
  return (
    ESTADO_VALIDACION_COLOR[estado as EstadoValidacion] ?? ESTADO_VALIDACION_COLOR.sin_validacion
  );
}

// Defensa extra: la normalización real la hace el servidor, pero si algún texto con el nombre de la
// fuente se colara, no debe llegar al usuario ni al archivo.
const MOTIVO_PROHIBIDO = /kyverum|manual|proveedor|simulad|migraci[oó]n|mock|webhook|scorer/i;

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

/** Actores en el orden de la pantalla: rol (comprador, vendedor, propietario) y luego ordinal. */
export function actoresOrdenados(actores: ActorValidacionDto[] | undefined): ActorValidacionDto[] {
  const peso = (r: string) => {
    const i = ROLES_ACTOR.indexOf(r as RolActor);
    return i < 0 ? ROLES_ACTOR.length : i;
  };
  return [...(actores ?? [])].sort((a, b) => peso(a.rol) - peso(b.rol) || a.ordinal - b.ordinal);
}

/** El actor n-ésimo (1-based) de un rol, por ordinal. */
export function actorDeRol(
  actores: ActorValidacionDto[] | undefined,
  rol: RolActor,
  n: number,
): ActorValidacionDto | undefined {
  return actoresOrdenados(actores).filter((a) => a.rol === rol)[n - 1];
}

export const SIN_ACTORES: MaxActoresPorRol = { comprador: 0, vendedor: 0, propietario: 0 };

/**
 * Encabezado plano de una columna de actor en el archivo: «Comprador», «Documento comprador»,
 * «Validación vendedor 2»… El primer actor de un rol va sin número; desde el segundo, con él.
 */
export function encabezadoActor(base: string | null, rol: RolActor, n: number): string {
  const sufijo = n > 1 ? ` ${n}` : "";
  return base === null ? `${ROL_LABEL[rol]}${sufijo}` : `${base} ${rol}${sufijo}`;
}

/** Texto de varias líneas (una por actor) para el valor de respaldo de una columna de pantalla. */
export function textoPorActor(
  actores: ActorValidacionDto[] | undefined,
  f: (a: ActorValidacionDto) => string,
): string {
  const lista = actoresOrdenados(actores);
  return lista.length === 0 ? "—" : lista.map(f).join("\n");
}

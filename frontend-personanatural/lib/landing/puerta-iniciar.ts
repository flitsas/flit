// Mensajes y reglas de organismo de la puerta «Iniciar». El catálogo de tipos de trámite ya no se fija aquí: lo configura la plataforma
// por organismo y llega de GET /tipos-tramite (portal web y punto por igual).

export const MENSAJE_PLACA_NO_ENCONTRADA = "No encontramos un vehículo con esa placa.";
export const MENSAJE_RUNT_PUERTA = "No pudimos consultar el RUNT. Inténtalo de nuevo.";
export const MENSAJE_ORGANISMO_DESCONOCIDO = "No podemos radicar el trámite porque la consulta no trajo el organismo de tránsito donde está matriculado este vehículo.";

/** Aviso del modal cuando el organismo que reporta el RUNT no está habilitado en el portal. */
export function mensajeOrganismoNoDisponible(organismo: string): string {
  return `No podemos radicar el trámite. Este vehículo está matriculado en ${organismo} y ese organismo todavía no recibe trámites por este portal.`;
}

function normalizar(valor: string | null | undefined): string {
  return (valor ?? "")
    .normalize("NFD")
    .replace(/\p{M}/gu, "")
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, " ")
    .trim();
}

function coincide(objetivo: string, candidato: string | undefined): boolean {
  const otro = normalizar(candidato);
  if (!otro) return false;
  if (otro === objetivo) return true;
  const corto = otro.length < objetivo.length ? otro : objetivo;
  const largo = corto === otro ? objetivo : otro;
  return corto.length >= 6 && largo.includes(corto);
}

/** El organismo del RUNT está entre los que el portal tiene habilitados. */
export function organismoDelRuntHabilitado(nombreRunt: string | null | undefined, organismos: readonly { nombre: string; municipio?: string }[]): boolean {
  const objetivo = normalizar(nombreRunt);
  if (!objetivo) return false;
  return organismos.some((o) => coincide(objetivo, o.nombre) || coincide(objetivo, o.municipio));
}

/** El habilitado que coincide con el nombre que trajo el RUNT, para guardarlo en el borrador. */
export function organismoHabilitadoDelRunt<T extends { nombre: string; municipio?: string }>(
  nombreRunt: string | null | undefined,
  organismos: readonly T[],
): T | null {
  const objetivo = normalizar(nombreRunt);
  if (!objetivo) return null;
  return organismos.find((o) => coincide(objetivo, o.nombre) || coincide(objetivo, o.municipio)) ?? null;
}

/**
 * Mensaje para quedarse en el modal, o null si se puede abrir el trámite.
 * Sin organismo en la consulta tampoco se sigue: la persona no elige otro. Si el tipo deja el organismo en manos del operador
 * (`loEligeOperador`), el del RUNT no decide nada: nunca se rechaza y el organismo se elige en el trámite.
 */
export function mensajeDeOrganismo(
  nombreRunt: string | null | undefined,
  organismos: readonly { nombre: string; municipio?: string }[],
  loEligeOperador = false,
): string | null {
  if (loEligeOperador) return null;
  const nombre = nombreRunt?.trim();
  if (!nombre) return MENSAJE_ORGANISMO_DESCONOCIDO;
  return organismoDelRuntHabilitado(nombre, organismos) ? null : mensajeOrganismoNoDisponible(nombre);
}

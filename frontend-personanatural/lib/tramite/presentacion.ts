import type { Solicitud } from "@/lib/api/cliente";

/** Nombres y archivos que el ciudadano ya vio, para la confirmación. Tras enviar el servicio no los vuelve a entregar. */
interface Recuerdo {
  actores: Map<string, string>;
  archivos: Map<string, string>;
}

const porSolicitud = new Map<string, Recuerdo>();

function de(id: string): Recuerdo {
  const ya = porSolicitud.get(id);
  if (ya) return ya;
  const nuevo = { actores: new Map<string, string>(), archivos: new Map<string, string>() };
  porSolicitud.set(id, nuevo);
  return nuevo;
}

const llaveActor = (rol: string, ordinal: number) => `${rol}:${ordinal}`;

export function recordarActor(solicitudId: string, rol: string, ordinal: number, nombre: string): void {
  const limpio = nombre.trim();
  if (!limpio) return;
  de(solicitudId).actores.set(llaveActor(rol, ordinal), limpio);
}

export function recordarArchivo(solicitudId: string, id: string, nombre: string, tipo: string): void {
  const limpio = nombre.trim();
  if (!limpio || limpio === tipo) return;
  de(solicitudId).archivos.set(id, limpio);
}

/** Guarda lo que la pantalla todavía tiene, antes de que un refresco lo borre. */
export function recordarPresentacion(solicitud: Pick<Solicitud, "solicitudId" | "actores" | "adjuntos">): void {
  for (const actor of solicitud.actores) recordarActor(solicitud.solicitudId, actor.rol, actor.ordinal, actor.nombre ?? "");
  for (const archivo of solicitud.adjuntos) recordarArchivo(solicitud.solicitudId, archivo.id, archivo.nombreArchivo, archivo.tipo);
}

export function nombreRecordado(solicitudId: string, rol: string, ordinal: number): string | undefined {
  return porSolicitud.get(solicitudId)?.actores.get(llaveActor(rol, ordinal));
}

export function archivoRecordado(solicitudId: string, id: string): string | undefined {
  return porSolicitud.get(solicitudId)?.archivos.get(id);
}

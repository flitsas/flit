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

/** Datos del vehículo que la consulta del diagnóstico ya trajo (sin propietario): el Resumen los reutiliza en vez de consultar de nuevo. */
export interface VehiculoRecordado {
  marca?: string | null;
  linea?: string | null;
  modeloAno?: string | null;
  clase?: string | null;
  color?: string | null;
  tipoServicio?: string | null;
}

const vehiculos = new Map<string, VehiculoRecordado>();

/** Guarda solo los seis datos del vehículo que muestra el Resumen; nada del propietario. Solo en memoria. */
export function recordarVehiculo(solicitudId: string, v: (VehiculoRecordado & { existe?: boolean }) | null | undefined): void {
  if (!v || v.existe === false) return;
  vehiculos.set(solicitudId, { marca: v.marca, linea: v.linea, modeloAno: v.modeloAno, clase: v.clase, color: v.color, tipoServicio: v.tipoServicio });
}

export function vehiculoRecordado(solicitudId: string): VehiculoRecordado | undefined {
  return vehiculos.get(solicitudId);
}

/** Texto cuando el servicio solo conoce el código del documento y no el nombre del archivo que subió la persona. */
export const ARCHIVO_CARGADO = "Archivo cargado";

/**
 * Nombre del archivo de un adjunto para la tarjeta del documento. Al reabrir un borrador el servicio puede devolver el código del
 * tipo (p. ej. «prenda_levantamiento») en lugar del nombre: eso nunca se muestra como si fuera el archivo.
 */
export function nombreVisibleDeAdjunto(adjunto: { id: string; tipo: string; nombreArchivo: string }, solicitudId?: string): string {
  const real = adjunto.nombreArchivo.trim();
  if (real && real !== adjunto.tipo) return real;
  return (solicitudId ? archivoRecordado(solicitudId, adjunto.id) : undefined) ?? ARCHIVO_CARGADO;
}

/** Vacía la memoria de la sesión (nombres, archivos y vehículo). Lo usan las pruebas para no arrastrar estado entre casos. */
export function olvidarPresentacion(): void {
  porSolicitud.clear();
  vehiculos.clear();
}

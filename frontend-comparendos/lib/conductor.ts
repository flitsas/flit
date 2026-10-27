// Lógica pura de la asignación del conductor (HU #13481, §7.4).
import { esCorreo } from "@/lib/correos";

export const DECLARACION_LEY_1581 =
  "Declaro que la empresa cuenta con la autorización del titular para el tratamiento de sus datos personales (Ley 1581 de 2012).";

export interface FormConductor {
  tipoDocumento: string;
  numeroDocumento: string;
  nombres: string;
  correo: string;
  telefono: string;
  direccion: string;
  ciudad: string;
  sucursal: string;
  tipoContrato: string;
  motivo: string;
  declaracion: boolean;
}

/** Lo que falta antes de poder guardar; vacío = listo. */
export function erroresAsignacion(f: FormConductor, reemplaza: boolean): string[] {
  const errores: string[] = [];
  if (!f.numeroDocumento.trim() || f.numeroDocumento.trim().length > 20) errores.push("Escribe el número de documento (hasta 20 caracteres).");
  if (!f.nombres.trim()) errores.push("Escribe el nombre del conductor.");
  if (f.correo.trim() && !esCorreo(f.correo)) errores.push("El correo no es válido.");
  if (reemplaza && !f.motivo.trim()) errores.push("Escribe el motivo del cambio.");
  if (!f.declaracion) errores.push("Marca la declaración de tratamiento de datos.");
  return errores;
}

const vacioANulo = (v: string) => (v.trim() ? v.trim() : null);

/** Cuerpo de PUT /comparendos/{id}/contraventor. */
export function armarAsignacion(f: FormConductor) {
  return {
    tipoDocumento: f.tipoDocumento,
    numeroDocumento: f.numeroDocumento.trim(),
    nombres: f.nombres.trim(),
    correo: vacioANulo(f.correo),
    telefono: vacioANulo(f.telefono),
    direccion: vacioANulo(f.direccion),
    ciudad: vacioANulo(f.ciudad),
    sucursal: vacioANulo(f.sucursal),
    tipoContrato: vacioANulo(f.tipoContrato),
    motivo: vacioANulo(f.motivo),
    declaracionTratamientoDatos: f.declaracion,
  };
}

/** Respuesta de «Probar conexión» con la API de contratos (HU #13843, solo SuperAdmin). */
export interface PruebaContratos {
  conectado: boolean;
  motivo: string | null;
  httpStatus: number | null;
  duracionMs: number;
  contactoDesviado: boolean;
  correoDesvio: string | null;
  simulada: boolean;
}

/** Título, tono y detalle del resultado de la prueba. */
export function resumenPrueba(p: PruebaContratos): { titulo: string; tono: "success" | "warning" | "error"; detalle: string[] } {
  if (p.conectado) {
    return {
      titulo: `Conectado: Renting respondió el inicio de sesión en ${p.duracionMs.toLocaleString("es-CO")} ms`,
      tono: "success",
      detalle: [
        p.contactoDesviado
          ? `El contacto de los conductores se desvía a ${p.correoDesvio ?? "el buzón de pruebas"}: ningún aviso le llega al cliente de Renting.`
          : "Contacto real encendido: los avisos le llegan al conductor (solo debe pasar en producción).",
        "La prueba no consulta ninguna placa.",
      ],
    };
  }
  return {
    titulo: p.simulada ? "No conectado: este ambiente usa respuestas simuladas" : "No conectado",
    tono: p.simulada ? "warning" : "error",
    detalle: [p.motivo ?? "Sin motivo.", ...(p.httpStatus ? [`Código de respuesta: ${p.httpStatus}.`] : [])],
  };
}

// Lógica pura del registro de pagos (HU #13494, §6.19). La API vuelve a validar todo (HU #13493).
import type { Etiqueta } from "@/lib/etiquetas";

export const TIPOS_PAGO = [
  { valor: "CC", texto: "CC: cambio de contraventor" },
  { valor: "PC", texto: "PC: pagó el contraventor" },
  { valor: "BC", texto: "BC: banco / empresa" },
] as const;

export type TipoPago = (typeof TIPOS_PAGO)[number]["valor"];

export interface FormPago {
  tipo: TipoPago | "";
  valor: string;
  fecha: string;
  comprobante: File | null;
}

export const MAXIMO_ARCHIVO = 10 * 1024 * 1024;
const TIPOS_COMPROBANTE = ["application/pdf", "image/jpeg", "image/png"];

/** «$ 302.050» o «302050» → 302050; sin dígitos → null. Los pesos no llevan decimales. */
export function leerValor(texto: string): number | null {
  const digitos = texto.replace(/\D/g, "");
  return digitos ? Number(digitos) : null;
}

/** Lo que falta antes de registrar el pago; vacío = listo. `hoy` en aaaa-mm-dd (hora de Colombia). */
export function erroresPago(f: FormPago, hoy: string): string[] {
  const errores: string[] = [];
  if (!f.tipo) errores.push("Elige el tipo de pago.");
  const valor = leerValor(f.valor);
  if (!valor) errores.push("Escribe el valor pagado.");
  if (!f.fecha) errores.push("Escribe la fecha de pago.");
  else if (f.fecha > hoy) errores.push("La fecha de pago no puede ser futura.");
  if (f.comprobante) {
    if (!TIPOS_COMPROBANTE.includes(f.comprobante.type)) errores.push("El comprobante debe ser PDF, JPG o PNG.");
    if (f.comprobante.size > MAXIMO_ARCHIVO) errores.push("El comprobante supera 10 MB.");
  }
  return errores;
}

/** Cuerpo multipart de POST /comparendos/{id}/pago. */
export function armarPago(f: FormPago): FormData {
  const form = new FormData();
  form.append("tipo", f.tipo);
  form.append("valor", String(leerValor(f.valor) ?? 0));
  form.append("fecha", f.fecha);
  if (f.comprobante) form.append("comprobante", f.comprobante);
  return form;
}

/** Lo pagado menos el valor del comparendo (RN-163: puede diferir). null si falta alguno de los dos. */
export function diferencia(valorPagado: number | null, valorComparendo: number | null): number | null {
  if (valorPagado === null || valorComparendo === null) return null;
  return valorPagado - valorComparendo;
}

export const ESTADO_CARGA: Record<string, Etiqueta> = {
  Validando: { texto: "Validando", tono: "info" },
  Procesada: { texto: "Procesada", tono: "success" },
  ProcesadaConErrores: { texto: "Con errores", tono: "warning" },
  Rechazada: { texto: "Rechazada", tono: "danger" },
};

/** Lo que traía el archivo en una fila de la carga (la API guarda los datos leídos como JSON). */
export interface DatosFilaPago {
  tipoPago: string | null;
  placa: string | null;
  numeroComparendo: string | null;
  valor: number | null;
  fechaPago: string | null;
}

const SIN_DATOS: DatosFilaPago = { tipoPago: null, placa: null, numeroComparendo: null, valor: null, fechaPago: null };

export function leerDatosFila(json: string): DatosFilaPago {
  try {
    const d = JSON.parse(json) as Partial<DatosFilaPago> | null;
    return { ...SIN_DATOS, ...(d ?? {}) };
  } catch {
    return SIN_DATOS;
  }
}

/** El archivo de la carga masiva: .xlsx de hasta 10 MB (la API limita además a 1.000 filas). null = válido. */
export function errorArchivoCarga(archivo: File): string | null {
  if (!archivo.name.toLowerCase().endsWith(".xlsx")) return "Carga un archivo de Excel (.xlsx) con la plantilla de pagos.";
  if (archivo.size > MAXIMO_ARCHIVO) return "El archivo supera 10 MB.";
  return null;
}

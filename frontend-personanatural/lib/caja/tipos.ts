// Contrato del BFF de la caja (core-personanatural, `Caja/CajaContratos.cs`), JSON en camelCase.

export type MedioCaja = "efectivo" | "datafono" | "transferencia" | "otro";

export const MEDIOS_CAJA: readonly { id: MedioCaja; etiqueta: string }[] = [
  { id: "efectivo", etiqueta: "Efectivo" },
  { id: "datafono", etiqueta: "Datáfono" },
  { id: "transferencia", etiqueta: "Transferencia" },
  { id: "otro", etiqueta: "Otro" },
];

export const ETIQUETA_MEDIO: Record<string, string> = Object.fromEntries(MEDIOS_CAJA.map((m) => [m.id, m.etiqueta]));

export interface SesionCaja {
  usuario: { id: string; nombre: string };
  organismo: { id: string; codigo: string; nombre: string };
  expiraEn: string;
}

export interface LineaCobro {
  concepto: string;
  valorCop: number;
}

export interface TramiteParaCobrar {
  codigo: string;
  tipoTramite: { codigo: string; nombre: string };
  organismo: { codigo: string; nombre: string };
  lineas: LineaCobro[];
  totalCop: number;
  versionTarifa: string;
  venceEn: string;
}

export interface CobroConfirmado {
  codigo: string;
  valorCop: number;
  medio: string;
  medioDetalle?: string | null;
  pagadoEn: string;
  mensaje: string;
}

export interface ItemCobroDelDia {
  codigo: string;
  valorCop: number;
  medio: string;
  medioDetalle?: string | null;
  /** «HH:mm», hora de Colombia. */
  hora: string;
  usuario: string;
  /** `pagado` o `anulado` (el anulado no suma al total del día). */
  estado: string;
}

export interface CobrosDelDia {
  fecha: string;
  totalDiaCop: number;
  items: ItemCobroDelDia[];
  total: number;
  pagina: number;
  tamano: number;
}

export interface ConfirmacionPedida {
  codigo: string;
  medio: MedioCaja;
  medioDetalle?: string;
  totalVistoCop: number;
  prefactura?: File | null;
  /** Una por intento de confirmación: reintentar con la misma no cobra dos veces. */
  llave: string;
}

/** Prefactura: PDF, JPG o PNG de hasta 5 MB (el BFF vuelve a validar por la firma del archivo). */
export const PREFACTURA_MAX_BYTES = 5 * 1024 * 1024;
export const PREFACTURA_TIPOS: readonly string[] = ["application/pdf", "image/jpeg", "image/png"];
export const DETALLE_MEDIO_MAX = 120;

/** Código público del trámite. */
export const FORMATO_CODIGO = /^PN-\d{4}-\d{6}$/;

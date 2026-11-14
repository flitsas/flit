// Datos ficticios de la bandeja (PS-4) y de la gestión (PS-5) del backoffice (plan §6.2 y §6.3). Placas TST###, compañías
// y documentos inventados: nada sale de datos reales.
import type { FilaBandeja, ListaBandeja, SaneamientoBackoffice } from "@/lib/backoffice/saneamientos";
import type { RenglonSaneamiento } from "@/lib/saneamiento/api";
import { deudaCompleta } from "./datosDeuda";

export { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

export const EMPRESA_A = "00000000-0000-7000-8000-0000000000a1";
export const EMPRESA_B = "00000000-0000-7000-8000-0000000000b2";
/** Compañía que no está en el listado de empresas: se rotula «Compañía 0000000c». */
export const EMPRESA_DESCONOCIDA = "0000000c-0000-7000-8000-0000000000c3";
export const LOTE_BO = "00000000-0000-7000-8000-00000000b001";
export const SID = "00000000-0000-7000-8000-00000000d001";
export const USUARIO_FLIT = "a1b2c3d4-0000-7000-8000-0000000000f5";
export const BASE = "/backoffice/saneamientos";
export const RUTA_DETALLE = `${BASE}/${SID}`;

export const EMPRESAS = [
  { id: EMPRESA_A, nombre: "Transportes A SAS", nit: "900000010" },
  { id: EMPRESA_B, nombre: "Carga del Norte SAS", nit: null },
];

export function filaBandeja(n: number, cambios: Partial<FilaBandeja> = {}): FilaBandeja {
  return {
    id: `00000000-0000-7000-8000-00000000d${String(n).padStart(3, "0")}`,
    empresa: EMPRESA_A,
    diagnostico_id: LOTE_BO,
    diagnostico_nombre: "Lote octubre",
    vehiculo_id: `vehiculo-${n}`,
    placa: `TST${String(n).padStart(3, "0")}`,
    modo: "CON_ANALISIS",
    veredicto: "BLOQUEADO",
    estado: "SOLICITADO",
    solicitado_en: "2026-11-03T15:00:00+00:00",
    tomado_en: null,
    cerrado_en: null,
    renglones_total: 0,
    renglones_pendientes: 0,
    ...cambios,
  };
}

export function listaBandeja(saneamientos: FilaBandeja[], cambios: Partial<ListaBandeja> = {}): ListaBandeja {
  return { pagina: 1, tamano: 10, total: saneamientos.length, saneamientos, ...cambios };
}

export const RID_PROPIETARIO = "00000000-0000-7000-8000-00000000r001";
export const RID_SOAT = "00000000-0000-7000-8000-00000000r002";

export function renglon(cambios: Partial<RenglonSaneamiento> = {}): RenglonSaneamiento {
  return {
    id: RID_PROPIETARIO,
    version: 45,
    fuente: "SIMIT_PROPIETARIO",
    hallazgo: "DEUDA",
    descripcion: null,
    automatico: true,
    resultado: "PENDIENTE",
    nota: null,
    evidencia: null,
    ...cambios,
  };
}

export const renglonSoat = (cambios: Partial<RenglonSaneamiento> = {}) =>
  renglon({ id: RID_SOAT, version: 46, fuente: "SOAT", hallazgo: "NO_VIGENTE", ...cambios });

/** Saneamiento de FLIT en gestión, con dos conceptos pendientes y el historial completo. */
export function saneamientoBo(cambios: Partial<SaneamientoBackoffice> = {}): SaneamientoBackoffice {
  return {
    id: SID,
    version: 123,
    estado: "EN_GESTION",
    solicitado_en: "2026-11-03T15:00:00+00:00",
    tomado_en: "2026-11-04T15:00:00+00:00",
    cerrado_en: null,
    motivo_cierre: null,
    motivo_anulacion: null,
    observacion_flit: null,
    diagnostico_id: LOTE_BO,
    diagnostico_nombre: "Lote octubre",
    vehiculo_id: "vehiculo-1",
    placa: "TST001",
    veredicto: "BLOQUEADO",
    renglones: [renglon(), renglonSoat()],
    historial: [
      { evento: "SOLICITADO", ocurrido_en: "2026-11-03T15:00:00+00:00", actor: "COMPANIA", fuente: null, motivo: null, usuario_id: "00000000-0000-7000-8000-000000000001" },
      { evento: "TOMADO", ocurrido_en: "2026-11-04T15:00:00+00:00", actor: "FLIT", fuente: null, motivo: null, usuario_id: USUARIO_FLIT },
    ],
    ...cambios,
  };
}

export const saneamientoSolicitado = (cambios: Partial<SaneamientoBackoffice> = {}) =>
  saneamientoBo({ estado: "SOLICITADO", tomado_en: null, renglones: [], historial: [saneamientoBo().historial[0]], ...cambios });

/** Deuda del vehículo en la gestión (GET …/{sid}/deuda): la misma forma del panel de la compañía. */
export const deudaBackoffice = () => deudaCompleta({ vehiculo: { id: "vehiculo-1", placa: "TST001" } });

/** Archivo PDF mínimo con firma y fin (nunca un comprobante real). */
export function pdf(nombre = "pago.pdf", contenido = "%PDF-1.7\n1 0 obj\n%%EOF\n"): File {
  return new File([contenido], nombre, { type: "application/pdf" });
}

// Datos ficticios del panel de deuda y del saneamiento (plan §6.3 y §6.5) para las pruebas de PS-2. Placas y documentos
// inventados: nunca datos reales. SP-11: la persona del SIMIT no trae nombre.
import type { PanelDeuda, Saneamiento } from "@/lib/saneamiento/api";

export const LOTE = "00000000-0000-7000-8000-00000000e001";
export const VEHICULO = "vehiculo-1";
export const PLACA = "TST001";
export const RUTA_DEUDA = `/diagnosticos/${LOTE}/vehiculos/${VEHICULO}/deuda`;
export const RUTA_SANEAMIENTO = `/diagnosticos/${LOTE}/vehiculos/${VEHICULO}/saneamiento`;

/** Vehículo Con Análisis IA, bloqueado, con SIMIT recortado, impuesto con vigencias, SOAT vencido y locatario N/A. */
export function deudaCompleta(cambios: Partial<PanelDeuda> = {}): PanelDeuda {
  return {
    vehiculo: { id: VEHICULO, placa: PLACA },
    modo: "CON_ANALISIS",
    dictamen: {
      veredicto: "BLOQUEADO",
      catalogo_version: 2,
      motivos: [
        { codigo: "IMPUESTO", efecto: "ADVIERTE", texto: "Hay impuesto pendiente de una vigencia." },
        { codigo: "SIMIT_PROPIETARIO", efecto: "BLOQUEA", texto: "El propietario tiene comparendos pendientes." },
      ],
    },
    fuentes: [
      { fuente: "RTM", estado: "SIN_HALLAZGO", consultado_en: "2026-11-03T15:00:00+00:00", vence: "2027-05-01" },
      { fuente: "SOAT", estado: "NO_VIGENTE", consultado_en: "2026-11-03T15:00:00+00:00", vence: "2026-10-01" },
      {
        fuente: "IMPUESTO",
        estado: "CON_DEUDA",
        consultado_en: "2026-11-03T15:00:00+00:00",
        monto_total: 1250000,
        vigencias: [
          { anio: 2025, valor: 1250000, pagada: false },
          { anio: 2024, valor: 980000, pagada: true },
        ],
      },
      { fuente: "SIMIT_LOCATARIO", estado: "NO_APLICA", motivo_na: "SIN_DOCUMENTO_LOCATARIO" },
      {
        fuente: "SIMIT_PROPIETARIO",
        estado: "CON_DEUDA",
        consultado_en: "2026-11-03T15:00:00+00:00",
        persona: { tipo_documento: "CC", documento: "900000001" },
        paz_y_salvo: false,
        comparendos: [
          { numero: "C-0001", fecha: "2026-01-15", valor: "450000.00", estado: "Pendiente" },
          { numero: "C-0002", fecha: "2026-02-20", valor: "$ 120.000", estado: "Cobro coactivo" },
        ],
        comparendos_truncados: true,
        acuerdos_pago: [{ saldo_pendiente: "300000" }],
      },
      { fuente: "SIMIT_VEHICULO", estado: "SIN_VERIFICAR", consultado_en: null },
    ],
    saneamiento: { estado: "EN_GESTION" },
    ...cambios,
  };
}

/** Vehículo sin hallazgos en ninguna fuente, modo Simple (sin dictamen) y sin saneamiento. */
export function deudaSinPendientes(cambios: Partial<PanelDeuda> = {}): PanelDeuda {
  return {
    vehiculo: { id: VEHICULO, placa: PLACA },
    modo: "SIMPLE",
    dictamen: null,
    fuentes: [
      { fuente: "SIMIT_VEHICULO", estado: "SIN_HALLAZGO", consultado_en: "2026-11-03T15:00:00+00:00", paz_y_salvo: true, comparendos: [] },
      { fuente: "IMPUESTO", estado: "SIN_HALLAZGO", consultado_en: "2026-11-03T15:00:00+00:00", monto_total: 0, vigencias: [] },
      { fuente: "SOAT", estado: "SIN_HALLAZGO", consultado_en: "2026-11-03T15:00:00+00:00", vence: "2027-03-10" },
      { fuente: "RTM", estado: "NO_APLICA", motivo_na: "SERVICIO_PUBLICO" },
    ],
    saneamiento: null,
    ...cambios,
  };
}

/** Saneamiento EN_GESTION con dos renglones (uno con evidencia), observación de FLIT e historial. */
export function saneamientoEnGestion(cambios: Partial<Saneamiento> = {}): Saneamiento {
  return {
    id: "san-1",
    version: 7,
    estado: "EN_GESTION",
    solicitado_en: "2026-11-04T14:00:00+00:00",
    solicitado_por_mi: true,
    tomado_en: "2026-11-05T16:30:00+00:00",
    cerrado_en: null,
    motivo_cierre: null,
    motivo_anulacion: null,
    observacion_flit: "Se gestiona el pago con el organismo de tránsito.",
    renglones: [
      {
        id: "ren-1",
        version: 3,
        fuente: "SIMIT_PROPIETARIO",
        hallazgo: "DEUDA",
        descripcion: null,
        automatico: true,
        resultado: "SANEADO",
        nota: "Pagado en el portal.",
        evidencia: { nombre: "pago.pdf", tamano: 123456, subida_en: "2026-11-06T10:00:00+00:00" },
      },
      {
        id: "ren-2",
        version: 4,
        fuente: "OTRO",
        hallazgo: null,
        descripcion: "Embargo registrado",
        automatico: false,
        resultado: "PENDIENTE",
        nota: null,
        evidencia: null,
      },
    ],
    historial: [
      { evento: "SOLICITADO", ocurrido_en: "2026-11-04T14:00:00+00:00", actor: "COMPANIA", fuente: null, motivo: null },
      { evento: "TOMADO", ocurrido_en: "2026-11-05T16:30:00+00:00", actor: "FLIT", fuente: null, motivo: null },
      // Un evento que la compañía no debería recibir: no se pinta.
      { evento: "RENGLON_ACTUALIZADO", ocurrido_en: "2026-11-06T09:00:00+00:00", actor: "FLIT", fuente: "SIMIT_PROPIETARIO", motivo: null },
      { evento: "EVIDENCIA_CARGADA", ocurrido_en: "2026-11-06T10:00:00+00:00", actor: "FLIT", fuente: "SIMIT_PROPIETARIO", motivo: null },
    ],
    ...cambios,
  };
}

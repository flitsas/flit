/**
 * Qué lleva el Excel de la bandeja del organismo (HU #12220).
 *
 * <p>La regla que gobierna este archivo: <b>los datos que en pantalla van apilados dentro de una
 * misma celda salen cada uno en SU columna</b>. La bandeja apila la empresa y el gestor en una
 * celda, y el estado con la ruta de placa en otra; en una hoja de cálculo eso son celdas con dos
 * valores que no se pueden ordenar ni filtrar, que es justo para lo que se descarga el archivo.</p>
 *
 * <p>La maquinaria de escritura no se reinventa: <c>buildWorkbook</c> y <c>lib/xlsx</c> ya están en
 * producción en Consultas, ICT, LOG QX, los reportes del organismo y el listado del gestor. De ahí
 * salen gratis los números como números, las fechas como fechas y la neutralización del prefijo de
 * fórmula.</p>
 */
import { bogotaClock, type XlsxCell } from '@/lib/xlsx';
import { formatFecha } from '@/lib/format/date';
import type { OtClientProcedure } from '@/lib/api/types-ot';
import { OT_PROCEDURES_COLUMNS } from './ot-procedures-columns';
import { formatOtProcedureStatus } from '@/components/admin/transit-offices/ot-utils';

/** Cómo se lee la ruta de placa en el archivo. Vacío si el trámite no está en esa ruta. */
const RUTA_PLACA_LABEL: Record<string, string> = {
  preasignado: 'Placa preasignada',
  asignado: 'Placa asignada',
  terminado: 'Terminado',
};

export interface OtProcedureExportField {
  id: string;
  label: string;
  value: (row: OtClientProcedure) => string;
  /** Valor tipado. `null` para celda VACÍA: un «—» en columna de fecha la vuelve texto. */
  raw: (row: OtClientProcedure) => XlsxCell;
  /** Ancho en Excel, en caracteres. Sin esto una fecha sale como `#####`. */
  width?: number;
}

const texto = (v: string | null | undefined): string => (v ?? '').trim();

/**
 * Los campos que aporta cada columna de la tabla. Una columna puede aportar VARIOS —ahí está el
 * desapilado— y el orden del arreglo es el orden de las columnas en la hoja.
 */
const EXPORT_FIELDS: Record<string, OtProcedureExportField[]> = {
  radicado: [
    {
      id: 'radicado',
      label: 'Radicado',
      value: (r) => texto(r.referenceNumber),
      raw: (r) => texto(r.referenceNumber) || null,
      width: 14,
    },
    // El prioritario es un icono en la tabla y no se puede exportar como tal; en la hoja es la
    // columna por la que de verdad se va a filtrar cuando alguien quiera revisar la cola urgente.
    {
      id: 'prioritario',
      label: 'Prioritario',
      value: (r) => (r.prioritario ? 'Sí' : 'No'),
      raw: (r) => (r.prioritario ? 'Sí' : 'No'),
      width: 11,
    },
  ],
  vin: [
    {
      id: 'vin',
      label: 'VIN',
      value: (r) => texto(r.vin),
      raw: (r) => texto(r.vin) || null,
      width: 20,
    },
  ],
  placa: [
    {
      id: 'placa',
      label: 'Placa',
      value: (r) => texto(r.placa),
      raw: (r) => texto(r.placa) || null,
      width: 10,
    },
  ],
  vendedor: [
    {
      id: 'vendedor',
      label: 'Propietario / vendedor',
      value: (r) => texto(r.vendedorNombre),
      raw: (r) => texto(r.vendedorNombre) || null,
      width: 28,
    },
  ],
  comprador: [
    {
      id: 'comprador',
      label: 'Comprador',
      value: (r) => texto(r.compradorNombre),
      raw: (r) => texto(r.compradorNombre) || null,
      width: 28,
    },
  ],
  tipoTramite: [
    {
      id: 'tipoTramite',
      label: 'Tipo de trámite',
      value: (r) => texto(r.procedureTypeName),
      raw: (r) => texto(r.procedureTypeName) || null,
      width: 24,
    },
  ],
  // La celda de la tabla apila empresa y gestor: en la hoja son dos columnas, o no se puede
  // agrupar por empresa, que es la primera cosa que hace quien abre este archivo.
  empresaGestor: [
    {
      id: 'empresa',
      label: 'Empresa cliente',
      value: (r) => texto(r.clientTenantName),
      raw: (r) => texto(r.clientTenantName) || null,
      width: 30,
    },
    {
      id: 'gestor',
      label: 'Gestor',
      value: (r) => texto(r.gestorNombre),
      raw: (r) => texto(r.gestorNombre) || null,
      width: 26,
    },
  ],
  // Lo mismo con el estado: el estado del trámite y su ruta de placa son dos ejes distintos, y en
  // la bandeja se leen juntos solo porque comparten sitio.
  estado: [
    {
      id: 'estado',
      label: 'Estado',
      value: (r) => formatOtProcedureStatus(r.status),
      raw: (r) => formatOtProcedureStatus(r.status),
      width: 16,
    },
    {
      id: 'rutaPlaca',
      label: 'Ruta de placa',
      value: (r) => (r.plateFlowStatus ? (RUTA_PLACA_LABEL[r.plateFlowStatus] ?? r.plateFlowStatus) : ''),
      raw: (r) =>
        r.plateFlowStatus ? (RUTA_PLACA_LABEL[r.plateFlowStatus] ?? r.plateFlowStatus) : null,
      width: 18,
    },
  ],
  fechaRadicacion: [
    {
      id: 'fechaRadicacion',
      label: 'Fecha de radicación',
      // `value` es el texto de respaldo (CSV); `raw` es lo que hace que Excel la trate como FECHA
      // y no como una cadena que no se puede ordenar ni restar.
      value: (r) => (r.createdAt ? formatFecha(r.createdAt) : ''),
      raw: (r) => bogotaClock(r.createdAt ?? null),
      width: 20,
    },
  ],
};

/**
 * Los campos del archivo, a partir de las columnas que el usuario tiene visibles.
 *
 * <p>Respeta el orden de <see cref="OT_PROCEDURES_COLUMNS"/> para que la hoja se lea como la
 * pantalla, y no repite un campo aunque dos columnas lo aportaran.</p>
 */
export function otProceduresExportFields(
  visibleKeys: readonly string[],
): OtProcedureExportField[] {
  const visibles = new Set(visibleKeys);
  const campos: OtProcedureExportField[] = [];
  const yaPuestos = new Set<string>();

  for (const columna of OT_PROCEDURES_COLUMNS) {
    if (!visibles.has(columna.key)) continue;
    for (const campo of EXPORT_FIELDS[columna.key] ?? []) {
      if (yaPuestos.has(campo.id)) continue;
      yaPuestos.add(campo.id);
      campos.push(campo);
    }
  }

  return campos;
}

/**
 * `bandeja-ot_2026-09-09_10-30.xlsx`, o `..._parte_1_de_3.xlsx` cuando el resultado se repartió.
 *
 * <p>Lleva fecha y hora porque el destino real es la carpeta de Descargas: media docena de
 * `bandeja-ot.xlsx` seguidos no se distinguen entre sí. El sufijo de parte solo aparece con MÁS de
 * un archivo: un `parte_1_de_1` sugiere que falta algo por descargar.</p>
 */
export function nombreArchivoBandejaOt(
  sello: string,
  parte?: { numero: number; total: number },
): string {
  const sufijo = parte && parte.total > 1 ? `_parte_${parte.numero}_de_${parte.total}` : '';
  return `bandeja-ot_${sello}${sufijo}.xlsx`;
}

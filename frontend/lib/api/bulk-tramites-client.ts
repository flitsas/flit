import { apiUrl, tenantHeader } from './tramites-client';

/** Tipos de plantilla soportados por la carga masiva (contrato con el backend de HU #12520). */
export type BulkTramitesTemplateType = 'matricula' | 'traspaso' | 'otros';

export interface BulkTramitesTemplateOption {
  tipo: BulkTramitesTemplateType;
  titulo: string;
  descripcion: string;
}

/**
 * Las tres plantillas, en el orden en que se ofrecen. «Otros trámites» es una sola para todo el
 * resto del catálogo (cambio de color, prenda, etc.): el tipo concreto se elige DENTRO del archivo,
 * en su columna `tipo_tramite`.
 */
export const BULK_TRAMITES_TEMPLATES: readonly BulkTramitesTemplateOption[] = [
  {
    tipo: 'matricula',
    titulo: 'Matrícula inicial',
    descripcion:
      'Vehículo por VIN y propietarios (hasta 4, con porcentaje). Las empresas van por NIT y firma su representante legal registrado.',
  },
  {
    tipo: 'traspaso',
    titulo: 'Traspaso',
    descripcion:
      'Vehículo por placa, comprador y vendedor (hasta 4 propietarios por lado). Las empresas van por NIT y firma su representante legal registrado.',
  },
  {
    tipo: 'otros',
    titulo: 'Otros trámites',
    descripcion: 'Cambio de color, prenda, duplicados… El tipo se elige dentro del archivo.',
  },
];

/** Tope de filas por archivo (mismo número que valida el backend). */
export const BULK_TRAMITES_MAX_ROWS = 50;

export interface BulkTramitesBatchAccepted {
  batchId: string;
  totalRows: number;
  rowsWithStructuralErrors: number;
}

/**
 * Mensajes de los motivos de rechazo del ARCHIVO completo. El backend devuelve un código estable;
 * traducirlo aquí evita enseñarle al usuario `template_invalid` en pantalla.
 */
const ERRORES_ARCHIVO: Record<string, string> = {
  template_invalid:
    'El archivo no coincide con la plantilla. No cambies, renombres ni muevas las columnas de la primera fila; descarga la plantilla de nuevo si tienes dudas.',
  too_many_rows: `El archivo supera las ${BULK_TRAMITES_MAX_ROWS} filas permitidas. Divídelo en varios archivos.`,
  invalid_file: 'El archivo no es un Excel (.xlsx) válido.',
  empty_file: 'El archivo no tiene filas de datos: está solo el encabezado.',
};

export function mensajeErrorCargaMasiva(codigo: string | undefined): string {
  if (!codigo) return 'No se pudo procesar el archivo.';
  return ERRORES_ARCHIVO[codigo] ?? codigo;
}

/** Resultado de una fila tras procesarse (HU #12523). `null` mientras el lote está en cola. */
export type BulkTramitesRowOutcome = 'created' | 'created_pending' | 'not_created';

export interface BulkTramitesBatchCounts {
  created: number;
  createdPending: number;
  notCreated: number;
  pendientes: number;
}

export interface BulkTramitesBatchSummary {
  id: string;
  templateType: BulkTramitesTemplateType;
  sourceFilename: string;
  status: 'queued' | 'processing' | 'completed';
  totalRows: number;
  createdAt: string;
  completedAt: string | null;
  counts: BulkTramitesBatchCounts;
}

export interface BulkTramitesBatchRow {
  rowNumber: number;
  identificador: string | null;
  outcome: BulkTramitesRowOutcome | null;
  motivo: string | null;
  procedureInstanceId: string | null;
}

export interface BulkTramitesBatchDetail {
  batch: BulkTramitesBatchSummary;
  rows: BulkTramitesBatchRow[];
}

/** Etiqueta de lo que pasó con la fila, en el idioma del usuario y no en el del backend. */
export function etiquetaOutcome(outcome: BulkTramitesRowOutcome | null): string {
  switch (outcome) {
    case 'created':
      return 'Creado';
    case 'created_pending':
      return 'Creado — falta retomarlo';
    case 'not_created':
      return 'No creado';
    default:
      return 'En cola';
  }
}

export const bulkTramitesClient = {
  /**
   * Descarga la plantilla del tipo indicado. Devuelve el blob y el nombre que el backend propone
   * en el Content-Disposition — mismo patrón que `downloadAttachment`.
   */
  descargarPlantilla: async (
    tipo: BulkTramitesTemplateType,
  ): Promise<{ blob: Blob; filename: string }> => {
    const res = await fetch(
      apiUrl(`/api/v1/tramites/carga-masiva/plantilla?tipo=${tipo}`),
      { headers: tenantHeader() },
    );
    if (!res.ok) {
      throw new Error(`${res.status} ${res.statusText}`);
    }

    const blob = await res.blob();
    const cd = res.headers.get('content-disposition') ?? '';
    const match = /filename="?([^";]+)"?/i.exec(cd);
    return { blob, filename: match?.[1]?.trim() || `plantilla-carga-masiva-${tipo}.xlsx` };
  },

  /**
   * Sube el archivo diligenciado. La respuesta llega en cuanto el lote queda encolado: el
   * procesamiento ocurre en segundo plano (HU #12523), por eso no devuelve trámites creados sino
   * el id del lote y cuántas filas entraron.
   */
  subirLote: async (
    tipo: BulkTramitesTemplateType,
    archivo: File,
  ): Promise<BulkTramitesBatchAccepted> => {
    const form = new FormData();
    form.append('archivo', archivo);

    const res = await fetch(apiUrl(`/api/v1/tramites/carga-masiva/lotes?tipo=${tipo}`), {
      method: 'POST',
      headers: tenantHeader(),
      body: form,
    });

    if (!res.ok) {
      const body = await res.text().catch(() => '');
      let codigo: string | undefined;
      try {
        codigo = (JSON.parse(body) as { error?: string }).error;
      } catch {
        codigo = undefined;
      }
      throw new Error(mensajeErrorCargaMasiva(codigo));
    }

    return (await res.json()) as BulkTramitesBatchAccepted;
  },

  /** Últimos lotes del cliente, más recientes primero (HU #12524). */
  listarLotes: async (): Promise<BulkTramitesBatchSummary[]> => {
    const res = await fetch(apiUrl('/api/v1/tramites/carga-masiva/lotes'), {
      headers: tenantHeader(),
    });
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);

    const body = (await res.json()) as { items: BulkTramitesBatchSummary[] };
    return body.items;
  },

  /** Detalle fila a fila de un lote. */
  detalleLote: async (batchId: string): Promise<BulkTramitesBatchDetail> => {
    const res = await fetch(apiUrl(`/api/v1/tramites/carga-masiva/lotes/${batchId}`), {
      headers: tenantHeader(),
    });
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);

    return (await res.json()) as BulkTramitesBatchDetail;
  },
};

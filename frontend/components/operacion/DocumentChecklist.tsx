'use client';

import { useRef, useState } from 'react';
import { useWizardFocusTrap } from './use-wizard-focus-trap';
import { Eye, FileText, Info } from 'lucide-react';
import {
  resumirVins,
  useProcedureDocuments,
  type OcrUiResult,
} from '@/hooks/useProcedureDocuments';
import { useProcedureBatchUpload } from '@/hooks/useProcedureBatchUpload';
import { BatchDropzone } from './BatchDropzone';
import { BatchReviewPanel } from './BatchReviewPanel';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { WizardCardHeader, WizardSegmented } from './wizard-atoms';
import { INLINE_ALERT_TONES } from '@/components/atom/InlineAlert';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { isPrendaManagedChecklistTipo } from './prenda-document-tipos';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DocumentPreviewModal } from '@/components/shared/DocumentPreviewModal';
import type {
  ChecklistItemView,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
  /**
   * Notifica al contenedor (wizard) que los documentos cambiaron, para que
   * re-consulte su estado de gating. Sin esto, el wizard no se entera de que
   * el checklist quedó completo y "Continuar" no se habilita.
   */
  onChanged?: () => void;
  /**
   * Oculta el título "Documentos requeridos" y su descripción cuando el contenedor
   * ya pinta el título del paso (el wizard lo hace con su h2 + subtítulo).
   */
  hideHeader?: boolean;
  /** Modalidad del trámite: decide qué tipos pasan por OCR (matrícula: 4; traspaso: impronta+soat). */
  modalidad?: WizardModalidad;
}

/**
 * Tipos de documento que el sistema genera automáticamente (mandato, trámite virtual).
 * El operador no puede ni debe adjuntarlos; el slot muestra "Autogenerado por el sistema".
 */
const AUTO_DOC_TIPOS = new Set(['mandato', 'tramite_virtual']);

/** MIME permitidos por el contrato. */
export const ALLOWED_MIME = [
  'application/pdf',
  'image/jpeg',
  'image/png',
  'image/webp',
] as const;

/** Tamaño máximo: 20 MB. */
export const MAX_SIZE_BYTES = 20 * 1024 * 1024;

const ALLOWED_LABEL = 'PDF, JPG, PNG o WEBP';

/**
 * Límites de carga por tipo de documento (HU #10524, RF08/09/10). Cuando se omiten (o vienen
 * vacíos) se aplican los límites globales actuales — misma validación que hoy, sin regresión.
 * El backend (HU #10520) es la fuente de verdad: valida por tipo en la carga; este check cliente
 * es un pre-filtro de UX.
 */
export interface FileTypeLimits {
  allowedMimes?: readonly string[];
  maxSizeBytes?: number;
}

/**
 * Valida mime y tamaño antes de subir. Pura y testeable de forma aislada. Aplica los límites
 * por tipo si se proveen (<paramref name="limits"/>), con respaldo a los globales.
 * Devuelve un mensaje de error claro, o null si el archivo es aceptable.
 */
export function validateFile(file: File, limits?: FileTypeLimits): string | null {
  const allowed = (limits?.allowedMimes && limits.allowedMimes.length > 0
    ? limits.allowedMimes
    : ALLOWED_MIME) as readonly string[];
  const maxSize = limits?.maxSizeBytes && limits.maxSizeBytes > 0 ? limits.maxSizeBytes : MAX_SIZE_BYTES;

  if (!allowed.includes(file.type)) {
    return `Tipo de archivo no permitido. Usa ${ALLOWED_LABEL}.`;
  }
  if (file.size > maxSize) {
    return maxSize === MAX_SIZE_BYTES
      ? 'El archivo supera el máximo de 20 MB.'
      : `El archivo supera el máximo de ${formatSize(maxSize)}.`;
  }
  return null;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Formatea un monto como moneda COP: 119990000 → "$119.990.000". '' si no es un número &gt; 0. */
function formatCOP(value: unknown): string {
  const n = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(n) || n <= 0) return '';
  return new Intl.NumberFormat('es-CO', {
    style: 'currency',
    currency: 'COP',
    maximumFractionDigits: 0,
  }).format(n);
}

/** Valor string no vacío de un campo del JSON del OCR (trim); '' si es null/undefined. */
function pickStr(data: Record<string, unknown>, key: string): string {
  const v = data[key];
  return v === null || v === undefined ? '' : String(v).trim();
}

/** Une varios campos no vacíos con un separador (p. ej. marca + línea + modelo). */
function joinFields(data: Record<string, unknown>, keys: string[], sep = ' '): string {
  return keys.map((k) => pickStr(data, k)).filter(Boolean).join(sep);
}

/** Descriptor de un campo del resumen OCR. */
interface OcrField {
  label: string;
  /** Valor a mostrar (ya formateado); '' → la fila se omite. */
  value: (d: Record<string, unknown>) => string;
  /** 'state' → se pinta como chip de color (coincide/no_coincide/…). */
  kind?: 'state';
  /** Campo VIN: se resalta en rojo si el rechazo fue por cruce de VIN. */
  vin?: boolean;
  /** Valor en negrita (p. ej. Total). */
  strong?: boolean;
}

/** Campos del resumen por tipo de documento (set ampliado). */
const OCR_RESUMEN_FIELDS: Record<string, ReadonlyArray<OcrField>> = {
  factura: [
    { label: 'N.º factura', value: (d) => pickStr(d, 'numero_factura') },
    { label: 'Fecha', value: (d) => pickStr(d, 'fecha') },
    { label: 'Emisor', value: (d) => pickStr(d, 'emisor_nombre') },
    { label: 'NIT', value: (d) => pickStr(d, 'emisor_nit') },
    { label: 'Comprador', value: (d) => pickStr(d, 'comprador_nombre') },
    { label: 'Vehículo', value: (d) => joinFields(d, ['vehiculo_marca', 'vehiculo_linea', 'vehiculo_modelo']) },
    { label: 'Color', value: (d) => pickStr(d, 'vehiculo_color') },
    { label: 'IVA', value: (d) => formatCOP(d.iva) },
    { label: 'Total', value: (d) => formatCOP(d.total), strong: true },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin'), vin: true },
  ],
  aduana: [
    { label: 'N.º documento', value: (d) => pickStr(d, 'numero_documento') },
    { label: 'Fecha', value: (d) => pickStr(d, 'fecha') },
    { label: 'Aduana', value: (d) => pickStr(d, 'aduana') },
    { label: 'Importador', value: (d) => pickStr(d, 'importador_nombre') },
    { label: 'Subpartida', value: (d) => pickStr(d, 'subpartida_arancelaria') },
    { label: 'País origen', value: (d) => pickStr(d, 'pais_origen') },
    { label: 'Valor CIF', value: (d) => formatCOP(d.valor_cif_cop), strong: true },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin') || pickStr(d, 'vehiculo_chasis'), vin: true },
  ],
  impronta: [
    { label: 'N.º certificado', value: (d) => pickStr(d, 'numero_certificado') },
    { label: 'Entidad', value: (d) => pickStr(d, 'entidad_emisora') },
    { label: 'Estado motor', value: (d) => pickStr(d, 'estado_motor'), kind: 'state' },
    { label: 'Estado VIN', value: (d) => pickStr(d, 'estado_vin'), kind: 'state' },
    { label: 'Estado chasis', value: (d) => pickStr(d, 'estado_chasis'), kind: 'state' },
    { label: 'Alertas', value: (d) => (Array.isArray(d.alertas) ? d.alertas.join(', ') : '') },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin') || pickStr(d, 'vehiculo_chasis'), vin: true },
  ],
  soat: [
    { label: 'N.º póliza', value: (d) => pickStr(d, 'numero_poliza') },
    { label: 'Aseguradora', value: (d) => pickStr(d, 'aseguradora') },
    { label: 'Vigencia', value: (d) => joinFields(d, ['fecha_inicio', 'fecha_vencimiento'], ' – ') },
    { label: 'Estado', value: (d) => pickStr(d, 'estado_poliza') },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin'), vin: true },
  ],
  // El prompt de `rtm` llegó en HU #10977 pero su resumen nunca se añadió aquí, así que el panel salía
  // con el encabezado y la grilla vacía. Los campos son los que pide el certificado de vigencia.
  rtm: [
    { label: 'N.º certificado', value: (d) => pickStr(d, 'numero_certificado') },
    { label: 'CDA', value: (d) => pickStr(d, 'cda_expide') },
    { label: 'Expedición', value: (d) => pickStr(d, 'fecha_expedicion') },
    { label: 'Vencimiento', value: (d) => pickStr(d, 'fecha_vencimiento') },
    { label: 'Estado', value: (d) => pickStr(d, 'estado') },
    { label: 'Resultado', value: (d) => pickStr(d, 'resultado') },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin'), vin: true },
  ],
};

/** Nombre corto del tipo para el encabezado de la tarjeta. */
const TIPO_LABEL: Record<string, string> = {
  factura: 'Factura',
  aduana: 'Aduana',
  impronta: 'Impronta',
  soat: 'SOAT',
  rtm: 'RTM',
};

/** Nombre legible de un tipo de documento OCR; el propio código si no está en el mapa. */
export function tipoLabel(tipo: string): string {
  return TIPO_LABEL[tipo] ?? tipo;
}

/**
 * Tono semántico del chip de estado (coincide / no_coincide / no_aplica / no_verificado).
 *
 * B4 (guardián de diseño) — antes pintaba con hex propios fuera de la paleta autorizada
 * (`#3B8A00` 3.8:1, `#B77900` 3.2:1, `#F9AC00` fuera de token). Ahora resuelve un tono de
 * `StatusBadge`, la única fuente de color de estado del sistema.
 */
function ocrFieldTone(value: string): StatusTone {
  const v = value.toLowerCase();
  if (v === 'coincide') return 'success';
  if (v === 'no_coincide') return 'danger';
  if (v === 'no_aplica') return 'neutral';
  return 'warning'; // no_verificado / otros
}

/** Chip "PDF recortado (X/Y págs)" cuando el OCR recortó un subconjunto de páginas. */
function recorteLabel(data: Record<string, unknown> | null): string | null {
  if (!data || data._paginas_extraidas !== true) return null;
  const extraidas = Array.isArray(data.paginas_documento) ? data.paginas_documento.length : null;
  const originales = typeof data._paginas_originales === 'number' ? data._paginas_originales : null;
  return extraidas && originales ? `PDF recortado (${extraidas}/${originales} págs)` : 'PDF recortado';
}

/**
 * Indicador OCR compacto: icono con color por estado + tooltip al pasar el mouse.
 * El detalle (campos, motivo) vive en tooltip/modal — no agranda el contenedor de upload.
 * Exportada para cargue individual y revisión masiva.
 */
export function OcrStatusPanel({ tipo, ocr }: { tipo: string; ocr: OcrUiResult }) {
  const [detailOpen, setDetailOpen] = useState(false);
  const [tipOpen, setTipOpen] = useState(false);
  // B5 (guardián de diseño) — trampa de foco + retorno de foco + Escape del modal de detalle OCR.
  const ocrDialogRef = useRef<HTMLDivElement>(null);
  useWizardFocusTrap(ocrDialogRef, {
    active: detailOpen,
    onEscape: () => setDetailOpen(false),
  });
  // B4 (guardián de diseño) — antes traía hex propios fuera de la paleta autorizada (`#3B8A00`
  // 3.8:1, `#B77900` 3.2:1, `#F9AC00` fuera de token). El tono sale de `--badge-*`
  // (`globals.css`), la misma fuente que usa `StatusBadge`, y queda theme-aware de paso.
  const ocrTone: StatusTone =
    ocr.status === 'verified' ? 'success' : ocr.status === 'rejected' ? 'danger' : 'warning';
  const palette = {
    color: `var(--badge-${ocrTone}-fg)`,
    border: `var(--badge-${ocrTone}-border)`,
    bg: `var(--badge-${ocrTone}-bg)`,
    label:
      ocr.status === 'verified'
        ? 'Verificado'
        : ocr.status === 'rejected'
          ? 'Rechazado'
          : 'No analizado',
  };

  const data = ocr.data;
  const nombre = TIPO_LABEL[tipo] ?? tipo;
  const tipoDocumento = data ? pickStr(data, 'tipo_documento') : '';
  const recorte = recorteLabel(data);
  const rechazoPorVin = ocr.status === 'rejected' && !!ocr.motivo && /VIN/i.test(ocr.motivo);

  const fields = data
    ? (OCR_RESUMEN_FIELDS[tipo] ?? [])
        .map((field) => {
          const value = field.value(data);
          return { field, value: field.vin ? resumirVins(value) : value };
        })
        .filter((x) => x.value !== '')
    : [];

  const tipId = `ocr-tip-${tipo}`;

  return (
    <div className="relative shrink-0">
      <button
        type="button"
        className="inline-flex h-7 w-7 items-center justify-center rounded-full"
        style={{ background: palette.color, color: '#FFFFFF', boxShadow: '0 1px 2px rgba(0,0,0,0.18)' }}
        aria-label={`OCR ${nombre}: ${palette.label}. Ver detalle`}
        aria-describedby={tipOpen ? tipId : undefined}
        aria-expanded={detailOpen}
        onMouseEnter={() => setTipOpen(true)}
        onMouseLeave={() => setTipOpen(false)}
        onFocus={() => setTipOpen(true)}
        onBlur={() => setTipOpen(false)}
        onClick={() => setDetailOpen(true)}
      >
        <Info className="h-4 w-4" strokeWidth={2.5} aria-hidden />
      </button>

      {tipOpen && !detailOpen && (
        <div
          id={tipId}
          role="tooltip"
          className="absolute right-0 z-40 mt-1.5 w-56 rounded-xl border bg-white p-2.5 text-left shadow-lg dark:bg-[#162744]"
          style={{ borderColor: palette.border }}
        >
          <p className="text-xs font-bold" style={{ color: palette.color }}>
            OCR — {palette.label}
          </p>
          {ocr.motivo && (
            <p className="mt-1 text-xs leading-snug opacity-80">{ocr.motivo}</p>
          )}
          {(tipoDocumento || recorte) && (
            <p className="mt-1 text-xs opacity-70">
              {[tipoDocumento, recorte].filter(Boolean).join(' · ')}
            </p>
          )}
          {fields.length > 0 && (
            <p className="mt-1.5 text-xs font-medium" style={{ color: '#557EFF' }}>
              Clic para ver {fields.length} campo{fields.length === 1 ? '' : 's'}
            </p>
          )}
        </div>
      )}

      {detailOpen && (
        <div
          // B6 (guardián de diseño) — overlay único del sistema: `rgba(22,39,68,0.45)` +
          // `backdrop-blur-[6px]` (antes `bg-black/40 backdrop-blur-sm`, uno de los cuatro
          // overlays distintos del asistente).
          className="fixed inset-0 z-50 grid place-items-center bg-[rgba(22,39,68,0.45)] px-4 backdrop-blur-[6px]"
          role="dialog"
          aria-modal="true"
          aria-labelledby={`ocr-detail-title-${tipo}`}
          onClick={() => setDetailOpen(false)}
        >
          <div
            ref={ocrDialogRef}
            tabIndex={-1}
            className="max-h-[80vh] w-full max-w-lg overflow-y-auto rounded-2xl border bg-white p-4 shadow-xl outline-none focus:ring-0 dark:bg-[#162744]"
            style={{ borderColor: palette.border }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mb-3 flex items-start justify-between gap-2">
              <div className="flex items-center gap-2">
                <span
                  className="inline-flex h-7 w-7 items-center justify-center rounded-full"
                  style={{ background: palette.color, color: '#FFFFFF' }}
                  aria-hidden
                >
                  <Info className="h-4 w-4" strokeWidth={2.5} />
                </span>
                <h3
                  id={`ocr-detail-title-${tipo}`}
                  className="text-sm font-semibold"
                  style={{ color: '#162744' }}
                >
                  OCR — {nombre}
                  <span className="mt-0.5 block text-xs font-bold" style={{ color: palette.color }}>
                    {palette.label}
                  </span>
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setDetailOpen(false)}
                className="rounded-lg px-2 py-1 text-xs font-medium opacity-70 hover:opacity-100"
              >
                Cerrar
              </button>
            </div>
            {ocr.motivo && (
              <p className="mb-2 text-xs font-medium" style={{ color: palette.color }}>
                {ocr.motivo}
              </p>
            )}
            {(tipoDocumento || recorte) && (
              <p className="mb-2 text-xs opacity-70">
                {[tipoDocumento, recorte].filter(Boolean).join(' · ')}
              </p>
            )}
            {fields.length === 0 ? (
              <p className="text-xs opacity-70">Sin campos adicionales detectados.</p>
            ) : (
              <dl className="grid grid-cols-1 gap-x-4 gap-y-2 sm:grid-cols-2">
                {fields.map(({ field, value }) => (
                  <div key={field.label} className="flex items-baseline gap-1.5 text-xs">
                    <dt className="shrink-0 opacity-70">{field.label}:</dt>
                    {field.kind === 'state' ? (
                      <dd>
                        <StatusBadge tone={ocrFieldTone(value)} label={value.replace(/_/g, ' ')} />
                      </dd>
                    ) : (
                      <dd
                        className={`min-w-0 wrap-break-word ${field.strong ? 'font-bold' : ''}`}
                        style={
                          field.vin && rechazoPorVin ? { color: '#FF4E00', fontWeight: 700 } : undefined
                        }
                      >
                        {value}
                      </dd>
                    )}
                  </div>
                ))}
              </dl>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * Caja de subida por ítem del checklist. Presentacional + un input de archivo
 * oculto; valida cliente-side antes de delegar la subida al hook.
 * Exportada para reutilizar el mismo diseño en la sección Prenda.
 */
export function DocumentSlot({
  item,
  attachment,
  uploading,
  analyzing,
  deleting,
  ocr,
  onUpload,
  onRemove,
  onDefer,
  onPreview,
}: {
  item: ChecklistItemView;
  attachment: ProcedureAttachment | undefined;
  uploading: boolean;
  analyzing: boolean;
  deleting: boolean;
  ocr: OcrUiResult | undefined;
  onUpload: (file: File) => void;
  onRemove: (attachmentId: string) => void;
  /** Difiere (o revierte) la impronta al paso FUR. Solo se pasa para el slot de impronta. */
  onDefer?: (diferida: boolean) => Promise<void>;
  /** Abre el modal de previsualización para este adjunto. */
  onPreview?: (attachment: ProcedureAttachment) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [deferring, setDeferring] = useState(false);
  // En solo lectura el checklist es visualización: sin subir/reemplazar/borrar.
  const readOnly = useWizardReadOnly();

  const tipo = item.docTipo ?? item.key;
  const done = item.satisfied || !!attachment;
  const busy = uploading || analyzing || deleting;
  const isAuto = AUTO_DOC_TIPOS.has(tipo);

  // La impronta es un documento que se genera en el paso de firma (FUR), no se carga aquí. Cuando es
  // obligatoria y aún no hay adjunto, el operador puede diferir su generación marcando este check
  // (marca el ítem como satisfecho sin archivo). La radicación sigue exigiendo la impronta real.
  const canDefer =
    !!onDefer && item.docTipo === 'impronta' && item.obligatorio && !attachment && !readOnly;
  // Satisfecho sin adjunto ⇒ viene del flag manual de diferido (para impronta es la única vía).
  const deferred = item.satisfied && !attachment;

  const handleDefer = async (checked: boolean) => {
    if (!onDefer) return;
    setLocalError(null);
    setDeferring(true);
    try {
      await onDefer(checked);
    } catch {
      setLocalError('No se pudo actualizar la generación diferida de la impronta.');
    } finally {
      setDeferring(false);
    }
  };

  const handlePick = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // permite re-seleccionar el mismo archivo
    if (!file) return;
    // Pre-validación cliente con los límites del tipo (RF08/09): así el error sale inline y con el
    // límite real (no el global de 20 MB) y no llega al backend / cuadro global.
    const err = validateFile(file, {
      allowedMimes: item.mimeTypesAllowed,
      maxSizeBytes: item.maxSizeBytes,
    });
    setLocalError(err);
    if (err) return;
    onUpload(file);
  };

  return (
    <li
      className={
        'flex h-full flex-col rounded-2xl bg-white p-4 transition dark:bg-[#162744] ' +
        // Documentos auto-generados: siempre borde sólido (no hay archivo que esperar).
        // Documentos normales: borde punteado cuando falta adjunto → borde sólido al resolverse.
        (isAuto || done
          ? 'border shadow-sm hover:shadow-md'
          : 'border-2 border-dashed hover:border-[#557EFF] hover:bg-[#EFF6FF]')
      }
      style={{
        borderColor: isAuto
          ? 'rgba(85,126,255,0.35)'
          : done
            ? 'rgba(140,198,63,0.45)'
            : item.obligatorio
              ? 'rgba(255,78,0,0.35)'
              : '#DFE5ED',
      }}
    >
      <div className="flex min-w-0 items-start justify-between gap-2">
        <div className="flex min-w-0 items-start gap-2.5">
          <FileText
            className="mt-0.5 h-4 w-4 shrink-0"
            style={{ color: isAuto ? '#557EFF' : done ? '#8CC63F' : '#59677D' }}
            aria-hidden="true"
          />
          <div className="min-w-0 flex-1">
            <p className="text-xs font-bold leading-snug">{item.label}</p>
            {!isAuto && (
              <p
                className="mt-1 text-xs opacity-70"
                title="Formatos aceptados y tamaño máximo del archivo"
              >
                {ALLOWED_LABEL} · hasta{' '}
                {item.maxSizeBytes ? formatSize(item.maxSizeBytes) : '20 MB'}
              </p>
            )}
            {attachment && (
              <p className="mt-1 truncate text-xs opacity-70">
                {attachment.filename} · {formatSize(attachment.sizeBytes)}
              </p>
            )}
          </div>
        </div>
        {/* Insignias arriba a la derecha, como en la propuesta: el estado del documento se lee
            antes que su nombre cuando se barre la grilla en diagonal. */}
        <div className="flex shrink-0 flex-col items-end gap-1">
          {isAuto ? (
            <span title="El sistema genera este documento automáticamente; no es necesario adjuntarlo">
              <StatusBadge
                tone="info"
                className="uppercase tracking-wide"
                ariaLabel="Autogenerado por el sistema"
                label={
                  <span className="inline-flex items-center gap-1">
                    <span aria-hidden="true">⚙</span>
                    Autogenerado
                  </span>
                }
              />
            </span>
          ) : item.obligatorio ? (
            // B4 (guardián de diseño) — StatusBadge tone="danger" resuelve paleta con contraste AA.
            <span title="Este documento es obligatorio para radicar el trámite">
              <StatusBadge
                tone="danger"
                className="uppercase tracking-wide"
                label={
                  <span className="inline-flex items-center gap-1">
                    <span aria-hidden="true">●</span>
                    Obligatorio
                  </span>
                }
              />
            </span>
          ) : (
            <span
              className="whitespace-nowrap text-xs font-medium uppercase tracking-wide opacity-70"
              title="Este documento es opcional"
            >
              Opcional
            </span>
          )}
          {ocr ? <OcrStatusPanel tipo={tipo} ocr={ocr} /> : null}
        </div>
      </div>

      {/* Barra de avance del diseño: llena y verde cuando el documento está resuelto, en pulso
          azul mientras el OCR lo analiza. Es decorativa — el estado ya va escrito en la píldora. */}
      {(done || analyzing) && (
        <div
          className="mt-3 h-1.5 w-full overflow-hidden rounded-full"
          style={{ background: '#DFE5ED' }}
          aria-hidden="true"
        >
          <div
            className={`h-full rounded-full ${analyzing ? 'animate-pulse' : ''}`}
            style={{
              width: analyzing ? '60%' : '100%',
              background: analyzing ? '#557EFF' : '#8CC63F',
            }}
          />
        </div>
      )}

      <div className="mt-auto flex flex-wrap items-center gap-2 pt-3">
        {isAuto ? (
          // Documentos autogenerados: no hay archivo que adjuntar ni borrar.
          <p className="text-xs opacity-60 italic">
            Autogenerado por el sistema al radicar.
          </p>
        ) : (
        <>
        {/* Solo se muestra el badge cuando hay adjunto (PDF ajuste P0): el slot vacío ya usa
            borde punteado naranja como señal visual de "falta algo"; el badge "Sin adjuntar" era
            redundante y generaba ruido en la grilla. */}
        {done && <StatusBadge label="Adjunto" tone="success" />}

        {readOnly ? (
          attachment && onPreview ? (
            <button
              type="button"
              onClick={() => onPreview(attachment)}
              className="text-xs font-semibold"
              style={{ color: '#557EFF' }}
              aria-label={`Previsualizar ${item.label}`}
            >
              Ver
            </button>
          ) : null
        ) : (
          <>
            {attachment && onPreview && (
              <button
                type="button"
                onClick={() => onPreview(attachment)}
                className="rounded-lg border p-1 disabled:opacity-60"
                style={{ color: '#557EFF' }}
                aria-label={`Previsualizar ${item.label}`}
              >
                <Eye className="h-3.5 w-3.5" aria-hidden="true" />
              </button>
            )}
            <input
              ref={inputRef}
              type="file"
              accept={ALLOWED_MIME.join(',')}
              onChange={handlePick}
              className="hidden"
              aria-label={`Subir ${item.label}`}
            />
            {/* CTA con borde, no enlace: en la grilla de la propuesta es la única acción primaria
                de la tarjeta y tiene que competir con la insignia de obligatorio. */}
            <button
              type="button"
              onClick={() => inputRef.current?.click()}
              disabled={busy}
              className="rounded-lg border bg-white px-3 py-1.5 text-xs font-semibold transition hover:bg-[#EFF6FF] disabled:cursor-not-allowed disabled:opacity-60 dark:bg-transparent"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
            >
              {analyzing
                ? 'Analizando…'
                : uploading
                  ? 'Subiendo…'
                  : attachment
                    ? 'Reemplazar'
                    : 'Adjuntar'}
            </button>
            {attachment && (
              <button
                type="button"
                onClick={() => onRemove(attachment.id)}
                disabled={busy}
                className="text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-60"
                style={{ color: '#FF4E00' }}
                aria-label={`Borrar ${item.label}`}
              >
                {deleting ? 'Borrando…' : 'Borrar'}
              </button>
            )}
          </>
        )}
        </>
        )}
      </div>

      {canDefer && (
        <label className="mt-2 flex items-start gap-2 text-xs cursor-pointer">
          <input
            type="checkbox"
            checked={deferred}
            disabled={deferring}
            onChange={(e) => void handleDefer(e.target.checked)}
            className="mt-0.5"
          />
          <span className="opacity-80">
            La impronta se generará automáticamente en el paso de firma (FUR).
            {deferred && (
              <span className="block opacity-70">
                Marcada como diferida — se generará más adelante; no necesitas cargarla aquí.
              </span>
            )}
          </span>
        </label>
      )}

      {localError && (
        <p
          className="mt-1.5 text-xs"
          style={{ color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {localError}
        </p>
      )}
    </li>
  );
}

/**
 * Grid de subida de documentos guiado por el checklist del trámite.
 * Reutilizable: dado un `instanceId`, carga el checklist (qué docTipos exige
 * la tipología) y los adjuntos, marca ✓ los satisfechos, valida mime/tamaño
 * antes de subir y resume "faltan N obligatorios / completo".
 */
export function DocumentChecklist({
  instanceId,
  onChanged,
  hideHeader = false,
  modalidad,
}: Props) {
  const { state, refresh, upload, remove, clearError } = useProcedureDocuments(
    instanceId,
    { modalidad },
  );
  const { checklist, attachments, uploadingTipos, analyzingTipos, deletingId, ocrResults } =
    state;

  // Feature #11211 — modos mutuamente excluyentes: individual (checklist) vs batch (masivo).
  const [uploadMode, setUploadMode] = useState<'individual' | 'batch'>('individual');
  const batch = useProcedureBatchUpload(instanceId, { modalidad });
  const readOnly = useWizardReadOnly();

  const attachmentByTipo = new Map<string, ProcedureAttachment>();
  for (const a of attachments) {
    if (!attachmentByTipo.has(a.tipo)) attachmentByTipo.set(a.tipo, a);
  }

  // Prenda se carga en PrendaForm (`prenda_*` / `inscripcion_prenda`); no duplicar aquí.
  const items = (checklist?.items ?? []).filter((item) => {
    const tipo = item.docTipo ?? item.key;
    return !isPrendaManagedChecklistTipo(tipo);
  });
  const showModeToggle = !readOnly && !!instanceId && items.length > 0;

  // Preview modal state (HU #10703)
  const [previewAttachment, setPreviewAttachment] = useState<ProcedureAttachment | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const handlePreview = async (attachment: ProcedureAttachment) => {
    setPreviewAttachment(attachment);
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewError(null);
    setPreviewLoading(true);
    try {
      const result = await tramitesClient.fetchAttachmentPreviewUrl(
        instanceId ?? '',
        attachment.id,
      );
      // El file-manager sirve el objeto como binary/octet-stream sin Content-Disposition, por lo que
      // un <iframe> con la URL directa fuerza descarga. Re-empaquetamos los bytes como Blob con el
      // mimetype real para forzar el render inline en el navegador (S3 permite CORS GET).
      const blob = await fetch(result.url).then((r) => {
        if (!r.ok) throw new Error(String(r.status));
        return r.blob();
      });
      const typed = attachment.mimetype ? new Blob([blob], { type: attachment.mimetype }) : blob;
      setPreviewUrl(URL.createObjectURL(typed));
    } catch {
      setPreviewError('No se pudo obtener la URL de previsualización. Descarga el archivo en su lugar.');
    } finally {
      setPreviewLoading(false);
    }
  };

  const closePreview = () => {
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewAttachment(null);
    setPreviewError(null);
  };

  const handleDownloadFromPreview = async () => {
    if (!instanceId || !previewAttachment) return;
    try {
      const { blob, filename, mimetype } = await tramitesClient.downloadAttachment(
        instanceId,
        previewAttachment.id,
        undefined,
        previewAttachment.filename,
      );
      const objectUrl = URL.createObjectURL(new Blob([blob], { type: mimetype }));
      const a = document.createElement('a');
      a.href = objectUrl;
      a.download = filename;
      a.click();
      URL.revokeObjectURL(objectUrl);
    } catch {
      // silencioso — el usuario puede reintentar desde el listado
    }
  };

  // El OCR de la carga masiva analiza el lote entero y puede tardar bastante; mientras corre no hay
  // nada útil que hacer en la pantalla, así que la propuesta la cubre con la escena de espera. El
  // análisis de un documento suelto NO la levanta: su tarjeta ya muestra "Analizando…" con su barra,
  // y tapar la pantalla entera por un archivo impediría seguir adjuntando los demás.
  const analizandoLote = batch.state.phase === 'analyzing';

  return (
    <>
    {analizandoLote && <CarLoaderModal mode="ocr" />}
    <DocumentPreviewModal
      open={!!previewAttachment}
      onClose={closePreview}
      title={previewAttachment?.filename ?? 'Previsualización'}
      mimetype={previewAttachment?.mimetype ?? null}
      url={previewUrl}
      loading={previewLoading}
      error={previewError}
      onDownload={previewAttachment ? () => void handleDownloadFromPreview() : undefined}
    />
    <section
      className="rounded-2xl p-4 border bg-white dark:bg-[#162744]"
      aria-label="Documentos del trámite"
    >
      {/* Embebido en el asistente el paso ya tiene su título, pero la tarjeta necesita el suyo:
          desde el rediseño comparte el paso con las declaraciones, la prenda y las observaciones,
          y sin nombre propio las cuatro se leían como un único bloque continuo. */}
      <WizardCardHeader
        title={hideHeader ? 'Gestión de documentos' : 'Documentos requeridos'}
        subtitle={
          hideHeader
            ? undefined
            : `Adjunta los documentos que exige el trámite (${ALLOWED_LABEL}, máx 20 MB).`
        }
      />

      {/* Banda de completitud, no píldora en la esquina: es la respuesta a "¿ya puedo seguir?" y
          en la propuesta ocupa el ancho de la tarjeta, con el ámbar de lo que falta o el verde de
          lo resuelto. */}
      {checklist &&
        (() => {
          // Los tonos salen de INLINE_ALERT_TONES y no de hex sueltos: es la misma paleta con la
          // que el resto del asistente pinta "todo bien" y "falta algo".
          const tono = INLINE_ALERT_TONES[checklist.completo ? 'success' : 'warning'];
          const Icono = tono.Icon;
          return (
            <div
              className="mb-3 flex items-center gap-2.5 rounded-xl px-4 py-3"
              style={{ background: tono.background, border: `1px solid ${tono.border}` }}
              role="status"
              aria-live="polite"
            >
              <Icono className="h-4 w-4 shrink-0" style={{ color: tono.color }} aria-hidden="true" />
              <span className="text-xs font-bold" style={{ color: tono.color }}>
                {checklist.completo
                  ? 'Documentos completos'
                  : `Faltan ${checklist.faltanObligatorios} obligatorio${
                      checklist.faltanObligatorios === 1 ? '' : 's'
                    }`}
              </span>
            </div>
          );
        })()}

      {state.error && (
        <div
          className="rounded-xl p-3 text-xs border mb-3 flex items-center justify-between gap-3"
          style={{
            borderColor: '#FF4E00',
            background: 'rgba(255,78,0,0.06)',
            color: '#FF4E00',
          }}
          role="alert"
          aria-live="polite"
        >
          <span>{state.error}</span>
          <button
            type="button"
            onClick={clearError}
            className="font-bold"
            aria-label="Descartar error"
          >
            ×
          </button>
        </div>
      )}

      {/* Feature #11211 — selector de modo (excluyente) + UI correspondiente. Default: uno a uno. */}
      {showModeToggle && (
        <div className="mb-3 space-y-2">
          {/* Pista segmentada como en la propuesta (Carga Individual / Carga Masiva): las dos
              opciones son excluyentes y cambian lo que se ve debajo, así que conviene verlas
              juntas y no como dos píldoras sueltas que se leen como filtros. */}
          <WizardSegmented
            label="Modo de cargue de documentos"
            value={uploadMode}
            options={[
              { value: 'individual', label: 'Uno a uno' },
              { value: 'batch', label: 'Masivo' },
            ]}
            onChange={setUploadMode}
          />
          <p className="text-xs opacity-70" role="note">
            {uploadMode === 'individual'
              ? 'Puedes cargar cada documento en su casilla, uno a uno. También puedes cambiar a Masivo para subir varios archivos juntos.'
              : 'Carga varios archivos a la vez: el sistema los clasifica y tú confirmas. Las casillas de abajo siguen disponibles por si falta alguno.'}
          </p>
        </div>
      )}

      {showModeToggle && uploadMode === 'batch' && (
        <div className="mb-3">
          {batch.state.phase === 'reviewing' || batch.state.phase === 'uploading' ? (
            <BatchReviewPanel
              state={batch.state}
              aceptadas={batch.aceptadas}
              onToggle={batch.setDecision}
              onCancel={batch.reset}
              onConfirm={() =>
                void batch.confirm().then((ok) => {
                  void refresh();
                  onChanged?.();
                  return ok;
                })
              }
            />
          ) : (
            <BatchDropzone
              busy={batch.state.phase === 'analyzing'}
              onFiles={(files) => void batch.analyze(files, items, attachments)}
            />
          )}

          {batch.state.error && (
            <div
              className="mt-2 flex items-center justify-between gap-3 rounded-xl border p-3 text-xs"
              style={{
                borderColor: '#FF4E00',
                background: 'rgba(255,78,0,0.06)',
                color: '#FF4E00',
              }}
              role="alert"
              aria-live="polite"
            >
              <span>{batch.state.error}</span>
              <button
                type="button"
                onClick={batch.clearError}
                className="font-bold"
                aria-label="Descartar error de la carga masiva"
              >
                ×
              </button>
            </div>
          )}
        </div>
      )}

      {items.length === 0 ? (
        <p className="text-xs opacity-70">
          {state.loading
            ? 'Cargando documentos requeridos…'
            : 'Este trámite no requiere documentos.'}
        </p>
      ) : (
        <ul
          // Grid parejo: 1 item → 1 col; 2 items → 2 cols; 3+ items → 3 cols (max).
          // Para n=4 o n=5 la última fila queda 1 o 2 celdas anchas pero eso es correcto
          // (CSS Grid rellena izquierda–derecha). Siempre 3 cols para n≥6 → grilla simétrica.
          className={`grid gap-3 ${
            items.length === 1
              ? 'grid-cols-1'
              : items.length === 2
                ? 'grid-cols-1 sm:grid-cols-2'
                : 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3'
          }`}
          aria-label="Checklist de documentos"
        >
          {[...items]
            .sort((a, b) => {
              if (a.obligatorio !== b.obligatorio) return a.obligatorio ? -1 : 1;
              return a.label.localeCompare(b.label, 'es', { sensitivity: 'base' });
            })
            .map((item) => {
            const tipo = item.docTipo ?? item.key;
            const attachment = attachmentByTipo.get(tipo);
            return (
              <DocumentSlot
                key={item.key}
                item={item}
                attachment={attachment}
                uploading={uploadingTipos.has(tipo)}
                analyzing={analyzingTipos.has(tipo)}
                deleting={!!attachment && deletingId === attachment.id}
                ocr={ocrResults[tipo]}
                onUpload={(file) =>
                  void upload(tipo, file).then((ok) => {
                    if (ok) onChanged?.();
                  })
                }
                onRemove={(id) =>
                  void remove(id).then((ok) => {
                    if (ok) onChanged?.();
                  })
                }
                onDefer={
                  instanceId
                    ? async (diferida) => {
                        await tramitesClient.setImprontaDiferida(instanceId, diferida);
                        // Refresca el checklist propio del componente (de él sale item.satisfied,
                        // que controla el check); sin esto el estado queda obsoleto y el check no
                        // se marca aunque el backend haya guardado. Igual que hacen upload/remove.
                        await refresh();
                        onChanged?.();
                      }
                    : undefined
                }
                onPreview={instanceId ? (att) => void handlePreview(att) : undefined}
              />
            );
          })}
        </ul>
      )}
    </section>
    </>
  );
}

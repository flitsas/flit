'use client';

import { useRef, useState } from 'react';
import { Eye } from 'lucide-react';
import {
  useProcedureDocuments,
  type OcrUiResult,
} from '@/hooks/useProcedureDocuments';
import { useWizardReadOnly } from './WizardReadOnlyContext';
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
};

/** Nombre corto del tipo para el encabezado de la tarjeta. */
const TIPO_LABEL: Record<string, string> = {
  factura: 'Factura',
  aduana: 'Aduana',
  impronta: 'Impronta',
  soat: 'SOAT',
};

/** Colores del chip de estado (coincide / no_coincide / no_aplica / no_verificado). */
function stateChipStyle(value: string): { color: string; background: string } {
  const v = value.toLowerCase();
  if (v === 'coincide') return { color: '#3B8A00', background: 'rgba(140,198,63,0.20)' };
  if (v === 'no_coincide') return { color: '#FF4E00', background: 'rgba(255,78,0,0.12)' };
  if (v === 'no_aplica') return { color: '#5B6472', background: 'rgba(120,130,145,0.15)' };
  return { color: '#B77900', background: 'rgba(249,172,0,0.18)' }; // no_verificado / otros
}

/** Chip "PDF recortado (X/Y págs)" cuando el OCR recortó un subconjunto de páginas. */
function recorteLabel(data: Record<string, unknown> | null): string | null {
  if (!data || data._paginas_extraidas !== true) return null;
  const extraidas = Array.isArray(data.paginas_documento) ? data.paginas_documento.length : null;
  const originales = typeof data._paginas_originales === 'number' ? data._paginas_originales : null;
  return extraidas && originales ? `PDF recortado (${extraidas}/${originales} págs)` : 'PDF recortado';
}

/**
 * Tarjeta de estado OCR de un documento: encabezado (verificado/rechazado/no analizado) + chip del
 * tipo, motivo cuando aplica, y una grilla legible de pares etiqueta/valor (set ampliado por tipo).
 */
function OcrStatusPanel({ tipo, ocr }: { tipo: string; ocr: OcrUiResult }) {
  const palette =
    ocr.status === 'verified'
      ? { color: '#3B8A00', border: '#8CC63F', bg: 'rgba(140,198,63,0.08)', icon: '✓', label: 'Verificado' }
      : ocr.status === 'rejected'
        ? { color: '#FF4E00', border: '#FF4E00', bg: 'rgba(255,78,0,0.06)', icon: '✗', label: 'Rechazado' }
        : { color: '#B77900', border: '#F9AC00', bg: 'rgba(249,172,0,0.08)', icon: '!', label: 'No analizado' };

  const data = ocr.data;
  const nombre = TIPO_LABEL[tipo] ?? tipo;
  const tipoDocumento = data ? pickStr(data, 'tipo_documento') : '';
  const recorte = recorteLabel(data);
  const rechazoPorVin = ocr.status === 'rejected' && !!ocr.motivo && /VIN/i.test(ocr.motivo);

  const fields = data
    ? (OCR_RESUMEN_FIELDS[tipo] ?? [])
        .map((field) => ({ field, value: field.value(data) }))
        .filter((x) => x.value !== '')
    : [];

  return (
    <div
      className="mt-2 rounded-lg border p-2.5"
      style={{ background: palette.bg, borderColor: palette.border }}
      role="status"
      aria-live="polite"
    >
      {/* Encabezado: estado + chip del tipo + chip de recorte */}
      <div className="flex flex-wrap items-center gap-1.5">
        <span className="text-[11px] font-bold" style={{ color: palette.color }}>
          <span aria-hidden="true">{palette.icon}</span> {nombre} — {palette.label}
        </span>
        {tipoDocumento && (
          <span
            className="rounded px-1.5 py-0.5 text-[9px] font-semibold"
            style={{ background: 'rgba(85,126,255,0.10)', color: '#557EFF' }}
          >
            {tipoDocumento}
          </span>
        )}
        {recorte && (
          <span
            className="rounded px-1.5 py-0.5 text-[9px] font-semibold"
            style={{ background: 'rgba(85,126,255,0.10)', color: '#557EFF' }}
          >
            {recorte}
          </span>
        )}
      </div>

      {/* Motivo (rechazado / no analizado) */}
      {ocr.motivo && (
        <p className="mt-1 text-[10px] font-medium" style={{ color: palette.color }}>
          {ocr.motivo}
        </p>
      )}

      {/* Grilla de campos: 2 columnas, etiqueta mutada + valor con buen contraste */}
      {fields.length > 0 && (
        <dl className="mt-1.5 grid grid-cols-1 gap-x-4 gap-y-1 sm:grid-cols-2">
          {fields.map(({ field, value }) => (
            <div key={field.label} className="flex items-baseline gap-1.5 text-[11px]">
              <dt className="shrink-0 opacity-60">{field.label}:</dt>
              {field.kind === 'state' ? (
                <dd>
                  <span
                    className="rounded px-1.5 py-0.5 text-[10px] font-semibold"
                    style={stateChipStyle(value)}
                  >
                    {value.replace(/_/g, ' ')}
                  </span>
                </dd>
              ) : (
                <dd
                  className={`min-w-0 wrap-break-word ${field.strong ? 'font-bold' : ''}`}
                  style={field.vin && rechazoPorVin ? { color: '#FF4E00', fontWeight: 700 } : undefined}
                >
                  {value}
                </dd>
              )}
            </div>
          ))}
        </dl>
      )}
    </div>
  );
}

/**
 * Caja de subida por ítem del checklist. Presentacional + un input de archivo
 * oculto; valida cliente-side antes de delegar la subida al hook.
 */
function DocumentSlot({
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
      className="rounded-xl border p-3"
      style={{ borderColor: done ? '#8CC63F' : '#DFE5ED' }}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-1.5">
            {done && (
              <span style={{ color: '#8CC63F' }} aria-hidden="true">
                ✓
              </span>
            )}
            <span className="text-xs font-semibold">{item.label}</span>
            {item.obligatorio ? (
              <span
                className="rounded px-1.5 py-0.5 text-[9px] font-bold uppercase"
                style={{ background: 'rgba(255,78,0,0.10)', color: '#FF4E00' }}
              >
                Obligatorio
              </span>
            ) : (
              <span className="text-[10px] opacity-50 font-normal">
                (opcional)
              </span>
            )}
          </div>
          {attachment && (
            <p className="mt-1 text-[11px] opacity-70 truncate">
              {attachment.filename} · {formatSize(attachment.sizeBytes)}
            </p>
          )}
        </div>

        {readOnly ? (
          <div className="flex shrink-0 items-center gap-2">
            <span className="text-[11px] font-semibold opacity-60">
              {done ? 'Adjunto' : 'Sin adjuntar'}
            </span>
            {attachment && onPreview && (
              <button
                type="button"
                onClick={() => onPreview(attachment)}
                className="rounded-lg border p-1.5 disabled:opacity-60"
                style={{ borderColor: '#DFE5ED', color: '#557EFF' }}
                aria-label={`Previsualizar ${item.label}`}
              >
                <Eye className="h-3.5 w-3.5" aria-hidden="true" />
              </button>
            )}
          </div>
        ) : (
          <div className="flex shrink-0 items-center gap-2">
            {attachment && onPreview && (
              <button
                type="button"
                onClick={() => onPreview(attachment)}
                className="rounded-lg border p-1.5 disabled:opacity-60"
                style={{ borderColor: '#DFE5ED', color: '#557EFF' }}
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
            <button
              type="button"
              onClick={() => inputRef.current?.click()}
              disabled={busy}
              className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:cursor-not-allowed disabled:opacity-60"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
            >
              {analyzing
                ? 'Analizando…'
                : uploading
                  ? 'Subiendo…'
                  : attachment
                    ? 'Reemplazar'
                    : 'Subir'}
            </button>
            {attachment && (
              <button
                type="button"
                onClick={() => onRemove(attachment.id)}
                disabled={busy}
                className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:cursor-not-allowed disabled:opacity-60"
                style={{ borderColor: '#FF4E00', color: '#FF4E00' }}
                aria-label={`Borrar ${item.label}`}
              >
                {deleting ? 'Borrando…' : 'Borrar'}
              </button>
            )}
          </div>
        )}
      </div>

      {canDefer && (
        <label className="mt-2 flex items-start gap-2 text-[11px] cursor-pointer">
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
              <span className="block opacity-60">
                Marcada como diferida — se generará más adelante; no necesitas cargarla aquí.
              </span>
            )}
          </span>
        </label>
      )}

      {localError && (
        <p
          className="mt-1.5 text-[10px]"
          style={{ color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {localError}
        </p>
      )}

      {ocr && <OcrStatusPanel tipo={tipo} ocr={ocr} />}
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
  const { checklist, attachments, uploadingTipo, analyzingTipo, deletingId, ocrResults } =
    state;

  const attachmentByTipo = new Map<string, ProcedureAttachment>();
  for (const a of attachments) {
    if (!attachmentByTipo.has(a.tipo)) attachmentByTipo.set(a.tipo, a);
  }

  const items = checklist?.items ?? [];

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

  return (
    <>
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
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14] mt-4"
      aria-label="Documentos del trámite"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        {hideHeader ? (
          <div />
        ) : (
          <div>
            <h4 className="text-sm font-bold">Documentos requeridos</h4>
            <p className="text-[11px] opacity-60">
              Adjunta los documentos que exige el trámite ({ALLOWED_LABEL}, máx
              20 MB).
            </p>
          </div>
        )}
        {checklist && (
          <span
            className="shrink-0 rounded-full px-3 py-1 text-[11px] font-bold"
            style={
              checklist.completo
                ? { background: 'rgba(140,198,63,0.15)', color: '#8CC63F' }
                : { background: 'rgba(249,172,0,0.15)', color: '#F9AC00' }
            }
            role="status"
            aria-live="polite"
          >
            {checklist.completo
              ? 'Documentos completos'
              : `Faltan ${checklist.faltanObligatorios} obligatorio${
                  checklist.faltanObligatorios === 1 ? '' : 's'
                }`}
          </span>
        )}
      </div>

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

      {items.length === 0 ? (
        <p className="text-[11px] opacity-60">
          {state.loading
            ? 'Cargando documentos requeridos…'
            : 'Este trámite no requiere documentos.'}
        </p>
      ) : (
        <ul className="space-y-2" aria-label="Checklist de documentos">
          {items.map((item) => {
            const tipo = item.docTipo ?? item.key;
            const attachment = attachmentByTipo.get(tipo);
            return (
              <DocumentSlot
                key={item.key}
                item={item}
                attachment={attachment}
                uploading={uploadingTipo === tipo}
                analyzing={analyzingTipo === tipo}
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

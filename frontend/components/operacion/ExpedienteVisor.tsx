'use client';

import { useId, useState, type ReactNode } from 'react';
import { ChevronDown, Download, FileText } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { documentLabel } from '@/lib/tramites/document-labels';
import type {
  InstanceStatus,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

// Expediente digital: documentos del trámite. Vehículo, actores y validación
// viven en MatriculaResumen. El organismo de tránsito no se muestra aquí
// (se elige en el paso 1 o lo fija el RUNT en traspaso).

interface Props {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  modalidad?: WizardModalidad;
  status?: InstanceStatus;
  onBeforeGenerateConsolidado?: () => Promise<void>;
  onAttachmentsChange?: () => void;
}

const BLUE = '#557EFF';
const BORDER = '#DFE5ED';

/** Solo el consolidado del wizard (`tipo === 'consolidado'`). Nunca el maestro ni fuzzy match. */
export function findConsolidadoAttachment(
  attachments: ProcedureAttachment[],
): ProcedureAttachment | undefined {
  return attachments.find((a) => (a.tipo ?? '').toLowerCase() === 'consolidado');
}

function ExpedienteDisclosure({
  title,
  defaultOpen = true,
  children,
}: {
  title: string;
  defaultOpen?: boolean;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const panelId = useId();

  return (
    <div
      className="overflow-hidden rounded-xl border bg-white dark:bg-[#0B0F14]"
      style={{ borderColor: BORDER }}
    >
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center justify-between gap-2 px-4 py-3 text-left"
        aria-expanded={open}
        aria-controls={panelId}
      >
        <span className="flex items-center gap-2">
          <span className="h-4 w-1 rounded-full" style={{ background: BLUE }} aria-hidden="true" />
          <span className="text-xs font-bold uppercase tracking-[0.2em]" style={{ color: BLUE }}>
            {title}
          </span>
        </span>
        <ChevronDown
          className={`h-4 w-4 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`}
          style={{ color: '#9AA5B1' }}
          aria-hidden
        />
      </button>
      {open ? (
        <div
          id={panelId}
          className="border-t px-4 py-4"
          style={{ borderColor: BORDER }}
          role="region"
          aria-label={title}
        >
          {children}
        </div>
      ) : null}
    </div>
  );
}

function openObjectUrlInWindow(url: string, existingWin: Window | null) {
  if (existingWin && !existingWin.closed) {
    existingWin.location.replace(url);
    try {
      existingWin.opener = null;
    } catch {
      // ignore
    }
    return;
  }
  const win = window.open(url, '_blank');
  if (win) {
    try {
      win.opener = null;
    } catch {
      // ignore
    }
    return;
  }
  const a = document.createElement('a');
  a.href = url;
  a.target = '_blank';
  a.rel = 'noopener noreferrer';
  document.body.appendChild(a);
  a.click();
  a.remove();
}

/**
 * HTML autocontenido del loader de apertura de documentos (about:blank).
 * Fuente visual: circular-journey-loader (CarLoader) — pista azul estática,
 * vehículo turquesa e arco externo en sentidos opuestos.
 */
function buildDocumentTabHtml(
  kind: 'loading' | 'error',
  message: string,
): string {
  const isError = kind === 'error';
  const safeMsg = message.replace(/</g, '&lt;').replace(/"/g, '&quot;');
  const title = message.replace(/</g, '');
  const track = isError ? '#FF4E00' : '#557eff';
  const accent = isError ? '#FF4E00' : '#00dbd5';
  const textColor = isError ? '#FF4E00' : '#557eff';
  const animCss = isError
    ? ''
    : `
  .car-loader__car-spin {
    animation: car-loader-spin-ccw 2s linear infinite;
    transform-box: view-box;
    transform-origin: 50% 50%;
  }
  .car-loader__arc-spin {
    animation: car-loader-spin-cw 2s linear infinite;
    transform-box: view-box;
    transform-origin: 50% 50%;
  }
  .car-loader__dot {
    display: inline-block;
    animation: car-loader-dot-blink 1.2s infinite ease-in-out both;
  }`;

  return `<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>${title}</title>
<link rel="preconnect" href="https://fonts.googleapis.com"/>
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin/>
<link href="https://fonts.googleapis.com/css2?family=Poppins:wght@400&display=swap" rel="stylesheet"/>
<style>
  @keyframes car-loader-spin-cw { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
  @keyframes car-loader-spin-ccw { from { transform: rotate(0deg); } to { transform: rotate(-360deg); } }
  @keyframes car-loader-dot-blink { 0%, 100% { opacity: 0; } 50% { opacity: 1; } }
  html, body { margin: 0; height: 100%; background: #fcfbf8; }
  body {
    display: flex;
    align-items: center;
    justify-content: center;
    -webkit-font-smoothing: antialiased;
  }
  .car-loader__wrapper {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    line-height: 0;
  }
  .car-loader__wrapper > svg { display: block; }
  .car-loader__text {
    margin-top: 2px;
    font-family: "Poppins", ui-sans-serif, system-ui, sans-serif;
    font-weight: 400;
    font-size: 16px;
    color: ${textColor};
    letter-spacing: 0.02em;
    text-align: center;
    line-height: 1;
  }
  .car-loader__word { position: relative; display: inline-block; }
  .car-loader__dots {
    position: absolute;
    left: 100%;
    top: 0;
    white-space: nowrap;
  }
  ${animCss}
</style>
</head>
<body>
  <div class="car-loader__wrapper" role="status" aria-live="polite" aria-label="${safeMsg}">
    <svg width="240" height="240" viewBox="0 0 300 300" aria-hidden="true">
      <circle cx="150" cy="150" r="90" fill="none" stroke="${track}" stroke-width="45"/>
      <g class="car-loader__arc-spin" style="transform-origin: 150px 150px">
        <g transform="translate(115.6 264) scale(1)">
          <path
            d="M1.91,2.5c10,2.91,20.57,4.47,31.51,4.47,11.66,0,22.9-1.77,33.47-5.06"
            fill="none"
            stroke="${accent}"
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="3.82"
          />
        </g>
      </g>
      <g class="car-loader__car-spin" style="transform-origin: 150px 150px">
        <g transform="translate(126.8 238.3)">
          <path d="M28.98,23.24l1.65-2-23.84.24c-2.82.03-5.02-.89-5.92-3.78-1.18-3.78-1.18-8.19,0-11.97C1.78,2.84,3.99,1.92,6.8,1.95l23.84.23-1.74-2c1.37-.43,2.22-.08,3.01.96,1.71,2.26,5.84-.13,9.56,1.16s5,4.97,4.94,8.41c-.04,2.32.03,4.68-.88,6.56-2.86,5.88-7.47,3.78-12.86,4.14-1.11.99-1.19,2.53-3.68,1.83Z" fill="${accent}"/>
          <path d="M34.29,19.21l-6.11-.63-.02-13.71,6.02-.67c3.62,3.1,3.76,11.42.11,15.01Z" fill="#fff"/>
          <path d="M9.97,17.35c-1.36.32-3.75.15-4.25-1.26-.95-2.68-.95-6.07,0-8.76.49-1.39,2.85-1.52,4.19-1.3.47,3.84.44,7.2.06,11.32Z" fill="#fff"/>
          <path d="M18.71,5.69l-6.55-.07c1.87-1.38,3.88-1.36,6.42-1.49l.13,1.56Z" fill="#fff"/>
          <path d="M18.64,17.68l.06,1.59c-2.34-.07-4.43-.05-6.57-1.45l6.51-.14Z" fill="#fff"/>
          <path d="M26.49,4.76l-7.12.88-.08-1.53c2.29,0,4.37-.1,7.2.65Z" fill="#fff"/>
          <path d="M26.48,18.68c-2.26.64-4.33.59-7.13.62v-1.54s7.13.92,7.13.92Z" fill="#fff"/>
          <path d="M43.71,5.45l.05,3.33c-1.46-.76-1.86-2.49-1.6-4.4.28-.37,1.54.5,1.55,1.06Z" fill="#fff"/>
          <path d="M43.72,17.94c0,.53-1.12,1.36-1.51,1.23-.32-1.73-.12-3.51,1.53-4.53l-.03,3.31Z" fill="#fff"/>
          <path d="M3.38,5.73c-.12.86-.39,1.67-1.53,2.14-.07-.97.19-2.49.5-3.23.06-.14,1.22-.28,1.2-.14l-.17,1.23Z" fill="#fff"/>
          <path d="M3.33,17.37l.25,1.32c.04.21-1.08.31-1.18.11-.32-.65-.6-2.06-.64-3.1.87-.05,1.41.84,1.57,1.67Z" fill="#fff"/>
        </g>
      </g>
    </svg>
    <span class="car-loader__text">
      <span class="car-loader__word">
        ${isError ? safeMsg : 'Cargando'}
        ${
          isError
            ? ''
            : `<span class="car-loader__dots" aria-hidden="true">
          <span class="car-loader__dot" style="animation-delay:0s">.</span>
          <span class="car-loader__dot" style="animation-delay:0.3s">.</span>
          <span class="car-loader__dot" style="animation-delay:0.6s">.</span>
        </span>`
        }
      </span>
    </span>
  </div>
</body>
</html>`;
}

function openLoadingTab(): Window | null {
  // Abrir en el gesto del usuario evita bloqueo de popup y da feedback inmediato
  // mientras llega el PDF (consolidado / archivos grandes).
  const win = window.open('about:blank', '_blank');
  if (!win) return null;
  try {
    win.opener = null;
    win.document.open();
    win.document.write(buildDocumentTabHtml('loading', 'Cargando documento…'));
    win.document.close();
  } catch {
    // Si el navegador no deja escribir en about:blank, igual usamos la ventana.
  }
  return win;
}

/**
 * Abre el adjunto en pestaña nueva lo más rápido posible:
 * 1) abre la pestaña al instante (gesto del usuario),
 * 2) pide URL prefirmada (sin proxy del binario por core-api),
 * 3) baja desde storage y re-empaqueta el Blob con el MIME real (PDF inline).
 * Fallback a /download si falla preview-url.
 */
export async function openAttachmentInNewTab(
  instanceId: string,
  attachment: Pick<ProcedureAttachment, 'id' | 'tipo' | 'filename' | 'mimetype'>,
) {
  const win = openLoadingTab();
  const mime =
    attachment.mimetype?.trim() ||
    (attachment.tipo === 'consolidado' || (attachment.filename ?? '').toLowerCase().endsWith('.pdf')
      ? 'application/pdf'
      : 'application/octet-stream');

  try {
    let blob: Blob;
    try {
      const preview = await tramitesClient.fetchAttachmentPreviewUrl(
        instanceId,
        attachment.id,
      );
      if (!preview?.url) throw new Error('preview_url_empty');
      const raw = await fetch(preview.url).then((r) => {
        if (!r.ok) throw new Error(`storage_${r.status}`);
        return r.blob();
      });
      blob = new Blob([raw], { type: mime });
    } catch {
      const downloaded = await tramitesClient.downloadAttachment(
        instanceId,
        attachment.id,
        undefined,
        attachment.filename,
      );
      blob = downloaded.mimetype
        ? new Blob([downloaded.blob], { type: downloaded.mimetype })
        : new Blob([downloaded.blob], { type: mime });
    }

    const objectUrl = URL.createObjectURL(blob);
    openObjectUrlInWindow(objectUrl, win);
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 120_000);
  } catch (err) {
    if (win && !win.closed) {
      try {
        win.document.open();
        win.document.write(
          buildDocumentTabHtml('error', 'No se pudo abrir el documento.'),
        );
        win.document.close();
      } catch {
        win.close();
      }
    }
    throw err;
  }
}

export default function ExpedienteVisor({
  instanceId,
  attachments,
  modalidad = 'matricula_inicial',
  status = 'borrador',
  onBeforeGenerateConsolidado,
  onAttachmentsChange,
}: Props) {
  return (
    <section aria-label="Expediente digital" className="space-y-3">
      <DocumentosSection
        instanceId={instanceId}
        attachments={attachments}
        modalidad={modalidad}
        status={status}
        onBeforeGenerateConsolidado={onBeforeGenerateConsolidado}
        onAttachmentsChange={onAttachmentsChange}
      />
    </section>
  );
}

function consolidadoAvisoLabel(aviso: string): string {
  const [documento, motivo = ''] = aviso.split(':').map((s) => s.trim());
  const nombre =
    documento === 'documentos_del_expediente'
      ? 'algunos documentos del expediente'
      : documentLabel(documento);
  const causa =
    motivo.includes('organismo_requerido')
      ? ' (falta el organismo de tránsito)'
      : motivo.includes('provider_unavailable')
        ? ' (el proveedor no está disponible; vuelve a generar el expediente en unos minutos)'
        : motivo.includes('provider_validation')
          ? ' (el proveedor rechazó los datos del trámite)'
          : motivo
            ? ` (${motivo})`
            : '';
  return `${nombre}${causa}`;
}

function DocumentosSection({
  instanceId,
  attachments,
  modalidad,
  status,
  onBeforeGenerateConsolidado,
  onAttachmentsChange,
}: {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  modalidad: WizardModalidad;
  status: InstanceStatus;
  onBeforeGenerateConsolidado?: () => Promise<void>;
  onAttachmentsChange?: () => void;
}) {
  const consolidado = findConsolidadoAttachment(attachments);
  const [generating, setGenerating] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const estadoFinal = status === 'aprobado' || status === 'anulado';
  const busy = generating || downloading;

  const applyAvisos = (generado: Awaited<ReturnType<typeof tramitesClient.generarConsolidado>>) => {
    const avisos: string[] = [];
    if (generado?.incompleto) {
      const faltantes = (generado.documentosFaltantes ?? []).map(documentLabel).join(', ');
      avisos.push(
        faltantes
          ? `Faltan documentos obligatorios: ${faltantes}.`
          : 'Faltan documentos obligatorios.',
      );
    }
    for (const aviso of generado?.avisosCascada ?? []) {
      avisos.push(`No se pudo generar ${consolidadoAvisoLabel(aviso)}.`);
    }
    if (avisos.length > 0) {
      setError(`Expediente consolidado generado. ${avisos.join(' ')}`);
    }
  };

  const consolidadoIdFromResult = (
    generado: Awaited<ReturnType<typeof tramitesClient.generarConsolidado>>,
  ): { id: string; filename: string } | null => {
    const nested = generado?.document;
    if (nested?.attachmentId) {
      return { id: nested.attachmentId, filename: nested.filename || 'consolidado.pdf' };
    }
    // Compat con respuestas planas (mocks / clientes antiguos).
    const flat = generado as { attachmentId?: string; filename?: string } | null | undefined;
    if (flat?.attachmentId) {
      return { id: flat.attachmentId, filename: flat.filename || 'consolidado.pdf' };
    }
    return null;
  };

  const handleGenerate = async () => {
    if (!instanceId) return;
    setGenerating(true);
    setError(null);
    try {
      await onBeforeGenerateConsolidado?.();
      // force=true: invalida caché y reconstruye sin anidar un consolidado previo (evita docs duplicados).
      const generado = await tramitesClient.generarConsolidado(instanceId, undefined, true);
      applyAvisos(generado);
      onAttachmentsChange?.();
    } catch (err) {
      const msg = (err instanceof Error ? err.message : '').trim();
      setError(
        msg.includes('generacion_bloqueada_estado_final')
          ? 'El trámite ya está aprobado o anulado: su documentación es definitiva y no se regenera.'
          : msg ||
              'No se pudo generar el consolidado. Revisa la conexión e inténtalo de nuevo.',
      );
    } finally {
      setGenerating(false);
    }
  };

  /**
   * Siempre regenera con force=true y abre el PDF nuevo.
   * Abrir un consolidado cacheado podía mostrar todos los documentos duplicados si se había
   * generado anidando un consolidado_maestro u otro paquete previo.
   */
  const handleDescargarTodo = async () => {
    if (!instanceId) return;
    setDownloading(true);
    setError(null);
    try {
      await onBeforeGenerateConsolidado?.();
      const generado = await tramitesClient.generarConsolidado(instanceId, undefined, true);
      applyAvisos(generado);
      const doc = consolidadoIdFromResult(generado);
      if (doc) {
        await openAttachmentInNewTab(instanceId, {
          id: doc.id,
          tipo: 'consolidado',
          filename: doc.filename,
          mimetype: 'application/pdf',
          sizeBytes: 0,
          sha256: '',
          source: 'system',
          uploadedAt: new Date().toISOString(),
        });
      }
      onAttachmentsChange?.();
    } catch (err) {
      const msg = (err instanceof Error ? err.message : '').trim();
      setError(
        msg.includes('generacion_bloqueada_estado_final')
          ? 'El trámite ya está aprobado o anulado: su documentación es definitiva y no se regenera.'
          : msg ||
              'No se pudo generar el consolidado. Revisa la conexión e inténtalo de nuevo.',
      );
    } finally {
      setDownloading(false);
    }
  };

  const gradientBtnClass =
    'inline-flex items-center justify-center rounded-full px-6 py-2.5 text-xs font-semibold text-white transition hover:opacity-95 disabled:opacity-50';
  const gradientBtnStyle = { background: 'linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)' };

  return (
    <ExpedienteDisclosure title="Documentos">
      {attachments.length > 0 ? (
        <ul className="space-y-2.5" aria-label="Documentos del expediente (visor)">
          {attachments.map((a) => (
            <DocRow key={a.id} instanceId={instanceId} attachment={a} />
          ))}
        </ul>
      ) : (
        <p className="text-[11px] opacity-60">No se han cargado documentos.</p>
      )}

      <div className="mt-4 space-y-3 border-t pt-4" style={{ borderColor: BORDER }}>
        <div>
          <h5 className="text-xs font-bold" style={{ color: '#162744' }}>
            Expediente consolidado
          </h5>
          <p className="mt-1 text-[11px] opacity-70">
            Un solo PDF con el FUR, el certificado de identidad, la impronta y los documentos
            cargados en el trámite
            {modalidad === 'traspaso' ? ' (incluye el contrato de compraventa)' : ''}. Al generarlo se
            producen también los documentos que falten.
          </p>
        </div>

        {error && (
          <div
            className="rounded-xl border p-3 text-xs"
            style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
            role="alert"
            aria-live="polite"
          >
            {error}
          </div>
        )}

        {estadoFinal ? (
          <p className="text-[11px] font-medium" style={{ color: '#557EFF' }} role="status">
            El trámite ya está {status === 'aprobado' ? 'aprobado' : 'anulado'}: su documentación es
            definitiva. Puedes consultarla y descargarla.
          </p>
        ) : null}

        <div className="flex flex-wrap items-center gap-2">
          {!estadoFinal && consolidado ? (
            <button
              type="button"
              onClick={() => void handleGenerate()}
              disabled={busy || !instanceId}
              className="rounded-full px-5 py-2.5 text-xs font-semibold text-white disabled:opacity-50"
              style={{ background: '#162744' }}
            >
              {generating ? 'Generando expediente…' : 'Re-generar expediente consolidado'}
            </button>
          ) : null}

          {instanceId && (!estadoFinal || consolidado) ? (
            <button
              type="button"
              className={gradientBtnClass}
              style={gradientBtnStyle}
              disabled={busy}
              onClick={() => void handleDescargarTodo()}
              aria-label="Ver expediente consolidado (PDF)"
            >
              {downloading
                ? 'Generando expediente…'
                : 'Ver expediente consolidado (PDF)'}
            </button>
          ) : null}
        </div>
      </div>
    </ExpedienteDisclosure>
  );
}

function DocRow({
  instanceId,
  attachment: d,
}: {
  instanceId: string | null;
  attachment: ProcedureAttachment;
}) {
  const [busy, setBusy] = useState(false);
  const label = documentLabel(d.tipo) || d.filename || d.tipo || 'Documento';
  const filename = d.filename?.trim() || '';

  return (
    <li
      className="flex items-center gap-3 rounded-2xl border bg-white px-4 py-3 dark:bg-[#0B0F14]"
      style={{ borderColor: BORDER }}
    >
      <FileText className="h-5 w-5 shrink-0" style={{ color: BLUE }} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="truncate text-xs">
          <span className="font-semibold" style={{ color: '#162744' }}>
            {label}
          </span>
          {filename ? (
            <span className="font-normal opacity-55"> · {filename}</span>
          ) : null}
        </p>
        {d.sha256 ? (
          <p className="mt-0.5 truncate font-mono text-[10px] opacity-45" title={d.sha256}>
            SHA-256 {d.sha256}
          </p>
        ) : null}
      </div>
      <button
        type="button"
        disabled={!instanceId || busy}
        className="inline-flex shrink-0 items-center gap-1.5 rounded-full px-4 py-2 text-[11px] font-semibold text-white disabled:opacity-50"
        style={{ background: BLUE }}
        aria-label={`Ver ${filename || label}`}
        onClick={async () => {
          if (!instanceId) return;
          setBusy(true);
          try {
            await openAttachmentInNewTab(instanceId, d);
          } finally {
            setBusy(false);
          }
        }}
      >
        <Download className="h-3.5 w-3.5" aria-hidden="true" />
        {busy ? 'Abriendo…' : 'Ver'}
      </button>
    </li>
  );
}

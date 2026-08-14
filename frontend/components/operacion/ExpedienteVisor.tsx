'use client';

import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Download, Eye, FileText } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  openLoadingDocumentTab,
  openObjectUrlInWindow,
  showDocumentTabError,
} from '@/lib/documents/open-document-tab';
import { documentLabel } from '@/lib/tramites/document-labels';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { WizardCardHeader } from './wizard-atoms';
import { WIZARD_CARD } from './wizard-field-styles';
import type {
  InstanceStatus,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

// Expediente digital: documentos del trámite. Vehículo, actores y validación
// viven en MatriculaResumen. El organismo de tránsito no se muestra aquí
// (se elige en el paso 1 o lo fija el RUNT en traspaso).
//
// Rediseño (captura del paso de Resumen, Step5) — «Documentos cargados» y «Expediente
// consolidado» son DOS tarjetas siempre abiertas, no un único `WizardAccordion` titulado
// «Documentos» con el consolidado anidado dentro. La rejilla de documentos no cambia; solo su
// contenedor.

interface Props {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  modalidad?: WizardModalidad;
  status?: InstanceStatus;
  /** Nombre del organismo de tránsito ya elegido — casilla «Confirmo la radicación ante…». */
  organismoNombre?: string;
  /**
   * Trámites simultáneos declarados con el principal (propuesta, Step5: `subs`). Este módulo no
   * tiene su propio concepto de sub-trámite; reutiliza las transformaciones de vehículo declaradas
   * (`VehicleTransformationsCard`, ya rotuladas «Trámites Simultáneos» en el paso de Requisitos) en
   * vez de inventar una lista nueva.
   */
  tramitesSimultaneos?: string[];
  onBeforeGenerateConsolidado?: () => Promise<void>;
  onAttachmentsChange?: () => void;
  /**
   * Confirmaciones del expediente consolidado SIN marcar (punto 6, rediseño Step5): se dispara con
   * la lista de textos pendientes cada vez que cambia. Las casillas no gatean nada aquí —solo abren
   * el PDF, que no exige confirmación—; quien reciba esto es responsable de sumarlo a los requisitos
   * pendientes que bloquean el envío real del trámite.
   */
  onConfirmacionesPendientesChange?: (pendientes: string[]) => void;
}

const BLUE = '#557EFF';
const BORDER = '#DFE5ED';
// `#557EFF` como TEXTO/relleno sólido con blanco no llega a 4.5:1 (≈3.61:1, ver auditoría de
// diseño). `--badge-info-fg` es el mismo azul oscurecido para texto que ya usan los badges de
// tono `info` (≈6.46:1 sobre blanco, theme-aware) — token `--badge-*`, autorizado por norma.
const INK_BLUE = 'var(--badge-info-fg)';

/** Solo el consolidado del wizard (`tipo === 'consolidado'`). Nunca el maestro ni fuzzy match. */
export function findConsolidadoAttachment(
  attachments: ProcedureAttachment[],
): ProcedureAttachment | undefined {
  return attachments.find((a) => (a.tipo ?? '').toLowerCase() === 'consolidado');
}

/**
 * Tarjeta de sección siempre abierta (rediseño): mismo tratamiento que `ResumenCard` de
 * `MatriculaResumen` (radio, borde, franja azul junto al título), sin acordeón. «Documentos
 * cargados» y «Expediente consolidado» son ahora dos de estas, no un único desplegable.
 */
function VisorCard({
  title,
  subtitle,
  action,
  children,
}: {
  title: string;
  subtitle?: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section aria-label={title} className={WIZARD_CARD}>
      <div className="mb-3 flex items-center gap-2">
        <span className="h-4 w-1 shrink-0 rounded-full" style={{ background: BLUE }} aria-hidden="true" />
        <WizardCardHeader title={title} subtitle={subtitle} action={action} className="" />
      </div>
      {children}
    </section>
  );
}

/**
 * Abre el adjunto en pestaña nueva lo más rápido posible:
 * 1) abre la pestaña al instante (gesto del usuario) con el carrito rodando,
 * 2) pide URL prefirmada (sin proxy del binario por core-api),
 * 3) baja desde storage y re-empaqueta el Blob con el MIME real (PDF inline).
 * Fallback a /download si falla preview-url.
 */
export async function openAttachmentInNewTab(
  instanceId: string,
  attachment: Pick<ProcedureAttachment, 'id' | 'tipo' | 'filename' | 'mimetype'>,
) {
  const win = openLoadingDocumentTab();
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
    showDocumentTabError(win);
    throw err;
  }
}

export default function ExpedienteVisor({
  instanceId,
  attachments,
  modalidad = 'matricula_inicial',
  status = 'borrador',
  organismoNombre,
  tramitesSimultaneos = [],
  onBeforeGenerateConsolidado,
  onAttachmentsChange,
  onConfirmacionesPendientesChange,
}: Props) {
  return (
    <section aria-label="Expediente digital" className="space-y-3">
      <DocumentosCargadosCard instanceId={instanceId} attachments={attachments} />
      <ExpedienteConsolidadoCard
        instanceId={instanceId}
        attachments={attachments}
        modalidad={modalidad}
        status={status}
        organismoNombre={organismoNombre}
        tramitesSimultaneos={tramitesSimultaneos}
        onBeforeGenerateConsolidado={onBeforeGenerateConsolidado}
        onAttachmentsChange={onAttachmentsChange}
        onConfirmacionesPendientesChange={onConfirmacionesPendientesChange}
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

/**
 * «Documentos cargados» (rediseño, captura Step5): tarjeta siempre abierta con la rejilla de
 * documentos. Antes era el cuerpo del `WizardAccordion` «Documentos»; la rejilla no cambia.
 */
function DocumentosCargadosCard({
  instanceId,
  attachments,
}: {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
}) {
  return (
    <VisorCard
      title="Documentos cargados"
      action={
        // Distintivo del bloque (propuesta, WizardTramite.tsx:701). `aria-hidden`: cada documento ya
        // anuncia su propio "SHA-256 <hash>" como texto accesible; sin ocultarlo, el nombre accesible
        // de la tarjeta pasaría de "Documentos cargados" a "Documentos cargados SHA-256" para cada
        // lector de pantalla que la recorra, un dato redundante con lo que ya lee en cada ficha.
        <span className="text-xs font-semibold" style={{ color: BLUE }} aria-hidden="true">
          SHA-256
        </span>
      }
    >
      {attachments.length > 0 ? (
        // Rejilla (propuesta, «Documentos cargados»): sigue siendo una lista semántica, la rejilla es
        // solo el `className` — `<ul>`/`<li>` no cambian.
        <ul
          className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4"
          aria-label="Documentos del expediente (visor)"
        >
          {attachments.map((a) => (
            <DocRow key={a.id} instanceId={instanceId} attachment={a} />
          ))}
        </ul>
      ) : (
        // Piso de opacidad de la norma: 0.7 (estaba en 0.6).
        <p className="text-xs opacity-70">No se han cargado documentos.</p>
      )}
    </VisorCard>
  );
}

/**
 * «Expediente consolidado» (rediseño, captura Step5): tarjeta propia, separada de los documentos.
 * Añade las casillas de confirmación del punto 6 — nacen desmarcadas. NO gatean esta tarjeta: ver el
 * PDF no es un acto que haya que confirmar. Lo que sí deben condicionar es radicar, y esa decisión
 * vive fuera de este componente — se reportan hacia arriba con `onConfirmacionesPendientesChange`
 * para que el wizard las sume a sus «Requisitos pendientes antes del envío».
 */
function ExpedienteConsolidadoCard({
  instanceId,
  attachments,
  modalidad,
  status,
  organismoNombre,
  tramitesSimultaneos,
  onBeforeGenerateConsolidado,
  onAttachmentsChange,
  onConfirmacionesPendientesChange,
}: {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  modalidad: WizardModalidad;
  status: InstanceStatus;
  organismoNombre?: string;
  tramitesSimultaneos: string[];
  onBeforeGenerateConsolidado?: () => Promise<void>;
  onAttachmentsChange?: () => void;
  onConfirmacionesPendientesChange?: (pendientes: string[]) => void;
}) {
  const consolidado = findConsolidadoAttachment(attachments);
  const [generating, setGenerating] = useState(false);
  // Antes "downloading" (la acción descargaba el PDF); ahora abre el consolidado en pestaña nueva
  // (punto 2, rediseño) — el nombre de la bandera sigue la acción real.
  const [opening, setOpening] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const estadoFinal = status === 'aprobado' || status === 'anulado';
  const busy = generating || opening;

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
   * Genera (si hace falta) y ABRE el consolidado en pestaña nueva — punto 2 del rediseño: la
   * captura vigente (`MatriculaInicial`) dice literal «Ver expediente consolidado (PDF)», y es lo
   * que decía FLIT antes de que una generación anterior (`WizardTramite`) lo cambiara a descarga
   * directa. La descarga real al equipo del gestor la sigue cubriendo el botón «Descargar» de cada
   * documento suelto (`DocRow`), que no cambia.
   * Regenerar con force=true evita mostrar todos los documentos duplicados si se había generado
   * anidando un consolidado_maestro u otro paquete previo.
   */
  const handleVerExpediente = async () => {
    if (!instanceId) return;
    setOpening(true);
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
      setOpening(false);
    }
  };

  // Casillas de confirmación (punto 6, captura Step5). Nacen desmarcadas — una casilla de
  // consentimiento premarcada («Confirmo que el comprador autorizó…») no sería una autorización
  // real — pero NO gatean este botón: ver el PDF no es un acto que haya que confirmar. Lo que sí
  // deben condicionar es radicar, fuera de este componente (ver `onConfirmacionesPendientesChange`).
  // Memoizadas por contenido (no por referencia de `tramitesSimultaneos`, que el padre reconstruye
  // en cada render) para no disparar el efecto de abajo sin que el texto realmente haya cambiado.
  const tramitesSimultaneosKey = tramitesSimultaneos.join('|');
  const confirmaciones = useMemo(
    () => [
      'Confirmo que los datos del vehículo coinciden con la factura de venta.',
      'Confirmo que el comprador autorizó el tratamiento de sus datos personales.',
      `Confirmo la radicación ante ${organismoNombre?.trim() || 'el organismo seleccionado'}.`,
      ...(tramitesSimultaneosKey
        ? [`Confirmo los trámites simultáneos: ${tramitesSimultaneosKey.split('|').join(', ')}.`]
        : []),
    ],
    [organismoNombre, tramitesSimultaneosKey],
  );
  const [checked, setChecked] = useState<Record<string, boolean>>({});
  const pendientes = useMemo(
    () => (estadoFinal ? [] : confirmaciones.filter((c) => !checked[c])),
    [estadoFinal, confirmaciones, checked],
  );
  useEffect(() => {
    onConfirmacionesPendientesChange?.(pendientes);
  }, [pendientes, onConfirmacionesPendientesChange]);

  return (
    <VisorCard
      title="Expediente consolidado"
      subtitle={`Un solo PDF con el FUR, el certificado de identidad, la impronta y los documentos cargados en el trámite${modalidad === 'traspaso' ? ' (incluye el contrato de compraventa)' : ''}. Al generarlo se producen también los documentos que falten.`}
    >
      {error && (
        <div
          className="mb-3 rounded-xl border p-3 text-xs"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      {estadoFinal ? (
        <p className="mb-3 text-xs font-medium" style={{ color: '#557EFF' }} role="status">
          El trámite ya está {status === 'aprobado' ? 'aprobado' : 'anulado'}: su documentación es
          definitiva. Puedes consultarla y descargarla.
        </p>
      ) : (
        // `role="group"`: un `aria-label` sobre un div sin rol lo ignoran los lectores de pantalla,
        // así que las casillas quedaban sueltas sin decir de qué son.
        <div
          className="mb-4 space-y-2"
          role="group"
          aria-label="Confirmaciones del expediente consolidado"
        >
          {confirmaciones.map((c) => (
            <label key={c} className="flex items-start gap-2 text-xs" style={{ color: '#162744' }}>
              <input
                type="checkbox"
                checked={!!checked[c]}
                onChange={(e) => setChecked((prev) => ({ ...prev, [c]: e.target.checked }))}
                className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF]"
              />
              {c}
            </label>
          ))}
        </div>
      )}

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
          // Sólido azul, sin borde (captura Step5) — no el degradado que traía «Descargar todo».
          <button
            type="button"
            className="inline-flex items-center justify-center rounded-full px-6 py-2.5 text-xs font-semibold text-white transition hover:opacity-95 disabled:opacity-50"
            style={{ background: BLUE }}
            disabled={busy}
            onClick={() => void handleVerExpediente()}
            aria-label="Ver expediente consolidado (PDF)"
          >
            {opening ? 'Generando expediente…' : 'Ver expediente consolidado (PDF)'}
          </button>
        ) : null}
      </div>
    </VisorCard>
  );
}

function DocRow({
  instanceId,
  attachment: d,
}: {
  instanceId: string | null;
  attachment: ProcedureAttachment;
}) {
  const [busyVer, setBusyVer] = useState(false);
  const [busyDescargar, setBusyDescargar] = useState(false);
  const label = documentLabel(d.tipo) || d.filename || d.tipo || 'Documento';
  const filename = d.filename?.trim() || '';
  const nombreAccesible = filename || label;
  // Truncado a 24 caracteres con elipsis (propuesta): la rejilla es un vistazo, no el detalle
  // forense. El hash completo sigue disponible en el `title` (tooltip nativo).
  const shaShort = d.sha256 && d.sha256.length > 24 ? `${d.sha256.slice(0, 24)}…` : d.sha256;

  const handleVer = async () => {
    if (!instanceId) return;
    setBusyVer(true);
    try {
      await openAttachmentInNewTab(instanceId, d);
    } finally {
      setBusyVer(false);
    }
  };

  // Descarga real (propuesta, WizardTramite.tsx:710-712): a diferencia de "Ver" (que muestra el
  // documento en pestaña nueva), esta guarda el archivo en el equipo del gestor con
  // `downloadAttachment` — el mismo cliente que usa el consolidado.
  const handleDescargar = async () => {
    if (!instanceId) return;
    setBusyDescargar(true);
    try {
      const descargado = await tramitesClient.downloadAttachment(
        instanceId,
        d.id,
        undefined,
        filename || undefined,
      );
      const objectUrl = URL.createObjectURL(
        descargado.mimetype
          ? new Blob([descargado.blob], { type: descargado.mimetype })
          : descargado.blob,
      );
      const anchor = document.createElement('a');
      anchor.href = objectUrl;
      anchor.download = descargado.filename || nombreAccesible;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(objectUrl);
    } finally {
      setBusyDescargar(false);
    }
  };

  return (
    <li
      // Ficha dentro del acordeón blanco de "Documentos" (`WizardAccordion`): un `bg-white` aquí
      // repetía la tarjeta-dentro-de-tarjeta que el guardián de diseño ya había marcado en este
      // módulo. Se hunde en el azul del fondo de app (`#EEF5FF` claro / `#0A1428` oscuro, igual
      // que `background.app`) en vez de repetir el blanco del contenedor.
      className="flex flex-col gap-3 rounded-2xl border bg-[#EEF5FF] p-4 dark:bg-[#0A1428]"
      style={{ borderColor: BORDER }}
    >
      <div className="flex items-start justify-between gap-2">
        <FileText className="h-5 w-5 shrink-0" style={{ color: BLUE }} aria-hidden="true" />
        {/* Todo documento de esta lista ya se generó/cargó con éxito —si no, no estaría en el
            expediente—: el badge afirma ese hecho, no simula un estado variable que este dato no
            tiene (a diferencia del OCR pendiente/erróneo de la carga, que es otra pantalla). */}
        <StatusBadge label="Cargado" tone="success" />
      </div>
      <div className="min-w-0">
        <p className="truncate text-xs font-semibold" style={{ color: '#162744' }} title={label}>
          {label}
        </p>
        {filename ? <p className="truncate text-xs opacity-70">{filename}</p> : null}
        {d.sha256 ? (
          <p className="mt-1 truncate font-mono text-xs opacity-70" title={d.sha256}>
            SHA-256 {shaShort}
          </p>
        ) : null}
      </div>
      {/* Pista de la barra en `#DFE5ED` (no `#EEF5FF`): la tarjeta ya usa ese azul como fondo, y la
          pista se volvía invisible sobre su propio contenedor. */}
      <div className="h-1.5 w-full overflow-hidden rounded-full" style={{ background: '#DFE5ED' }} aria-hidden="true">
        <div className="h-full w-full rounded-full" style={{ background: '#8CC63F' }} />
      </div>
      {/* Dos acciones distintas, no una renombrada: "Ver" abre el documento en pestaña nueva para
          consultarlo sin salir del trámite; "Descargar" lo guarda en el equipo del gestor. Ambos
          botones nombran el documento en su `aria-label` — hay tests que buscan ese nombre exacto.
          Estilo "blanco con borde" (propuesta, MatriculaInicial.tsx:1065): el relleno sólido en
          `#557EFF` con texto blanco a 12px no llega a 4.5:1 (≈3.61:1); `--badge-info-fg` es el
          mismo azul oscurecido que ya usan los badges de tono `info` para resolver justo este caso. */}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={!instanceId || busyVer}
          className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-full border bg-white px-3 py-2 text-xs font-semibold disabled:opacity-50 dark:bg-transparent"
          style={{ borderColor: BLUE, color: INK_BLUE }}
          aria-label={`Ver ${nombreAccesible}`}
          onClick={() => void handleVer()}
        >
          <Eye className="h-3.5 w-3.5" aria-hidden="true" />
          {busyVer ? 'Abriendo…' : 'Ver'}
        </button>
        <button
          type="button"
          disabled={!instanceId || busyDescargar}
          className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-full border bg-white px-3 py-2 text-xs font-semibold disabled:opacity-50 dark:bg-transparent"
          style={{ borderColor: BLUE, color: INK_BLUE }}
          aria-label={`Descargar ${nombreAccesible}`}
          onClick={() => void handleDescargar()}
        >
          <Download className="h-3.5 w-3.5" aria-hidden="true" />
          {busyDescargar ? 'Descargando…' : 'Descargar'}
        </button>
      </div>
    </li>
  );
}

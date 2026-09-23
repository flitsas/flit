'use client';

import { useState, type ReactNode } from 'react';
import { Eye, Loader2 } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  openLoadingDocumentTab,
  openObjectUrlInWindow,
  showDocumentTabError,
} from '@/lib/documents/open-document-tab';
import { documentLabel, catalogDocumentTitle } from '@/lib/tramites/document-labels';
import { DocumentCatalogCaption } from '@/components/shared/DocumentCatalogCaption';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { AvisoFalloRegeneracion } from '@/components/shared/AvisoFalloRegeneracion';
import { AvisoDocumentoFinal } from '@/components/shared/AvisoDocumentoFinal';
import { findAttachmentByDocTipo } from '@/lib/documents/doc-tipo';
import {
  avisosSinFalloConsolidado,
  detectarFalloRegeneracion,
  vigenciaTrasApertura,
  type FalloRegeneracionConsolidado,
} from '@/lib/tramites/fallo-regeneracion-consolidado';
import {
  COPY_ACTUALIZADO,
  COPY_RECONSTRUCCION_EN_CURSO,
  COPY_TIMEOUT_CON_ANTERIOR,
  COPY_TIMEOUT_SIN_ANTERIOR,
  mensajeErrorConsolidado,
  useAperturaConsolidado,
  type FaseApertura,
} from '@/lib/tramites/useAperturaConsolidado';
import { WizardCardHeader } from './wizard-atoms';
import { WizardAccordion } from './WizardAccordion';
import { WIZARD_CARD, WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import type {
  ChecklistItemView,
  ConsolidadoVigencia,
  GenerarConsolidadoResult,
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
  /**
   * «Documentos cargados» (rediseño, captura Step5) — el checklist de documentos REQUERIDOS por la
   * tipología del trámite (`GET /instances/{id}/checklist`), no los adjuntos ya generados. Cada
   * ítem se empareja con su adjunto por `docTipo` ↔ `ProcedureAttachment.tipo` para la huella y el
   * botón «Ver PDF»; sin checklist (aún no cargó) la rejilla queda vacía, no cae a los adjuntos.
   */
  checklist?: ChecklistItemView[];
  modalidad?: WizardModalidad;
  status?: InstanceStatus;
  onBeforeGenerateConsolidado?: () => Promise<void>;
  onAttachmentsChange?: () => void;
  /**
   * HU #12792 — vigencia del consolidado del wizard (`ProcedureInstanceDetail.consolidadoWizard`).
   * `null`/`undefined` (backend anterior al campo) ⇒ el indicador no se pinta.
   * HU #12800 — la misma vigencia decide la apertura: `desactualizado`/`inexistente` → abrir muestra
   * «reconstrucción en curso» sin bloquear la UI.
   */
  consolidadoWizard?: ConsolidadoVigencia | null;
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
        <div className="flex min-w-0 flex-1 items-center justify-between gap-2">
          <h3 className="text-[15px] font-bold leading-snug" style={{ color: INK_BLUE }}>
            {title}
          </h3>
          {action}
        </div>
      </div>
      {subtitle && <p className="mb-3 text-xs opacity-70">{subtitle}</p>}
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
  /** HU #12800 — pestaña ya abierta con el clic (reconstrucción larga); si no, se abre aquí. */
  existingWin?: Window | null,
) {
  const win = existingWin ?? openLoadingDocumentTab();
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

const CONSOLIDADO_SUBTITLE =
  'Un solo PDF con el FUR, el certificado de identidad, la impronta y los documentos cargados en el trámite. Al generarlo se producen también los documentos que falten.';

export default function ExpedienteVisor({
  instanceId,
  attachments,
  checklist = [],
  modalidad = 'matricula_inicial',
  status = 'borrador',
  onBeforeGenerateConsolidado,
  onAttachmentsChange,
  consolidadoWizard,
}: Props) {
  const subtitle =
    modalidad === 'traspaso'
      ? `${CONSOLIDADO_SUBTITLE.slice(0, -1)} (incluye el contrato de compraventa).`
      : CONSOLIDADO_SUBTITLE;

  return (
    <section aria-label="Expediente digital" className="space-y-3">
      <DocumentosCargadosCard instanceId={instanceId} attachments={attachments} checklist={checklist} />
      <WizardAccordion
        title="Expediente consolidado"
        subtitle={subtitle}
        defaultOpen
        level="h3"
        regionLabel="Expediente consolidado del trámite"
      >
        {/* HU #12792 — el indicador de vigencia abre el cuerpo del visor (lo pinta el cuerpo: el aviso
            de fallo de regeneración de la HU #12799 sale de su estado). */}
        <ExpedienteConsolidadoBody
          instanceId={instanceId}
          attachments={attachments}
          modalidad={modalidad}
          status={status}
          consolidadoWizard={consolidadoWizard}
          onBeforeGenerateConsolidado={onBeforeGenerateConsolidado}
          onAttachmentsChange={onAttachmentsChange}
        />
      </WizardAccordion>
    </section>
  );
}

function causaAviso(aviso: string): string {
  const motivo = aviso.split(':').slice(1).join(':').trim();
  return motivo.includes('organismo_requerido')
    ? ' (falta el organismo de tránsito)'
    : motivo.includes('provider_unavailable')
      ? ' (el proveedor no está disponible; vuelve a generar el expediente en unos minutos)'
      : motivo.includes('provider_validation')
        ? ' (el proveedor rechazó los datos del trámite)'
        : motivo
          ? ` (${motivo})`
          : '';
}

function consolidadoAvisoLabel(aviso: string): string {
  const documento = aviso.split(':')[0]?.trim() ?? '';
  const nombre =
    documento === 'documentos_del_expediente'
      ? 'algunos documentos del expediente'
      : documentLabel(documento);
  return `${nombre}${causaAviso(aviso)}`;
}

/**
 * «Documentos cargados» (rediseño, captura Step5): tarjeta siempre abierta con la rejilla de
 * documentos. Antes era el cuerpo del `WizardAccordion` «Documentos»; la rejilla no cambia.
 *
 * La fuente es el CHECKLIST (`GET /instances/{id}/checklist`, `ChecklistItemView[]`) — el listado de
 * documentos REQUERIDOS por la tipología del trámite con su estado (`satisfied`) — no los
 * `ProcedureAttachment[]` ya generados: esos, por definición, siempre están "Cargado" y no dejan ver
 * lo que falta. Cada ítem se empareja con su adjunto por `docTipo` ↔ `tipo` para la huella y el botón
 * «Ver PDF»; sin adjunto emparejado no hay botón, y el documento queda "Pendiente" si es obligatorio
 * o "No cargado" si es opcional (ver {@link DocRow}).
 */
function DocumentosCargadosCard({
  instanceId,
  attachments,
  checklist,
}: {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  checklist: ChecklistItemView[];
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
      {checklist.length > 0 ? (
        // Rejilla (propuesta, «Documentos cargados»): sigue siendo una lista semántica, la rejilla es
        // solo el `className` — `<ul>`/`<li>` no cambian.
        <ul
          className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4 2xl:grid-cols-6"
          aria-label="Documentos del expediente (visor)"
        >
          {checklist.map((item) => (
            <DocRow
              key={item.key}
              instanceId={instanceId}
              item={item}
              attachment={findAttachmentByDocTipo(attachments, item.docTipo)}
            />
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
 * «Expediente consolidado»: tarjeta propia, separada de los documentos. Genera y abre el PDF;
 * no pide confirmaciones al gestor ni bloquea la radicación.
 */
function ExpedienteConsolidadoBody({
  instanceId,
  attachments,
  modalidad,
  status,
  consolidadoWizard,
  onBeforeGenerateConsolidado,
  onAttachmentsChange,
}: {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  modalidad: WizardModalidad;
  status: InstanceStatus;
  consolidadoWizard?: ConsolidadoVigencia | null;
  onBeforeGenerateConsolidado?: () => Promise<void>;
  onAttachmentsChange?: () => void;
}) {
  const consolidado = findConsolidadoAttachment(attachments);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const estadoFinal = status === 'aprobado' || status === 'anulado';
  /**
   * HU #12799 — fallo de la última regeneración (respuesta con `regenerado: false` + aviso
   * `consolidado: …`). El backend no guarda un «último fallo» consultable: se conoce por la respuesta
   * de la apertura o de «Re-generar» y se limpia con la siguiente respuesta sin fallo.
   */
  const [fallo, setFallo] = useState<FalloRegeneracionConsolidado | null>(null);
  /**
   * HU #12799 (AC1/AC2) — vigencia refrescada en local tras abrir/regenerar: con fallo queda gris
   * con la fecha del PDF conservado; con éxito, verde. Atada a la prop de la que partió (`base`): en
   * cuanto el padre recarga la vigencia del backend, esa gana.
   */
  const [vigenciaLocal, setVigenciaLocal] = useState<{
    base: ConsolidadoVigencia | null | undefined;
    valor: ConsolidadoVigencia;
  } | null>(null);
  // Cambio de trámite: el fallo y la vigencia local del anterior no le pertenecen a este.
  const [instanciaVista, setInstanciaVista] = useState(instanceId);
  if (instanciaVista !== instanceId) {
    setInstanciaVista(instanceId);
    setFallo(null);
    setVigenciaLocal(null);
  }
  const vigencia =
    vigenciaLocal && vigenciaLocal.base === consolidadoWizard
      ? vigenciaLocal.valor
      : consolidadoWizard;

  /** HU #12799 — registra el resultado de una generación/apertura: fallo + vigencia local. */
  const registrarResultado = (generado: GenerarConsolidadoResult | null | undefined) => {
    if (!generado) return;
    setFallo(detectarFalloRegeneracion(generado));
    const nueva = vigenciaTrasApertura(vigencia, generado, new Date());
    if (nueva) setVigenciaLocal({ base: consolidadoWizard, valor: nueva });
  };

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
    // HU #12799 — el fallo del propio consolidado lo cuenta el aviso junto al indicador: aquí solo
    // quedan los avisos de los OTROS documentos de la cascada (sin duplicar mensajes).
    for (const aviso of avisosSinFalloConsolidado(generado?.avisosCascada)) {
      // HU #11642 — el aviso de FUR es distinto del resto de la cascada: el documento SÍ existe, lo
      // que falló fue rehacerlo, así que el consolidado que el gestor tiene delante conserva la
      // versión anterior. Decirle "no se pudo generar el FUR" le haría creer que falta, cuando el
      // problema real es que lo que ve no recoge su último cambio.
      if (aviso.startsWith('fur:')) {
        avisos.push(
          `No se pudo regenerar el FUR${causaAviso(aviso)}: el expediente conserva la versión anterior y puede no reflejar tus últimos cambios.`,
        );
        continue;
      }
      avisos.push(`No se pudo generar ${consolidadoAvisoLabel(aviso)}.`);
    }
    if (avisos.length > 0) {
      // HU #12799 — con `regenerado: false` NO se generó nada nuevo: no se dice «generado».
      setError(
        generado?.regenerado === false
          ? avisos.join(' ')
          : `Expediente consolidado generado. ${avisos.join(' ')}`,
      );
    }
  };

  /**
   * Acción EXPLÍCITA «Re-generar expediente consolidado»: única que envía force=true (HU #12788 AC3).
   * Invalida la vigencia y reconstruye consolidado y FUR (HU #11642) sin anidar un consolidado previo.
   */
  const handleGenerate = async () => {
    // HU #12788 — candado síncrono: `disabled={busy}` solo llega tras el re-render, así que un doble
    // clic rápido lanzaría dos POST /consolidado (y, con force, dos reconstrucciones). El candado (del
    // hook, compartido con la apertura) corta el segundo antes de que salga la petición.
    if (!instanceId || !apertura.tomarCandado()) return;
    setGenerating(true);
    setError(null);
    apertura.limpiarError();
    try {
      await onBeforeGenerateConsolidado?.();
      // force=true: invalida caché y reconstruye sin anidar un consolidado previo (evita docs duplicados).
      const generado = await tramitesClient.generarConsolidado(instanceId, undefined, true);
      registrarResultado(generado);
      applyAvisos(generado);
      onAttachmentsChange?.();
    } catch (err) {
      setError(mensajeErrorConsolidado(err));
    } finally {
      apertura.liberarCandado();
      setGenerating(false);
    }
  };

  /**
   * Genera (si hace falta) y ABRE el consolidado en pestaña nueva — punto 2 del rediseño: la
   * captura vigente (`MatriculaInicial`) dice literal «Ver expediente consolidado (PDF)».
   *
   * HU #12788 — abrir va SIN force: el backend respeta la bandera `consolidado_wizard_vigente`.
   * Code-review M2 (Épica #12760) — y por la ruta única de ENTREGA (`GET …/consolidado/entrega`,
   * #12785), no por el POST de generación: en un trámite aprobado sirve el definitivo, sin 409.
   * HU #12800 — la espera sale del componente (`useAperturaConsolidado`): si el PDF está
   * desactualizado se muestra «reconstrucción en curso» al instante, el resto del expediente sigue
   * operativo y, si la reconstrucción tarda más del máximo, se abre el PDF anterior con aviso.
   */
  const apertura = useAperturaConsolidado({
    instanceId,
    vigencia,
    consolidadoPrevio: consolidado ?? null,
    abrirAdjunto: openAttachmentInNewTab,
    onBeforeGenerate: onBeforeGenerateConsolidado,
    onEntregado: (generado) => {
      registrarResultado(generado);
      if (generado) applyAvisos(generado);
      onAttachmentsChange?.();
    },
  });
  const opening = apertura.enVuelo;
  const busy = generating || opening;
  const errorVisible = error ?? apertura.error;
  /**
   * AC3 #12785 — la entrega avisa que sirvió el DEFINITIVO aunque el `status` que recibe el visor
   * aún no sea final (detalle sin recargar): se trata igual que el estado final (sin «Re-generar»,
   * que respondería 409, y sin aviso de fallo).
   */
  const definitivoPorEntrega = !estadoFinal && apertura.definitivo;
  const sinRegeneracion = estadoFinal || definitivoPorEntrega;

  return (
    <>
      {/* HU #12799 — aviso de fallo con la fecha del PDF conservado y «Reintentar» = «Re-generar».
          El rótulo de vigencia (HU #12792) se retiró por decisión de producto. */}
      {fallo && !sinRegeneracion ? (
        <div className="mb-3">
          <AvisoFalloRegeneracion
            fallo={fallo}
            generadoEn={vigencia?.generadoEn ?? null}
            onReintentar={instanceId ? () => void handleGenerate() : undefined}
            reintentando={busy}
          />
        </div>
      ) : null}

      {errorVisible && (
        <div
          className="mb-3 rounded-xl border p-3 text-xs"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {errorVisible}
        </div>
      )}

      <ReconstruccionPanel fase={apertura.fase} sirvioAnterior={apertura.sirvioAnterior} />

      {estadoFinal ? (
        <p className="mb-3 text-xs font-medium" style={{ color: '#557EFF' }} role="status">
          El trámite ya está {status === 'aprobado' ? 'aprobado' : 'anulado'}: su documentación es
          definitiva. Puedes consultarla y descargarla.
        </p>
      ) : null}
      {definitivoPorEntrega ? <AvisoDocumentoFinal className="mb-3" /> : null}

      <div className="flex flex-wrap items-center gap-2">
        {!sinRegeneracion && consolidado ? (
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
          // CTA primario en el degradado de marca (`gradient.primary`), igual que el resto de
          // acciones principales del módulo. Antes iba en azul plano copiando la captura del Step5;
          // el guardián de diseño rechaza el CTA primario plano, así que se unifica con el degradado.
          <button
            type="button"
            className="inline-flex items-center justify-center rounded-full px-6 py-2.5 text-xs font-semibold text-white transition hover:opacity-95 disabled:opacity-50"
            style={{ background: WIZARD_CTA_GRADIENT }}
            disabled={busy}
            onClick={() => {
              setError(null);
              void apertura.abrir();
            }}
            aria-label="Ver expediente consolidado (PDF)"
          >
            {opening ? 'Generando expediente…' : 'Ver expediente consolidado (PDF)'}
          </button>
        ) : null}
      </div>
    </>
  );
}

/**
 * HU #12800 — estado de la reconstrucción del consolidado. Región `role="status"` + `aria-live`
 * (no modal, no overlay): el lector de pantalla la anuncia y el gestor sigue operando el expediente.
 * Azul = proceso; ámbar = advertencia (timeout); el texto dice el estado, no solo el color.
 */
function ReconstruccionPanel({
  fase,
  sirvioAnterior,
}: {
  fase: FaseApertura;
  sirvioAnterior: boolean;
}) {
  if (fase === 'reconstruyendo') {
    return (
      <div
        role="status"
        aria-live="polite"
        data-testid="consolidado-reconstruccion"
        className="mb-3 flex items-start gap-2 rounded-xl border p-3 text-xs"
        style={{ borderColor: BLUE, background: 'rgba(85,126,255,0.06)', color: INK_BLUE }}
      >
        <Loader2 className="mt-0.5 h-4 w-4 shrink-0 animate-spin" aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <p className="font-semibold">Reconstrucción en curso</p>
          <p className="mt-0.5">{COPY_RECONSTRUCCION_EN_CURSO}</p>
          {/* Progreso indeterminado: el backend no expone avance; `progressbar` sin valor = en curso. */}
          <div
            role="progressbar"
            aria-label="Progreso de la reconstrucción del expediente"
            className="mt-2 h-1.5 w-full overflow-hidden rounded-full"
            style={{ background: BORDER }}
          >
            <div className="h-full w-1/3 animate-pulse rounded-full" style={{ background: BLUE }} />
          </div>
        </div>
      </div>
    );
  }
  if (fase === 'timeout') {
    return (
      <div
        role="status"
        aria-live="polite"
        data-testid="consolidado-timeout"
        className="mb-3 flex items-start gap-2 rounded-xl border p-3 text-xs"
        style={{
          borderColor: 'rgba(249,172,0,0.55)',
          background: 'rgba(249,172,0,0.12)',
          color: 'var(--badge-warning-fg)',
        }}
      >
        <Loader2 className="mt-0.5 h-4 w-4 shrink-0 animate-spin" aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <p className="font-semibold">La actualización sigue en proceso</p>
          <p className="mt-0.5">
            {sirvioAnterior ? COPY_TIMEOUT_CON_ANTERIOR : COPY_TIMEOUT_SIN_ANTERIOR}
          </p>
        </div>
      </div>
    );
  }
  if (fase === 'actualizado') {
    return (
      <p
        role="status"
        aria-live="polite"
        data-testid="consolidado-actualizado"
        className="mb-3 text-xs font-medium"
        style={{ color: INK_BLUE }}
      >
        {COPY_ACTUALIZADO}
      </p>
    );
  }
  return null;
}

function DocRow({
  instanceId,
  item,
  attachment,
}: {
  instanceId: string | null;
  item: ChecklistItemView;
  /** Adjunto emparejado por `docTipo` ↔ `tipo`; ausente cuando el documento aún no se cargó. */
  attachment: ProcedureAttachment | undefined;
}) {
  const [busy, setBusy] = useState(false);
  // Rótulo del CHECKLIST (`item.label`, ya resuelto por backend) — no `documentLabel(tipo)`: es el
  // dato correcto para el requisito, y cubre también los que no tienen adjunto que traer un `tipo`.
  const label = catalogDocumentTitle(item.docTipo ?? item.key, item.label);
  const validado = item.satisfied;
  /**
   * Un documento OPCIONAL que falta no es lo mismo que uno obligatorio que falta, y hasta ahora se
   * pintaban idénticos: «Pendiente» en ámbar, con la barra en ámbar. Este paso es el último antes de
   * radicar, así que ese ámbar se lee como deuda —«todavía tengo que anexarlo»— y el gestor no tenía
   * cómo saber cuál de los dos era.
   *
   * `item.obligatorio` viene en el checklist desde siempre; simplemente no se estaba mirando. Con él,
   * el COLOR hace el trabajo: ámbar es «tienes que», gris es «para tu información». Y «Pendiente»
   * recupera su significado, que estaba diluido por usarse para todo.
   *
   * No se ocultan los opcionales que faltan: esta tarjeta es el inventario del expediente. Ocultarlos
   * los haría aparecer y desaparecer según su estado —un opcional YA cargado tiene que seguir
   * visible, porque va en el consolidado— y le quitaría al gestor el único sitio donde ve que falta.
   */
  const faltaObligatorio = !validado && item.obligatorio;
  // Truncado a 24 caracteres con elipsis (propuesta): la rejilla es un vistazo, no el detalle
  // forense. El hash completo sigue disponible en el `title` (tooltip nativo). Solo hay SHA cuando
  // hay adjunto emparejado.
  const sha = attachment?.sha256;
  const shaShort = sha && sha.length > 24 ? `${sha.slice(0, 24)}…` : sha;

  const handleVer = async () => {
    if (!instanceId || !attachment) return;
    setBusy(true);
    try {
      await openAttachmentInNewTab(instanceId, attachment);
    } finally {
      setBusy(false);
    }
  };

  return (
    <li
      // Tarjeta blanca DENTRO de la tarjeta blanca de la sección (propuesta, Step5): se distingue
      // por el borde + la sombra, no por un fondo hundido — el `#EEF5FF`/`#0A1428` que traía antes
      // es el hundido en el fondo de app que el guardián de diseño ya había marcado y que la
      // captura no tiene. `dark:bg-[#162744]` es la misma superficie oscura de tarjeta anidada que
      // usa `WIZARD_CARD` en toda la app, no el fondo de app.
      className="flex flex-col gap-3 rounded-xl border bg-white p-4 shadow-sm dark:bg-[#162744]"
      style={{ borderColor: BORDER }}
    >
      <div className="flex items-start justify-between gap-2">
        {/* Nombre en navy, puede ocupar dos líneas (propuesta): sin icono de fichero, la captura no
            lo tiene. */}
        <p className="min-w-0 flex-1 text-xs font-semibold leading-tight" style={{ color: '#162744' }} title={label}>
          <DocumentCatalogCaption nombre={item.label} codigo={item.docTipo ?? item.key} />
        </p>
        <StatusBadge
          label={validado ? 'Validado' : faltaObligatorio ? 'Pendiente' : 'No cargado'}
          tone={validado ? 'success' : faltaObligatorio ? 'warning' : 'neutral'}
          className="shrink-0"
        />
      </div>
      {sha ? (
        <p className="truncate font-mono text-xs opacity-70" title={sha}>
          SHA-256 {shaShort}
        </p>
      ) : null}
      {/* La barra tiene que decir lo MISMO que el badge. Cambiar solo la palabra y dejarla en ámbar
          seguiría comunicando urgencia: el color pesa más que el rótulo. */}
      <div className="h-1.5 w-full overflow-hidden rounded-full" style={{ background: '#DFE5ED' }} aria-hidden="true">
        <div
          className="h-full w-full rounded-full"
          style={{
            background: validado
              ? '#8CC63F'
              : faltaObligatorio
                ? 'var(--badge-warning-fg)'
                : '#94A3B8',
          }}
        />
      </div>
      {/* Un solo botón (propuesta, Step5): «Ver PDF», no "Ver"/"Descargar". Sin adjunto emparejado
          no se pinta — no hay nada que abrir. */}
      {attachment ? (
        <button
          type="button"
          disabled={!instanceId || busy}
          className="inline-flex items-center justify-center gap-1.5 rounded-full border bg-white px-4 py-2 text-xs font-semibold disabled:opacity-50 dark:bg-transparent"
          style={{ borderColor: BLUE, color: INK_BLUE }}
          aria-label={`Ver PDF de ${label}`}
          onClick={() => void handleVer()}
        >
          <Eye className="h-3.5 w-3.5" aria-hidden="true" />
          {busy ? 'Abriendo…' : 'Ver PDF'}
        </button>
      ) : null}
    </li>
  );
}

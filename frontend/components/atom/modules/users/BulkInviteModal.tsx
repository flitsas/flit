"use client";

import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { download } from "@/components/consultas/export";
import { useWizardFocusTrap } from "@/components/operacion/use-wizard-focus-trap";
import { controlCls } from "@/components/operacion/tramites-control-styles";
import {
  BULK_INVITE_MAX_FILE_BYTES,
  BULK_INVITE_MAX_ROWS,
  type BulkInvitationReport,
  type BulkInvitationsClient,
} from "@/lib/api/bulk-invitations-client";
import {
  BULK_INVITE_UNCERTAIN_MESSAGE,
  buildBulkInviteErrorsCsv,
  bulkInviteErrorRows,
  bulkInviteRequestErrorMessage,
  bulkInviteRowReason,
  bulkInviteTemplateErrorMessage,
  isBulkInviteOutcomeUncertain,
  validateBulkInviteFile,
} from "@/lib/users/bulkInviteErrors";
import { MODAL_CABECERA_CLS, MODAL_CERRAR_CLS, MODAL_DESCRIPCION_CLS, MODAL_PANEL_CLS, MODAL_TITULO_CLS, MODAL_VELO_CLS } from "@flit/ui/Modal";

const PRIMARY_FILL = "var(--kit-azul-relleno)";
const MAX_MB = BULK_INVITE_MAX_FILE_BYTES / (1024 * 1024);

/**
 * idle        → vacío: sin archivo, o con archivo listo para validar
 * validating  → cargando (dryRun)
 * preview     → lleno: resumen del dryRun, a la espera de confirmar
 * committing  → cargando (commit)
 * result      → lleno: conteos finales y filas con error
 * uncertain   → error de red/timeout en el commit: no se sabe qué se creó
 */
type Phase = "idle" | "validating" | "preview" | "committing" | "result" | "uncertain";

export interface BulkInviteModalProps {
  /**
   * Cliente del contexto de invitación (Security u OT, ya acotado a su destino). `null` mientras el
   * contexto está incompleto —p. ej. el SuperAdmin aún no eligió la empresa—: no se habilita
   * descargar la plantilla ni cargar el archivo (HU #14427, AC2).
   */
  client: BulkInvitationsClient | null;
  /**
   * Identifica el contexto (perfil + destino). Si cambia, se descarta el archivo cargado y el
   * resumen: el catálogo de roles y las validaciones eran las del contexto anterior (HU #14427, AC3).
   */
  contextKey: string;
  /** Texto del contexto cuando viene fijo (compañía propia, organismo del hub). */
  contextSummary?: string;
  /** Selector de contexto del llamador (Perfil + destino). Se deshabilita mientras hay una petición en curso. */
  contextSlot?: (ctx: { disabled: boolean }) => ReactNode;
  onClose: () => void;
  /**
   * Se invoca cuando el listado de usuarios quedó desactualizado: tras un commit con respuesta (con
   * o sin filas creadas) o sin ella (red/timeout, donde algo pudo crearse). `report` es `null` si no hubo respuesta.
   */
  onCompleted?: (report: BulkInvitationReport | null) => void;
}

/**
 * Asistente de invitación masiva por Excel (Feature #14420 · ADR-0077): descargar plantilla →
 * subir .xlsx → resumen del dryRun → confirmar → resultado + CSV de errores.
 *
 * Compartido por Usuarios (Gestor / OT) y el hub OT: quien lo monta inyecta el `client` ya acotado.
 * No hay librería de Excel en el frontend: el archivo viaja tal cual y el servidor lo lee.
 *
 * Accesibilidad: diálogo modal con foco contenido y devuelto al cerrar (Escape cierra salvo en
 * curso), controles con etiqueta y una región `status` que anuncia cada cambio de fase.
 */
export function BulkInviteModal({
  client,
  contextKey,
  contextSummary,
  contextSlot,
  onClose,
  onCompleted,
}: BulkInviteModalProps) {
  const titleId = useId();
  const descId = useId();
  const fileInputId = useId();
  const fileHelpId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const abortRef = useRef<AbortController | null>(null);
  const inFlightRef = useRef(false);

  const [phase, setPhase] = useState<Phase>("idle");
  const [file, setFile] = useState<File | null>(null);
  const [inputKey, setInputKey] = useState(0);
  const [report, setReport] = useState<BulkInvitationReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [templateBusy, setTemplateBusy] = useState(false);
  const [templateError, setTemplateError] = useState<string | null>(null);

  const busy = phase === "validating" || phase === "committing";
  const ready = client !== null;

  function reset() {
    setPhase("idle");
    setFile(null);
    setReport(null);
    setError(null);
    setTemplateError(null);
    setInputKey((k) => k + 1);
  }

  // Cambió el perfil o el destino: el archivo y su resumen ya no corresponden (ajuste de estado
  // durante el render, patrón soportado por React — no hace falta un efecto).
  const [lastContextKey, setLastContextKey] = useState(contextKey);
  if (lastContextKey !== contextKey) {
    setLastContextKey(contextKey);
    reset();
  }

  useEffect(() => () => abortRef.current?.abort(), []);

  const requestClose = () => {
    if (!busy) onClose();
  };
  useWizardFocusTrap(panelRef, { active: true, onEscape: requestClose });

  // Al empezar una petición el botón pulsado queda deshabilitado y el foco se iría al <body>, fuera
  // de la trampa: se mantiene dentro del diálogo.
  useEffect(() => {
    if (busy) panelRef.current?.focus();
  }, [busy]);

  async function handleDownloadTemplate() {
    if (!client || templateBusy) return;
    setTemplateError(null);
    setTemplateBusy(true);
    try {
      await client.downloadTemplate();
    } catch (err) {
      setTemplateError(bulkInviteTemplateErrorMessage(err));
    } finally {
      setTemplateBusy(false);
    }
  }

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const picked = e.target.files?.[0] ?? null;
    setReport(null);
    setError(null);
    setPhase("idle");
    if (!picked) {
      setFile(null);
      return;
    }
    const problem = validateBulkInviteFile(picked);
    if (problem) {
      // Antes de enviar nada al servidor (AC2): se descarta y se explica.
      setFile(null);
      setError(problem);
      setInputKey((k) => k + 1);
      return;
    }
    setFile(picked);
  }

  async function run(dryRun: boolean) {
    if (!client || !file || inFlightRef.current) return;
    inFlightRef.current = true;
    const controller = new AbortController();
    abortRef.current = controller;
    setError(null);
    setPhase(dryRun ? "validating" : "committing");
    try {
      const result = await client.submit(file, { dryRun, signal: controller.signal });
      setReport(result);
      setPhase(dryRun ? "preview" : "result");
      if (!dryRun) onCompleted?.(result);
    } catch (err) {
      if (controller.signal.aborted) return;
      if (!dryRun && isBulkInviteOutcomeUncertain(err)) {
        setReport(null);
        setPhase("uncertain");
        setError(BULK_INVITE_UNCERTAIN_MESSAGE);
        onCompleted?.(null);
      } else {
        // El archivo sigue seleccionado: se puede reintentar o elegir otro.
        setPhase(dryRun ? "idle" : "preview");
        setError(bulkInviteRequestErrorMessage(err));
      }
    } finally {
      inFlightRef.current = false;
    }
  }

  function handleDownloadErrors() {
    if (!report) return;
    const stamp = new Date().toISOString().slice(0, 10);
    download(buildBulkInviteErrorsCsv(report.rows), `errores-invitacion-masiva-${stamp}.csv`, "text/csv;charset=utf-8");
  }

  const announcement = announcementFor(phase, report);
  const errorRows = report ? bulkInviteErrorRows(report.rows) : [];

  if (typeof document === "undefined") return null;

  return createPortal(
    <div
      className={`fixed inset-0 z-[100] flex items-center justify-center overflow-hidden px-4 py-6 ${MODAL_VELO_CLS}`}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) requestClose();
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descId}
        aria-busy={busy}
        tabIndex={-1}
        data-testid="bulk-invite-modal"
        className={`flex max-h-[calc(100dvh-3rem)] w-full min-w-0 max-w-2xl flex-col p-4 sm:p-6 ${MODAL_PANEL_CLS}`}
      >
        <div className={`${MODAL_CABECERA_CLS} -mx-4 -mt-4 mb-4 sm:-mx-6 sm:-mt-6`}>
          <div className="flex min-w-0 flex-col items-center gap-2">
            <span className="contents" aria-hidden="true">
              <Icono nombre="reporte-detallado" disco size={52} />
            </span>
            <div className="min-w-0">
              <h2 id={titleId} className={MODAL_TITULO_CLS}>
                Importar usuarios desde Excel
              </h2>
              <p id={descId} className={MODAL_DESCRIPCION_CLS}>
                Invita hasta {BULK_INVITE_MAX_ROWS} usuarios por archivo. Cada fila lleva nombre, correo y un rol.
              </p>
            </div>
          </div>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={requestClose}
            disabled={busy}
            className={`${MODAL_CERRAR_CLS} disabled:cursor-not-allowed disabled:opacity-40`}
          >
            <Icono nombre="cerrar" className="h-5 w-5" />
          </button>
        </div>

        {/* Anuncia cada cambio de fase al lector de pantalla sin duplicar las alertas visibles. */}
        <p role="status" aria-live="polite" className="sr-only" data-testid="bulk-invite-live">
          {announcement}
        </p>

        <div className="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto overflow-x-hidden pr-1">
          {(contextSlot || contextSummary) && (
            <section aria-label="Contexto de la invitación" className="flex flex-col gap-2">
              {contextSlot?.({ disabled: busy })}
              {contextSummary && (
                <p
                  className="rounded-xl border border-[color:var(--di-border)] px-3 py-2 text-xs"
                  data-testid="bulk-invite-context"
                >
                  <span className="text-[10px] font-semibold uppercase opacity-60">Destino</span>
                  <span className="mt-0.5 block font-semibold">{contextSummary}</span>
                </p>
              )}
            </section>
          )}

          {/* Paso 1 — plantilla */}
          <section className="flex flex-col gap-2" aria-labelledby={`${titleId}-step1`}>
            <h3 id={`${titleId}-step1`} className="text-sm font-semibold">
              1. Descarga la plantilla
            </h3>
            <p className="text-[11px] leading-snug opacity-70">
              Trae una hoja con el catálogo de roles que puedes asignar en este destino. Un rol por fila; no cambies
              ni muevas las columnas de la primera fila.
            </p>
            <button
              type="button"
              onClick={() => void handleDownloadTemplate()}
              disabled={!ready || templateBusy || busy}
              className={`${controlCls(false)} self-start`}
              data-testid="bulk-invite-template"
            >
              <Icono nombre="nube-bajar" className={`h-3.5 w-3.5 ${templateBusy ? "animate-pulse" : ""}`} />
              {templateBusy ? "Descargando…" : "Descargar plantilla"}
            </button>
            {!ready && (
              <p className="text-[11px] opacity-70" data-testid="bulk-invite-needs-context">
                Elige primero el destino para habilitar la plantilla y la carga del archivo.
              </p>
            )}
            {templateError && (
              <InlineAlert tone="error" title="No se pudo descargar la plantilla">
                {templateError}
              </InlineAlert>
            )}
          </section>

          {/* Paso 2 — archivo */}
          <section className="flex flex-col gap-2 border-t border-[color:var(--di-border)] pt-5">
            <h3 className="text-sm font-semibold">2. Sube el archivo diligenciado</h3>
            <label htmlFor={fileInputId} className="text-xs font-semibold">
              Archivo Excel (.xlsx)
            </label>
            <input
              key={inputKey}
              id={fileInputId}
              type="file"
              accept=".xlsx"
              onChange={handleFileChange}
              disabled={!ready || busy}
              aria-describedby={error && phase !== "uncertain" ? `${fileHelpId} bulk-invite-error` : fileHelpId}
              aria-invalid={error && phase !== "uncertain" ? true : undefined}
              data-testid="bulk-invite-file"
              className="block w-full text-xs file:mr-3 file:rounded-xl file:border file:border-[color:var(--di-border)] file:bg-card file:px-3 file:py-2 file:text-xs file:font-semibold file:text-flit-primary hover:file:bg-[var(--kit-hover)] disabled:opacity-50 dark:file:text-foreground"
            />
            <p id={fileHelpId} className="text-[11px] opacity-70">
              Máximo {BULK_INVITE_MAX_ROWS} filas y {MAX_MB} MB.
              {file ? (
                <>
                  {" "}
                  Seleccionado: <strong className="break-all">{file.name}</strong>
                </>
              ) : null}
            </p>

            {error && phase !== "uncertain" && (
              <InlineAlert tone="warning" title="No se pudo procesar el archivo" id="bulk-invite-error">
                {error}
              </InlineAlert>
            )}
          </section>

          {/* Estado: cargando */}
          {busy && (
            <div
              className="flex items-center gap-2 rounded-xl border border-[color:var(--di-border)] p-3 text-xs"
              data-testid="bulk-invite-loading"
            >
              <Icono nombre="girador" className="h-4 w-4 shrink-0 animate-spin" style={{ color: "var(--color-flit-brand)" }} />
              <span>
                {phase === "validating"
                  ? "Validando el archivo…"
                  : "Creando las invitaciones y enviando los correos de activación. No cierres esta ventana."}
              </span>
            </div>
          )}

          {/* Estado: error de red o timeout en el commit */}
          {phase === "uncertain" && error && (
            <InlineAlert tone="error" title="No sabemos cuántas invitaciones se crearon" id="bulk-invite-uncertain">
              <span data-testid="bulk-invite-uncertain">{error}</span>
            </InlineAlert>
          )}

          {/* Estado: lleno — resumen del dryRun */}
          {phase === "preview" && report && (
            <section className="flex flex-col gap-3 border-t border-[color:var(--di-border)] pt-5" data-testid="bulk-invite-preview">
              <h3 className="text-sm font-semibold">3. Revisa el resumen y confirma</h3>
              <Counts
                items={[
                  { label: "Filas leídas", value: report.total },
                  { label: "Listas para invitar", value: report.valid, tone: "success" },
                  { label: "Con error", value: report.rejected + report.notProcessed, tone: report.rejected + report.notProcessed > 0 ? "danger" : undefined },
                ]}
              />
              {report.valid > 0 ? (
                <p className="text-[11px] opacity-70">
                  Aún no se creó nada. Al confirmar se enviará un correo de activación a cada usuario de las filas
                  listas{errorRows.length > 0 ? "; las filas con error se omiten." : "."}
                </p>
              ) : (
                <InlineAlert tone="warning" title="Ninguna fila se puede invitar">
                  Corrige el archivo con los motivos de abajo y vuelve a cargarlo.
                </InlineAlert>
              )}
              <ErrorList rows={errorRows} />
            </section>
          )}

          {/* Estado: lleno — resultado del commit */}
          {phase === "result" && report && (
            <section className="flex flex-col gap-3 border-t border-[color:var(--di-border)] pt-5" data-testid="bulk-invite-result">
              <InlineAlert
                tone={report.created > 0 && report.rejected + report.notProcessed === 0 ? "success" : "warning"}
                title={report.created > 0 ? "Invitaciones enviadas" : "No se creó ninguna invitación"}
              >
                {report.created} de {report.total} {report.total === 1 ? "fila quedó invitada" : "filas quedaron invitadas"}.
                {report.rejected + report.notProcessed > 0 ? " Revisa las que no se pudieron procesar." : ""}
              </InlineAlert>
              <Counts
                items={[
                  { label: "Filas leídas", value: report.total },
                  { label: "Invitaciones creadas", value: report.created, tone: "success" },
                  { label: "Rechazadas", value: report.rejected, tone: report.rejected > 0 ? "danger" : undefined },
                  ...(report.notProcessed > 0
                    ? [{ label: "Sin procesar", value: report.notProcessed, tone: "danger" as const }]
                    : []),
                ]}
              />
              <ErrorList rows={errorRows} />
              {errorRows.length > 0 && (
                <button
                  type="button"
                  onClick={handleDownloadErrors}
                  className={`${controlCls(false)} self-start`}
                  data-testid="bulk-invite-errors-csv"
                >
                  <Icono nombre="nube-bajar" className="h-3.5 w-3.5" />
                  Descargar CSV de errores
                </button>
              )}
            </section>
          )}
        </div>

        <div className="mt-4 flex shrink-0 flex-wrap justify-end gap-2 border-t border-[color:var(--di-border)] pt-4">
          {phase === "result" || phase === "uncertain" ? (
            <button type="button" onClick={onClose} className={controlCls(false)}>
              Cerrar
            </button>
          ) : (
            <>
              <button type="button" onClick={requestClose} disabled={busy} className={controlCls(false)}>
                Cancelar
              </button>
              {phase === "preview" || phase === "committing" ? (
                <>
                  <button type="button" onClick={reset} disabled={busy} className={controlCls(false)}>
                    Elegir otro archivo
                  </button>
                  <button
                    type="button"
                    onClick={() => void run(false)}
                    disabled={busy || !report || report.valid === 0}
                    data-testid="bulk-invite-confirm"
                    className="inline-flex h-9 items-center gap-1.5 rounded-xl px-4 text-xs font-semibold text-white transition hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-45"
                    style={{ background: PRIMARY_FILL }}
                  >
                    <Icono nombre="check" className={`h-3.5 w-3.5 ${phase === "committing" ? "animate-pulse" : ""}`} />
                    {phase === "committing"
                      ? "Invitando…"
                      : `Confirmar e invitar${report && report.valid > 0 ? ` (${report.valid})` : ""}`}
                  </button>
                </>
              ) : (
                <button
                  type="button"
                  onClick={() => void run(true)}
                  disabled={!ready || !file || busy}
                  data-testid="bulk-invite-validate"
                  className="inline-flex h-9 items-center gap-1.5 rounded-xl px-4 text-xs font-semibold text-white transition hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-45"
                  style={{ background: PRIMARY_FILL }}
                >
                  <Icono nombre="nube-subir" className={`h-3.5 w-3.5 ${phase === "validating" ? "animate-pulse" : ""}`} />
                  {phase === "validating" ? "Validando…" : "Validar archivo"}
                </button>
              )}
            </>
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
}

function announcementFor(phase: Phase, report: BulkInvitationReport | null): string {
  switch (phase) {
    case "validating":
      return "Validando el archivo.";
    case "committing":
      return "Creando las invitaciones.";
    case "preview":
      return report
        ? `Validación completada: ${report.valid} filas listas para invitar y ${report.rejected + report.notProcessed} con error.`
        : "";
    case "result":
      return report
        ? `Proceso terminado: ${report.created} invitaciones creadas y ${report.rejected + report.notProcessed} filas con error.`
        : "";
    case "uncertain":
      return "No se recibió respuesta del servidor. Algunas invitaciones pueden haberse creado.";
    default:
      return "";
  }
}

type CountTone = "success" | "danger";

const COUNT_TONE_COLOR: Record<CountTone, string> = {
  success: "var(--badge-success-fg)",
  danger: "var(--badge-danger-fg)",
};

function Counts({ items }: { items: { label: string; value: number; tone?: CountTone }[] }) {
  return (
    <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
      {items.map((item) => (
        <div key={item.label} className="rounded-xl border border-[color:var(--di-border)] px-3 py-2">
          <dt className="text-[10px] font-semibold uppercase opacity-60">{item.label}</dt>
          <dd
            className="mt-0.5 text-lg font-bold"
            style={item.tone ? { color: COUNT_TONE_COLOR[item.tone] } : undefined}
          >
            {item.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function ErrorList({ rows }: { rows: ReturnType<typeof bulkInviteErrorRows> }) {
  if (rows.length === 0) return null;
  return (
    <div className="flex flex-col gap-1.5" data-testid="bulk-invite-error-list">
      <p className="flex items-center gap-1.5 text-xs font-semibold" style={{ color: "var(--badge-danger-fg)" }}>
        <Icono nombre="alerta" className="h-3.5 w-3.5" />
        Filas con error ({rows.length})
      </p>
      {/* Región con scroll accesible por teclado (tabIndex + nombre). */}
      <ol
        tabIndex={0}
        aria-label="Filas con error"
        className="max-h-56 divide-y divide-[color:var(--di-border)] overflow-y-auto rounded-xl border border-[color:var(--di-border)] text-xs focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
      >
        {rows.map((row) => (
          <li key={`${row.rowNumber}-${row.email ?? ""}`} className="flex flex-col gap-0.5 px-3 py-2">
            <span>
              <span className="font-semibold">Fila {row.rowNumber}</span>
              <span className="opacity-60"> · </span>
              <span className="break-all">{row.email ?? "(sin correo)"}</span>
            </span>
            <span className="opacity-80">{bulkInviteRowReason(row)}</span>
          </li>
        ))}
      </ol>
    </div>
  );
}

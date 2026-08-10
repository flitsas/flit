"use client";

// Documentos personalizados por compañía (HU #11315, Feature #11309, ADR-0042). Sustituye, por tipo
// (mandato | tramite_virtual), el documento generado por FLIT en TODOS los trámites de la compañía.
// Visibilidad condicionada al canal (DT-7): solo se monta si `enrutamientoSMTP === "TENANT_API"`, leído
// SIEMPRE de la configuración que devuelve la API (nunca de una lista fija en el bundle). Precedente
// visual: panel de escrituras/representantes legales (`DeedsFormPanel`, `RepresentativesAndVaultTab`).
import { useCallback, useEffect, useRef, useState } from "react";
import {
  AlertTriangle,
  Eye,
  FileText,
  History,
  Loader2,
  RotateCcw,
  Undo2,
  UploadCloud,
} from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  activatePersonalizedDocumentVersion,
  deactivatePersonalizedDocument,
  fetchPersonalizedDocuments,
  firstValidationError,
  getPersonalizedDocumentView,
  isChannelNotEnabled,
  isVersionNotActivable,
  PERSONALIZED_DOCUMENT_TYPES,
  uploadAndConfirmPersonalizedDocument,
  type PersonalizedDocumentGroup,
  type PersonalizedDocumentType,
  type PersonalizedDocumentVersion,
} from "@/lib/api/admin-personalized-documents";
import { ApiValidationError } from "@/lib/api/types";
import type { EnrutamientoSMTP } from "@/lib/api/types";

export interface PersonalizedDocumentsPanelProps {
  tenantId: string;
  /**
   * Canal de notificaciones vigente de la compañía, leído de `TenantSettings.enrutamientoSMTP` (la
   * MISMA respuesta de configuración que ya carga la ficha de compañía — sin llamada adicional).
   * `FLIT_SMTP` ⇒ el panel no se renderiza en absoluto (DT-7 del plan técnico).
   */
  enrutamientoSMTP: EnrutamientoSMTP;
}

interface DocumentMeta {
  title: string;
  description: string;
  /** Advertencia mostrada ANTES de activar (alta o reactivación) — AC de la HU. */
  activationWarning: string;
}

const DOCUMENT_META: Record<PersonalizedDocumentType, DocumentMeta> = {
  mandato: {
    title: "Mandato",
    description:
      "Sustituye el mandato generado por FLIT en todos los trámites nuevos de esta compañía.",
    activationWarning:
      "Este documento personalizado NO incluirá los bloques de firma del mandatario que sí trae el mandato generado por FLIT.",
  },
  tramite_virtual: {
    title: "Solicitud de trámite virtual",
    description:
      "Sustituye la solicitud de trámite virtual generada por FLIT en todos los trámites nuevos de esta compañía.",
    activationWarning:
      "Este documento personalizado NO incluirá el sello del baúl de firmas que sí trae la solicitud generada por FLIT.",
  },
};

/**
 * Punto de entrada: aplica la compuerta de visibilidad por canal (DT-7). Con `FLIT_SMTP` no se
 * monta NADA — ni el fetch, ni el contenedor — para que no quede ni un rastro visual del módulo.
 */
export function PersonalizedDocumentsPanel({ tenantId, enrutamientoSMTP }: PersonalizedDocumentsPanelProps) {
  if (enrutamientoSMTP !== "TENANT_API") {
    return null;
  }
  return <PersonalizedDocumentsPanelContent tenantId={tenantId} />;
}

function PersonalizedDocumentsPanelContent({ tenantId }: { tenantId: string }) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const [groups, setGroups] = useState<Map<string, PersonalizedDocumentGroup>>(new Map());

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchPersonalizedDocuments(tenantId, signal);
        if (signal?.aborted) return;
        setGroups(new Map(data.map((g) => [g.documentType, g])));
        setStatus("ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial de datos al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  return (
    <section aria-label="Documentos personalizados de la compañía" className="space-y-4">
      <header>
        <h2 className="flex items-center gap-2 text-sm font-semibold">
          <FileText className="h-4 w-4" style={{ color: "#557EFF" }} aria-hidden />
          Documentos personalizados
        </h2>
        <p className="mt-0.5 max-w-2xl text-[11px] opacity-60">
          Reemplaza, por tipo de documento, el que FLIT genera automáticamente en cada trámite de esta
          compañía. Solo disponible con el canal «API Renting cliente».
        </p>
      </header>

      <UiStateBoundary
        status={status}
        onRetry={() => void load()}
        errorMessage="No se pudieron cargar los documentos personalizados."
      >
        <div className="space-y-4">
          {PERSONALIZED_DOCUMENT_TYPES.map((documentType) => (
            <PersonalizedDocumentSection
              key={documentType}
              tenantId={tenantId}
              documentType={documentType}
              group={groups.get(documentType) ?? null}
              onChanged={() => void load()}
            />
          ))}
        </div>
      </UiStateBoundary>
    </section>
  );
}

type DialogState =
  | { kind: "activate"; file: File }
  | { kind: "reactivate"; versionId: string; version: number }
  | { kind: "deactivate" }
  | null;

interface PersonalizedDocumentSectionProps {
  tenantId: string;
  documentType: PersonalizedDocumentType;
  group: PersonalizedDocumentGroup | null;
  onChanged: () => void;
}

function PersonalizedDocumentSection({
  tenantId,
  documentType,
  group,
  onChanged,
}: PersonalizedDocumentSectionProps) {
  const { show } = useToast();
  const meta = DOCUMENT_META[documentType];
  const [file, setFile] = useState<File | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [rowBusyId, setRowBusyId] = useState<string | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<DialogState>(null);
  const [preview, setPreview] = useState<{ label: string; url: string | null; loading: boolean; error: string | null } | null>(
    null,
  );
  const fileInputRef = useRef<HTMLInputElement>(null);

  const active = group?.active ?? null;
  const history = group?.history ?? [];
  const hasAnything = active !== null || history.length > 0;

  const inputId = `personalized-doc-file-${documentType}`;

  const resetFileInput = () => {
    setFile(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  };

  const runUploadAndConfirm = async (chosenFile: File) => {
    setSubmitting(true);
    setUploadError(null);
    try {
      await uploadAndConfirmPersonalizedDocument(tenantId, documentType, chosenFile);
      show(`${meta.title}: documento personalizado activado.`, "success");
      resetFileInput();
      setDialog(null);
      onChanged();
    } catch (err) {
      if (err instanceof ApiValidationError) {
        const detail = firstValidationError(err);
        setUploadError(detail?.message ?? "El documento no pasó la validación del servidor.");
        setDialog(null);
      } else if (isChannelNotEnabled(err)) {
        show("Esta funcionalidad requiere el canal «API Renting cliente».", "error");
        setDialog(null);
      } else {
        setUploadError("No se pudo cargar el documento. Intenta de nuevo.");
        setDialog(null);
      }
    } finally {
      setSubmitting(false);
    }
  };

  const runReactivate = async (versionId: string) => {
    setRowBusyId(versionId);
    try {
      await activatePersonalizedDocumentVersion(tenantId, versionId);
      show(`${meta.title}: versión reactivada.`, "success");
      setDialog(null);
      onChanged();
    } catch (err) {
      if (isVersionNotActivable(err)) {
        show("Esa versión no se puede activar en su estado actual.", "error");
      } else if (isChannelNotEnabled(err)) {
        show("Esta funcionalidad requiere el canal «API Renting cliente».", "error");
      } else {
        show("No se pudo reactivar la versión. Intenta de nuevo.", "error");
      }
      setDialog(null);
    } finally {
      setRowBusyId(null);
    }
  };

  const runDeactivate = async () => {
    setSubmitting(true);
    try {
      await deactivatePersonalizedDocument(tenantId, documentType);
      show(`${meta.title}: se volvió al documento del sistema.`, "success");
      setDialog(null);
      onChanged();
    } catch (err) {
      if (isChannelNotEnabled(err)) {
        show("Esta funcionalidad requiere el canal «API Renting cliente».", "error");
      } else {
        show("No se pudo volver al documento del sistema. Intenta de nuevo.", "error");
      }
      setDialog(null);
    } finally {
      setSubmitting(false);
    }
  };

  const openPreview = async (version: PersonalizedDocumentVersion) => {
    setPreview({ label: `${meta.title} · v${version.version}`, url: null, loading: true, error: null });
    try {
      const result = await getPersonalizedDocumentView(tenantId, version.id);
      setPreview({ label: `${meta.title} · v${version.version}`, url: result.url, loading: false, error: null });
    } catch {
      setPreview({
        label: `${meta.title} · v${version.version}`,
        url: null,
        loading: false,
        error: "No se pudo generar la vista previa. Intenta de nuevo.",
      });
    }
  };

  const onDialogConfirm = () => {
    if (!dialog) return;
    if (dialog.kind === "activate") void runUploadAndConfirm(dialog.file);
    else if (dialog.kind === "reactivate") void runReactivate(dialog.versionId);
    else void runDeactivate();
  };

  return (
    <div className="rounded-2xl border p-4" style={{ borderColor: "#DFE5ED" }}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-xs font-semibold">{meta.title}</h3>
          <p className="mt-0.5 max-w-md text-[11px] opacity-60">{meta.description}</p>
        </div>
        {active && (
          <button
            type="button"
            onClick={() => setDialog({ kind: "deactivate" })}
            disabled={submitting}
            className="flex shrink-0 items-center gap-1.5 rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
            style={{ borderColor: "#DFE5ED" }}
          >
            <Undo2 className="h-3.5 w-3.5" aria-hidden />
            Volver al documento del sistema
          </button>
        )}
      </div>

      {/* Estado vigente */}
      <div className="mt-3">
        {active ? (
          <div
            className="flex flex-wrap items-center justify-between gap-3 rounded-xl border px-3 py-2.5"
            style={{ borderColor: "#00DBD5", background: "rgba(0,219,213,0.06)" }}
          >
            <div className="flex min-w-0 items-center gap-2">
              <FileText className="h-4 w-4 shrink-0" style={{ color: "#0a8f8b" }} aria-hidden />
              <span className="min-w-0">
                <span className="block truncate text-xs font-semibold">
                  {active.filename} <span className="opacity-60">· v{active.version}</span>
                </span>
                <span className="block text-[11px] opacity-60">
                  Vigente desde {formatDate(active.activatedAt)} · {active.pageCount ?? "—"} páginas
                </span>
              </span>
            </div>
            <button
              type="button"
              onClick={() => void openPreview(active)}
              className="flex shrink-0 items-center gap-1.5 rounded-xl px-3 py-1.5 text-[11px] font-semibold text-white"
              style={{ background: "#557EFF" }}
            >
              <Eye className="h-3.5 w-3.5" aria-hidden />
              Vista previa
            </button>
          </div>
        ) : (
          <p
            className="rounded-xl border px-3 py-2 text-[11px] opacity-70"
            style={{ borderColor: "#DFE5ED" }}
            data-testid={`personalized-doc-empty-${documentType}`}
          >
            Ningún documento personalizado activo: se usa el generado por FLIT.
          </p>
        )}
      </div>

      {/* Carga de un PDF nuevo */}
      <div className="mt-3">
        {uploadError && (
          <p role="alert" className="mb-2 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
            {uploadError}
          </p>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <label
            htmlFor={inputId}
            className="flex flex-1 cursor-pointer items-center gap-2 rounded-xl border border-dashed px-3 py-2.5 text-xs"
            style={file ? { borderColor: "#557EFF" } : { borderColor: "#DFE5ED" }}
          >
            {file ? (
              <FileText className="h-4 w-4 shrink-0" style={{ color: "#557EFF" }} aria-hidden />
            ) : (
              <UploadCloud className="h-4 w-4 shrink-0 opacity-60" aria-hidden />
            )}
            <span className="min-w-0 truncate font-medium">
              {file ? file.name : "Selecciona el PDF del documento personalizado"}
            </span>
          </label>
          <input
            id={inputId}
            ref={fileInputRef}
            type="file"
            accept="application/pdf"
            className="sr-only"
            onChange={(e) => {
              setUploadError(null);
              setFile(e.target.files?.[0] ?? null);
            }}
          />
          <button
            type="button"
            disabled={!file || submitting}
            onClick={() => file && setDialog({ kind: "activate", file })}
            className="flex shrink-0 items-center gap-2 rounded-xl px-4 py-2.5 text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden />}
            Cargar y activar
          </button>
        </div>
        <p className="mt-1 text-[11px] opacity-50">
          PDF sin cifrar, hasta 20 MB y 30 páginas. El servidor valida el archivo al confirmarlo.
        </p>
      </div>

      {/* Historial */}
      {hasAnything && (
        <div className="mt-4">
          <h4 className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold opacity-70">
            <History className="h-3.5 w-3.5" aria-hidden />
            Historial
          </h4>
          <div className="overflow-x-auto rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
            <table className="w-full text-left text-[11px]" aria-label={`Historial de ${meta.title}`}>
              <thead>
                <tr className="bg-[#F4F7FC] dark:bg-white/5">
                  <th scope="col" className="px-3 py-2 font-semibold">Versión</th>
                  <th scope="col" className="px-3 py-2 font-semibold">Autor</th>
                  <th scope="col" className="px-3 py-2 font-semibold">Fecha</th>
                  <th scope="col" className="px-3 py-2 font-semibold">Páginas</th>
                  <th scope="col" className="px-3 py-2 font-semibold">Vigencia</th>
                  <th scope="col" className="px-3 py-2 font-semibold">Acciones</th>
                </tr>
              </thead>
              <tbody>
                {[...history]
                  .sort((a, b) => b.version - a.version)
                  .map((v) => (
                    <tr key={v.id} className="border-t" style={{ borderColor: "#DFE5ED" }}>
                      <td className="px-3 py-2 font-mono">v{v.version}</td>
                      <td className="px-3 py-2 font-mono" title={v.createdBy ?? undefined}>
                        {shortId(v.createdBy)}
                      </td>
                      <td className="px-3 py-2">{formatDate(v.createdAt)}</td>
                      <td className="px-3 py-2">{v.pageCount ?? "—"}</td>
                      <td className="px-3 py-2">
                        {v.isActive ? (
                          <span
                            className="rounded-md px-1.5 py-0.5 text-[10px] font-semibold"
                            style={{ color: "#0a8f8b", background: "rgba(0,219,213,0.12)" }}
                          >
                            Vigente
                          </span>
                        ) : (
                          <span className="opacity-60">Histórico</span>
                        )}
                      </td>
                      <td className="px-3 py-2">
                        <div className="flex items-center gap-2">
                          <button
                            type="button"
                            onClick={() => void openPreview(v)}
                            className="text-[11px] font-semibold"
                            style={{ color: "#557EFF" }}
                          >
                            Ver
                          </button>
                          {!v.isActive && (
                            <button
                              type="button"
                              disabled={rowBusyId === v.id}
                              onClick={() => setDialog({ kind: "reactivate", versionId: v.id, version: v.version })}
                              className="flex items-center gap-1 text-[11px] font-semibold disabled:opacity-50"
                              style={{ color: "#557EFF" }}
                            >
                              <RotateCcw className="h-3 w-3" aria-hidden />
                              Reactivar
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Advertencia + confirmación explícita antes de activar / volver al sistema */}
      <DocumentActionDialog
        dialog={dialog}
        documentTitle={meta.title}
        warningText={meta.activationWarning}
        busy={submitting || rowBusyId !== null}
        onConfirm={onDialogConfirm}
        onCancel={() => setDialog(null)}
      />

      {/* Vista previa (no cambia la versión vigente) */}
      {preview && (
        <Modal
          open
          onClose={() => setPreview(null)}
          title={`Vista previa · ${preview.label}`}
          size="lg"
          icon={Eye}
        >
          {preview.loading && (
            <p className="flex items-center gap-2 text-sm opacity-70">
              <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Generando vista previa…
            </p>
          )}
          {preview.error && (
            <p role="alert" className="text-sm" style={{ color: "#FF4E00" }}>
              {preview.error}
            </p>
          )}
          {preview.url && (
            <iframe
              src={preview.url}
              title={`Vista previa de ${preview.label}`}
              className="h-[70vh] w-full rounded-xl border"
              style={{ borderColor: "#DFE5ED" }}
            />
          )}
        </Modal>
      )}
    </div>
  );
}

function DocumentActionDialog({
  dialog,
  documentTitle,
  warningText,
  busy,
  onConfirm,
  onCancel,
}: {
  dialog: DialogState;
  documentTitle: string;
  warningText: string;
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const [acknowledged, setAcknowledged] = useState(false);

  useEffect(() => {
    // Reinicia el reconocimiento de la advertencia cada vez que se abre un diálogo distinto.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza el checkbox al abrir el diálogo
    setAcknowledged(false);
  }, [dialog]);

  if (!dialog) return null;

  const isActivation = dialog.kind === "activate" || dialog.kind === "reactivate";
  const title = isActivation
    ? `Activar ${documentTitle}`
    : `Volver al documento del sistema — ${documentTitle}`;
  const confirmLabel = isActivation ? "Confirmar activación" : "Volver al sistema";
  const canConfirm = !busy && (!isActivation || acknowledged);

  return (
    <Modal
      open
      onClose={onCancel}
      busy={busy}
      size="sm"
      icon={isActivation ? AlertTriangle : Undo2}
      iconBg={isActivation ? "#F9AC00" : "#557EFF"}
      title={title}
    >
      {isActivation ? (
        <>
          <div
            role="alert"
            className="mt-2 rounded-xl border px-3 py-2 text-xs"
            style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
          >
            {warningText}
          </div>
          <label className="mt-3 flex items-start gap-2 text-xs">
            <input
              type="checkbox"
              checked={acknowledged}
              onChange={(e) => setAcknowledged(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-[#557EFF]"
            />
            <span>Entiendo la advertencia y confirmo que quiero activar este documento.</span>
          </label>
        </>
      ) : (
        <p className="mt-2 text-sm opacity-80">
          El trámite volverá a usar el documento generado por FLIT. El histórico se conserva y puedes
          reactivar cualquier versión anterior más adelante.
        </p>
      )}
      <div className="mt-5 flex gap-3">
        <button
          type="button"
          onClick={onCancel}
          disabled={busy}
          className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
        >
          Cancelar
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={!canConfirm}
          className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
          style={{ background: "#557EFF" }}
        >
          {busy ? "Procesando…" : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}

function shortId(id: string | null): string {
  if (!id) return "—";
  return id.slice(0, 8);
}

function formatDate(iso: string | null): string {
  if (!iso) return "—";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return "—";
  return parsed.toLocaleDateString("es-CO", { year: "numeric", month: "2-digit", day: "2-digit" });
}

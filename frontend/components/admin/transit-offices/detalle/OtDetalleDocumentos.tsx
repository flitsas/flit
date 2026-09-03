"use client";

import { useCallback, useEffect, useState } from "react";
import { Check, Download, Eye, RefreshCw } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DocumentPreviewModal } from "@/components/shared/DocumentPreviewModal";
import {
  fetchOtAttachmentPreviewUrl,
  fetchOtDocuments,
  generarOtConsolidadoMaestro,
} from "@/lib/api/admin-ot";
import { downloadFile } from "@/lib/api/download";
import type { OtApiScope, OtProcedureAttachment } from "@/lib/api/admin-ot";
import { OtVacio } from "./OtDetallePrimitivos";
import { OT_BLUE, OT_GREEN } from "./ot-detalle-visual";

export interface OtDetalleDocumentosProps {
  /** ID del trámite de cliente OT. */
  procedureId: string;
  scope?: OtApiScope;
  /** Si es true el OT no puede reconstruir el consolidado (solo ver documentos). */
  readOnly?: boolean;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDate(iso: string): string {
  try {
    return new Intl.DateTimeFormat("es-CO", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

/** Botón de icono de la tarjeta de documento. */
function AccionDoc({
  icon: Icon,
  label,
  onClick,
  disabled,
  title,
  blanco,
}: {
  icon: typeof Eye;
  label: string;
  onClick: () => void;
  disabled?: boolean;
  title?: string;
  /** Sobre la tarjeta azul del consolidado el icono va en blanco. */
  blanco?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title ?? label}
      aria-label={label}
      className={`grid h-7 w-7 place-items-center rounded-lg border transition disabled:opacity-50 ${
        blanco ? "border-white/40 text-white" : "border-[#DFE5ED] hover:bg-[#557EFF]/10 dark:border-white/10"
      }`}
      style={blanco ? undefined : { color: OT_BLUE }}
    >
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
    </button>
  );
}

/**
 * Acordeón «Documentos del Trámite» (HU #12061).
 *
 * Sustituye a la tabla de cuatro columnas por la rejilla de tarjetas del prototipo, con el
 * consolidado del expediente destacado en azul al final. Es la misma funcionalidad —previsualizar,
 * descargar, ver y reconstruir el consolidado— servida por los mismos endpoints; lo que cambia es
 * la forma.
 *
 * El sello verde de «revisado» es local a la sesión y no viaja a ninguna parte: marca lo que el
 * revisor ya abrió durante esta consulta, que en un expediente de once documentos es la diferencia
 * entre saber por dónde iba y volver a empezar.
 */
export function OtDetalleDocumentos({
  procedureId,
  scope,
  readOnly = false,
}: OtDetalleDocumentosProps) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [attachments, setAttachments] = useState<OtProcedureAttachment[]>([]);
  const [consolidadoActing, setConsolidadoActing] = useState(false);
  const [revisados, setRevisados] = useState<Set<string>>(new Set());

  const [previewItem, setPreviewItem] = useState<OtProcedureAttachment | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchOtDocuments(procedureId, scope);
        if (signal?.aborted) return;
        const list = result.data ?? [];
        setAttachments(list);
        setStatus(list.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [procedureId, scope],
  );

  useEffect(() => {
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(c.signal);
    return () => c.abort();
  }, [load]);

  const handlePreview = async (item: OtProcedureAttachment) => {
    setRevisados((prev) => new Set(prev).add(item.id));
    setPreviewItem(item);
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewError(null);
    setPreviewLoading(true);
    try {
      const result = await fetchOtAttachmentPreviewUrl(procedureId, item.id, scope);
      // El file-manager sirve el objeto como binary/octet-stream sin Content-Disposition, por lo que
      // un <iframe> con la URL directa fuerza descarga. Re-empaquetamos los bytes como Blob con el
      // mimetype real para forzar el render inline en el navegador (S3 permite CORS GET).
      const blob = await fetch(result.url).then((r) => {
        if (!r.ok) throw new Error(String(r.status));
        return r.blob();
      });
      const typed = item.mimetype ? new Blob([blob], { type: item.mimetype }) : blob;
      setPreviewUrl(URL.createObjectURL(typed));
    } catch {
      setPreviewError("No se pudo obtener la URL de previsualización. Descarga el archivo.");
    } finally {
      setPreviewLoading(false);
    }
  };

  const closePreview = () => {
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewItem(null);
    setPreviewError(null);
  };

  const handleDownload = async (item: OtProcedureAttachment) => {
    setRevisados((prev) => new Set(prev).add(item.id));
    try {
      await downloadFile(
        `/api/v1/admin/ot/client-procedures/${procedureId}/documents/${item.id}/download`,
        {
          query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
          fallbackFilename: item.filename,
        },
      );
    } catch {
      show("No se pudo descargar el archivo.", "error");
    }
  };

  const handleConsolidado = async (force = false) => {
    // Botón único (Feature #10701): muestra el consolidado del expediente INLINE. Si el OT puede
    // generar, "asegura" el vigente — el backend regenera solo si la marca lo pide (nunca generado
    // o invalidado por CUALQUIER cambio del expediente: adjuntar o borrar un documento, editar
    // datos, la decisión del OT, una transición de estado…) y reutiliza si ya está vigente.
    // `force` se salta ese atajo y reconstruye. En modo QX read-only no se puede generar: solo se
    // muestra el consolidado existente.
    setConsolidadoActing(true);
    try {
      if (!readOnly) {
        // El consolidado maestro es el expediente completo: el 100% de los documentos cargados y
        // generados del trámite, ordenados por la matriz documental.
        const res = await generarOtConsolidadoMaestro(procedureId, scope, force);
        if (res.regenerado) show("Consolidado generado.", "success");
        void load();
        await handlePreview({
          id: res.document.attachmentId,
          tipo: res.document.tipo,
          filename: res.document.filename,
          mimetype: "application/pdf",
          sizeBytes: 0,
          sha256: res.document.sha256,
          source: "system",
          uploadedAt: "",
        });
        return;
      }
      const consolidado =
        attachments.find((a) => a.tipo === "consolidado_maestro") ??
        attachments.find((a) => a.tipo === "consolidado");
      if (!consolidado) {
        show("El trámite aún no tiene consolidado generado.", "error");
        return;
      }
      await handlePreview(consolidado);
    } catch {
      show("No se pudo abrir el consolidado.", "error");
    } finally {
      setConsolidadoActing(false);
    }
  };

  return (
    <>
      <DocumentPreviewModal
        open={!!previewItem}
        onClose={closePreview}
        title={previewItem?.filename ?? "Previsualización"}
        mimetype={previewItem?.mimetype ?? null}
        url={previewUrl}
        loading={previewLoading}
        error={previewError}
        onDownload={previewItem ? () => void handleDownload(previewItem) : undefined}
      />

      <div className="space-y-3" data-testid="ot-detalle-documentos">
        {/* El consolidado NO puede quedar dentro del guardián de estado: con el expediente vacío
            este pinta su mensaje en lugar de los hijos, y precisamente entonces —cuando no hay
            adjuntos— el organismo sigue necesitando poder abrir o reconstruir el consolidado. */}
        {status === "loading" || status === "error" ? (
          <UiStateBoundary
            status={status}
            errorMessage="Error al cargar los documentos del trámite."
            onRetry={() => void load()}
            skeletonRows={4}
          >
            <span />
          </UiStateBoundary>
        ) : (
          <>
            {attachments.length === 0 ? (
              <OtVacio mensaje="Este trámite no tiene documentos adjuntos." />
            ) : null}
            <ul className="grid list-none grid-cols-1 gap-3 p-0 sm:grid-cols-2 lg:grid-cols-3">
            {attachments.map((att) => (
              <li
                key={att.id}
                className="flex items-center gap-2 rounded-xl border border-[#DFE5ED] px-3 py-2 dark:border-white/5"
              >
                {revisados.has(att.id) ? (
                  <span
                    className="grid h-5 w-5 shrink-0 place-items-center rounded-full"
                    style={{ background: OT_GREEN }}
                    title="Revisado en esta consulta"
                  >
                    <Check className="h-3 w-3 text-white" aria-hidden="true" />
                    <span className="sr-only">Revisado</span>
                  </span>
                ) : (
                  <span className="h-5 w-5 shrink-0" aria-hidden="true" />
                )}
                <span className="min-w-0 flex-1">
                  <span
                    className="block truncate text-xs font-semibold"
                    style={{ color: OT_BLUE }}
                    title={att.filename}
                  >
                    {att.filename}
                  </span>
                  <span className="block truncate text-[10px] opacity-60">
                    {[att.tipo, formatSize(att.sizeBytes), att.uploadedAt && formatDate(att.uploadedAt)]
                      .filter(Boolean)
                      .join(" · ")}
                  </span>
                </span>
                <span className="flex shrink-0 items-center gap-1.5">
                  <AccionDoc
                    icon={Eye}
                    label={`Previsualizar ${att.filename}`}
                    onClick={() => void handlePreview(att)}
                  />
                  <AccionDoc
                    icon={Download}
                    label={`Descargar ${att.filename}`}
                    onClick={() => void handleDownload(att)}
                  />
                </span>
              </li>
            ))}

            {/* El consolidado no es un adjunto más: es el expediente entero en un PDF, y por eso el
                prototipo lo destaca en azul al final de la rejilla. */}
            <li
              // Ocupa la fila entera. En el prototipo cabe en una celda porque su rotulo es corto;
              // aquí, junto a nombres de fichero reales, se truncaba a «Consolidado de docu…».
              className="flex items-center gap-2 rounded-xl border px-3 py-2 sm:col-span-2 lg:col-span-3"
              style={{ background: OT_BLUE, borderColor: OT_BLUE }}
            >
              <span className="h-5 w-5 shrink-0" aria-hidden="true" />
              <span className="min-w-0 flex-1 truncate text-xs font-semibold text-white">
                {consolidadoActing ? "Abriendo el consolidado…" : "Consolidado de documentos"}
              </span>
              <span className="flex shrink-0 items-center gap-1.5">
                <AccionDoc
                  blanco
                  icon={Eye}
                  label="Ver consolidado del expediente"
                  title="Muestra el consolidado del expediente; lo genera si aún no está o si cambió el trámite"
                  disabled={consolidadoActing}
                  onClick={() => void handleConsolidado()}
                />
                {/* Salida manual: reconstruye el PDF ignorando la marca de vigencia. El expediente se
                    invalida solo cuando cambia, pero si el operador duda de lo que ve —o una vía de
                    invalidación falla— no debe quedarse sin forma de comprobarlo. */}
                {!readOnly ? (
                  <AccionDoc
                    blanco
                    icon={RefreshCw}
                    label="Actualizar el consolidado del expediente"
                    title="Reconstruye el expediente consolidado con el contenido actual del trámite"
                    disabled={consolidadoActing}
                    onClick={() => void handleConsolidado(true)}
                  />
                ) : null}
              </span>
            </li>
            </ul>
          </>
        )}
      </div>
    </>
  );
}

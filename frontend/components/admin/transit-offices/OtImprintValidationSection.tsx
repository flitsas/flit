"use client";

import { useCallback, useMemo, useState } from "react";
import { FileText, ShieldCheck } from "lucide-react";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  fetchImprintSignaturePreviewUrl,
  fetchListImprintSignatures,
  imprintHasPdf,
  validateImprintSignature,
  type ImprintSignatureDto,
  type ImprintSignatureValidationResultKind,
} from "@/lib/api/admin-ot-imprint-signatures";
import { ApiError } from "@/lib/api/types";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";

type ViewPhase = "idle" | "loading" | "error" | "empty" | "ready";

const INPUT_CLS =
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162244] placeholder:text-[#59677D]/70 uppercase focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";

const TEXTAREA_CLS =
  "w-full min-h-[8rem] rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 font-mono text-xs text-[#162244] placeholder:text-[#59677D]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";

export function OtImprintValidationSection({ transitOfficeId }: { transitOfficeId: string }) {
  const { show } = useToast();
  const [placaInput, setPlacaInput] = useState("");
  const [appliedPlaca, setAppliedPlaca] = useState("");
  const [phase, setPhase] = useState<ViewPhase>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [rows, setRows] = useState<ImprintSignatureDto[]>([]);
  const [validationById, setValidationById] = useState<
    Record<string, { result: ImprintSignatureValidationResultKind; failureReason: string | null }>
  >({});
  const [openingPdfId, setOpeningPdfId] = useState<string | null>(null);

  const [modalRow, setModalRow] = useState<ImprintSignatureDto | null>(null);
  const [signatureInput, setSignatureInput] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(
    async (placa: string) => {
      const trimmed = placa.trim();
      if (!trimmed) {
        setPhase("idle");
        setRows([]);
        setAppliedPlaca("");
        return;
      }

      setPhase("loading");
      setErrorMessage(null);
      try {
        const data = await fetchListImprintSignatures(trimmed, undefined, { transitOfficeId });
        setRows(data);
        setAppliedPlaca(trimmed);
        setValidationById({});
        setPhase(data.length === 0 ? "empty" : "ready");
      } catch (err) {
        setRows([]);
        setPhase("error");
        setErrorMessage(
          err instanceof ApiError ? err.message : "No se pudieron consultar las improntas firmadas.",
        );
      }
    },
    [transitOfficeId],
  );

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    void load(placaInput);
  };

  const openValidateModal = (row: ImprintSignatureDto) => {
    setModalRow(row);
    setSignatureInput("");
  };

  const closeValidateModal = () => {
    if (submitting) return;
    setModalRow(null);
    setSignatureInput("");
  };

  const handleViewPdf = useCallback(
    async (row: ImprintSignatureDto) => {
      if (!imprintHasPdf(row) || openingPdfId) return;
      setOpeningPdfId(row.id);
      try {
        await openPdfBlobInNewTab(async () => {
          const preview = await fetchImprintSignaturePreviewUrl(row.id, undefined, {
            transitOfficeId,
          });
          const response = await fetch(preview.url);
          if (!response.ok) {
            throw new Error(`preview_fetch_${response.status}`);
          }
          const blob = await response.blob();
          return new Blob([blob], { type: "application/pdf" });
        });
      } catch (err) {
        show(
          err instanceof ApiError ? err.message : "No se pudo abrir el PDF de la impronta.",
          "error",
        );
      } finally {
        setOpeningPdfId(null);
      }
    },
    [openingPdfId, show, transitOfficeId],
  );

  const handleAcceptValidation = useCallback(async () => {
    if (!modalRow) return;
    const signature = signatureInput.trim();
    if (!signature) {
      show("Ingrese la firma digital de la impronta.", "error");
      return;
    }

    const rowId = modalRow.id;
    setSubmitting(true);
    try {
      const result = await validateImprintSignature(rowId, signature, undefined, {
        transitOfficeId,
      });
      setValidationById((prev) => ({
        ...prev,
        [rowId]: { result: result.result, failureReason: result.failureReason },
      }));
      // Cerrar primero: el toast (z-100) queda tapado por el overlay del Modal (mismo z-index).
      setModalRow(null);
      setSignatureInput("");
      if (result.result === "valid") {
        show("La firma corresponde a esta impronta.", "success");
      } else if (result.result === "invalid") {
        show(result.failureReason?.trim() || "La firma no corresponde a esta impronta.", "error");
      } else {
        show("Impronta no encontrada.", "error");
      }
    } catch (err) {
      show(err instanceof ApiError ? err.message : "No se pudo validar la firma.", "error");
    } finally {
      setSubmitting(false);
    }
  }, [modalRow, show, signatureInput, transitOfficeId]);

  const columns: DataTableColumn<ImprintSignatureDto>[] = useMemo(
    () => [
      {
        key: "placa",
        header: "Placa",
        cellClassName: "font-mono font-semibold uppercase",
        render: (row) => row.placa,
      },
      {
        key: "moduleCode",
        header: "Módulo",
        render: (row) => row.moduleCode,
      },
      {
        key: "signedAt",
        header: "Firmado",
        render: (row) => formatDateTime(row.signedAt),
      },
      {
        key: "documentHash",
        header: "Hash documento",
        cellClassName: "font-mono",
        render: (row) => (
          <span title={row.documentHash} className="inline-block max-w-[9rem] truncate">
            {shortHash(row.documentHash)}
          </span>
        ),
      },
      {
        key: "ownerSignature",
        header: "Sin firma propietario",
        render: (row) =>
          row.wasSignedWithoutOwnerSignature ? (
            <StatusBadge label="Sí" tone="warning" />
          ) : (
            <StatusBadge label="No" tone="neutral" />
          ),
      },
      {
        key: "validation",
        header: "Validación",
        render: (row) => {
          const validation = validationById[row.id];
          if (!validation) return "—";
          if (validation.result === "valid") {
            return <StatusBadge label="Válida" tone="success" />;
          }
          if (validation.result === "invalid") {
            return (
              <StatusBadge
                label="Inválida"
                tone="danger"
                ariaLabel={
                  validation.failureReason
                    ? `Firma inválida: ${validation.failureReason}`
                    : "Firma inválida"
                }
              />
            );
          }
          return <StatusBadge label="No encontrada" tone="neutral" />;
        },
      },
      {
        key: "actions",
        header: "Acción",
        align: "right",
        render: (row) => {
          const canViewPdf = imprintHasPdf(row);
          const opening = openingPdfId === row.id;
          return (
            <div className="inline-flex flex-wrap items-center justify-end gap-1.5">
              <button
                type="button"
                className="inline-flex items-center gap-1.5 rounded-xl border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold text-[#162244] disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/15 dark:text-white"
                aria-label={
                  canViewPdf
                    ? `Ver PDF de impronta ${row.placa}`
                    : `PDF no disponible para impronta ${row.placa}`
                }
                disabled={!canViewPdf || openingPdfId !== null}
                onClick={() => void handleViewPdf(row)}
                data-testid={`ot-imprint-view-pdf-${row.id}`}
              >
                <FileText className="h-3.5 w-3.5" aria-hidden />
                {opening ? "Abriendo…" : "Ver PDF"}
              </button>
              <button
                type="button"
                className="inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-semibold text-white"
                style={{ background: "#557EFF" }}
                aria-label={`Validar firma de impronta ${row.placa}`}
                onClick={() => openValidateModal(row)}
              >
                <ShieldCheck className="h-3.5 w-3.5" aria-hidden />
                Validar firma
              </button>
            </div>
          );
        },
      },
    ],
    [handleViewPdf, openingPdfId, validationById],
  );

  const boundaryStatus =
    phase === "loading"
      ? "loading"
      : phase === "error"
        ? "error"
        : phase === "empty"
          ? "empty"
          : phase === "ready"
            ? "ready"
            : "empty";

  return (
    <div className="flex flex-col gap-4" data-testid="ot-imprint-validation-section">
      <div>
        <h2 className="text-sm font-semibold text-[#162244] dark:text-white">
          Validación de firma de impronta
        </h2>
        <p className="mt-1 text-xs leading-relaxed text-[#59677D] dark:text-white/65">
          Consulte por placa, abra el PDF firmado y pegue la firma digital para confirmar si
          corresponde a esa impronta.
        </p>
      </div>

      <form
        className="flex flex-wrap items-end gap-3"
        onSubmit={handleSearch}
        aria-label="Consultar improntas firmadas por placa"
      >
        <label className="min-w-[10rem] flex-1 sm:max-w-xs">
          <span className="mb-1 block text-xs font-semibold text-[#162244] dark:text-white">
            Placa
          </span>
          <input
            id="ot-imprint-validation-placa"
            type="text"
            value={placaInput}
            onChange={(e) => setPlacaInput(e.target.value.toUpperCase())}
            placeholder="ABC123"
            className={INPUT_CLS}
            autoComplete="off"
            data-testid="ot-imprint-validation-placa-input"
          />
        </label>
        <button
          type="submit"
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: "#557EFF" }}
          data-testid="ot-imprint-validation-search-btn"
        >
          Consultar
        </button>
      </form>

      {phase === "idle" ? (
        <div data-testid="ot-imprint-validation-idle">
          <UiStateBoundary
            status="empty"
            emptyMessage="Ingrese una placa y pulse Consultar para ver las improntas firmadas."
          />
        </div>
      ) : (
        <UiStateBoundary
          status={boundaryStatus}
          onRetry={() => void load(appliedPlaca || placaInput)}
          skeletonRows={4}
          emptyMessage={
            appliedPlaca
              ? `No hay improntas firmadas para la placa ${appliedPlaca}.`
              : "No hay improntas firmadas para esta placa."
          }
          errorMessage={errorMessage ?? "No se pudieron consultar las improntas firmadas."}
        >
          <div data-testid="ot-imprint-validation-table">
            <DataTable
              columns={columns}
              rows={rows}
              getRowKey={(row) => row.id}
              ariaLabel="Improntas firmadas por placa"
              minWidth={1040}
            />
          </div>
        </UiStateBoundary>
      )}

      <Modal
        open={modalRow !== null}
        onClose={closeValidateModal}
        title="Validar firma digital"
        icon={ShieldCheck}
        description={
          modalRow
            ? `Placa ${modalRow.placa} · firmado ${formatDateTime(modalRow.signedAt)}. Pegue el texto de «Firma digital impronta» del PDF.`
            : undefined
        }
        size="md"
        busy={submitting}
      >
        <div className="flex flex-col gap-4 p-1" data-testid="ot-imprint-validation-modal">
          <label className="block">
            <span className="mb-1 block text-xs font-semibold text-[#162244] dark:text-white">
              Firma digital de la impronta
            </span>
            <textarea
              value={signatureInput}
              onChange={(e) => setSignatureInput(e.target.value)}
              placeholder="Pegue aquí la Base64 de la firma digital…"
              className={TEXTAREA_CLS}
              disabled={submitting}
              data-testid="ot-imprint-validation-signature-input"
              aria-label="Firma digital de la impronta"
            />
          </label>
          <div className="flex flex-wrap justify-end gap-2">
            <button
              type="button"
              className="rounded-xl border border-[#DFE5ED] px-4 py-2 text-xs font-semibold text-[#162244] dark:border-white/15 dark:text-white"
              onClick={closeValidateModal}
              disabled={submitting}
            >
              Cancelar
            </button>
            <button
              type="button"
              className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60"
              style={{ background: "#557EFF" }}
              onClick={() => void handleAcceptValidation()}
              disabled={submitting}
              data-testid="ot-imprint-validation-accept-btn"
            >
              {submitting ? "Validando…" : "Aceptar"}
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

function formatDateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString("es-CO", {
    dateStyle: "short",
    timeStyle: "short",
  });
}

function shortHash(hash: string): string {
  if (hash.length <= 12) return hash;
  return `${hash.slice(0, 8)}…${hash.slice(-4)}`;
}

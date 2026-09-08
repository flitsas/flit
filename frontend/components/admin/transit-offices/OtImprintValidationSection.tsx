"use client";

import { useCallback, useMemo, useState } from "react";
import { ShieldCheck } from "lucide-react";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  fetchListImprintSignatures,
  validateImprintSignature,
  type ImprintSignatureDto,
  type ImprintSignatureValidationResultKind,
} from "@/lib/api/admin-ot-imprint-signatures";
import { ApiError } from "@/lib/api/types";

type ViewPhase = "idle" | "loading" | "error" | "empty" | "ready";

const INPUT_CLS =
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162244] placeholder:text-[#59677D]/70 uppercase focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";

export function OtImprintValidationSection({ transitOfficeId: _transitOfficeId }: { transitOfficeId: string }) {
  const { show } = useToast();
  const [placaInput, setPlacaInput] = useState("");
  const [appliedPlaca, setAppliedPlaca] = useState("");
  const [phase, setPhase] = useState<ViewPhase>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [rows, setRows] = useState<ImprintSignatureDto[]>([]);
  const [validatingId, setValidatingId] = useState<string | null>(null);
  const [validationById, setValidationById] = useState<
    Record<string, { result: ImprintSignatureValidationResultKind; failureReason: string | null }>
  >({});

  const load = useCallback(async (placa: string) => {
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
      const data = await fetchListImprintSignatures(trimmed);
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
  }, []);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    void load(placaInput);
  };

  const handleValidate = useCallback(
    async (row: ImprintSignatureDto) => {
      setValidatingId(row.id);
      try {
        const result = await validateImprintSignature(row.id);
        setValidationById((prev) => ({
          ...prev,
          [row.id]: { result: result.result, failureReason: result.failureReason },
        }));
        if (result.result === "valid") {
          show("Firma válida.", "success");
        } else if (result.result === "invalid") {
          show(result.failureReason?.trim() || "Firma inválida.", "error");
        } else {
          show("Impronta no encontrada.", "error");
        }
      } catch (err) {
        show(
          err instanceof ApiError ? err.message : "No se pudo validar la firma.",
          "error",
        );
      } finally {
        setValidatingId(null);
      }
    },
    [show],
  );

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
        render: (row) => (
          <button
            type="button"
            className="inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60"
            style={{ background: "#557EFF" }}
            disabled={validatingId === row.id}
            aria-label={`Validar firma de impronta ${row.placa}`}
            onClick={() => void handleValidate(row)}
          >
            <ShieldCheck className="h-3.5 w-3.5" aria-hidden />
            {validatingId === row.id ? "Validando…" : "Validar firma"}
          </button>
        ),
      },
    ],
    [handleValidate, validatingId, validationById],
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
          Consulta las improntas firmadas por placa y verifica la firma digital RSA-SHA256 del hash
          registrado.
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
              minWidth={960}
            />
          </div>
        </UiStateBoundary>
      )}
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

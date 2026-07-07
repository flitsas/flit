"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  createMandateSigner,
  fetchMandateSigners,
  fetchOtCompanies,
  inactivateMandateSigner,
  updateMandateSigner,
  type MandateSigner,
  type MandateSignerInput,
  type OtCompany,
} from "@/lib/api/admin-mandate-signers";
import { MandatarioFormPanel } from "./MandatarioFormPanel";

/**
 * Pestaña "Mandatario" del hub Admin OT (ADR-0023): lista los mandatarios activos del OT,
 * permite registrar/editar (con multiselect de compañías y exclusividad) e inactivar (baja
 * lógica que libera compañías). 4 estados de UI + WCAG 2.1 AA.
 */
export function MandatariosSection({ transitOfficeId }: { transitOfficeId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [signers, setSigners] = useState<MandateSigner[]>([]);
  const [companies, setCompanies] = useState<OtCompany[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<MandateSigner | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [signerList, companyList] = await Promise.all([
          fetchMandateSigners(transitOfficeId, signal),
          fetchOtCompanies(transitOfficeId, signal),
        ]);
        if (signal?.aborted) {
          return;
        }
        setSigners(signerList);
        setCompanies(companyList);
        setStatus(signerList.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const companyNameById = useMemo(
    () => new Map(companies.map((c) => [c.companyTenantId, c.legalName])),
    [companies],
  );

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };

  const openEdit = (signer: MandateSigner) => {
    setEditing(signer);
    setFormOpen(true);
  };

  const handleSubmit = (input: MandateSignerInput) =>
    editing
      ? updateMandateSigner(transitOfficeId, editing.id, input)
      : createMandateSigner(transitOfficeId, input);

  const handleInactivate = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      await inactivateMandateSigner(transitOfficeId, signer.id);
      show(`Mandatario ${signer.fullName} inactivado. Sus compañías quedaron libres.`, "success");
      await load();
    } catch {
      show("No se pudo inactivar el mandatario.", "error");
    } finally {
      setBusyId(null);
    }
  };

  const emptyCta = (
    <button
      type="button"
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
      style={{ background: "#557EFF" }}
      onClick={openCreate}
    >
      Registrar primer mandatario
    </button>
  );

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <button
          type="button"
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: "#557EFF" }}
          onClick={openCreate}
        >
          Nuevo mandatario
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="Este organismo de tránsito no tiene mandatarios registrados."
        emptyCta={emptyCta}
        errorMessage="No se pudieron cargar los mandatarios."
        onRetry={() => void load()}
        skeletonRows={4}
      >
        <table className="w-full border-separate border-spacing-y-2 text-xs">
          <thead>
            <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
              <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                Mandatario
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                Documento
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                Compañías
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                Huella
              </th>
              <th className="rounded-r-xl px-4 py-2.5 text-right" style={{ background: "#DFE5ED" }}>
                Acciones
              </th>
            </tr>
          </thead>
          <tbody>
            {signers.map((signer) => (
              <tr key={signer.id} className="bg-white dark:bg-[#0B0F14]">
                <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold">
                  {signer.fullName}
                </td>
                <td className="border-y px-4 py-3 font-mono">{maskDocument(signer.documentNumber)}</td>
                <td className="border-y px-4 py-3">
                  {signer.companyTenantIds.length === 0
                    ? "—"
                    : signer.companyTenantIds
                        .map((id) => companyNameById.get(id) ?? id)
                        .join(", ")}
                </td>
                <td className="border-y px-4 py-3 font-mono opacity-70" title={signer.integrityHash}>
                  {signer.integrityHash.slice(0, 10)}…
                </td>
                <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                  <div className="flex justify-end gap-2">
                    <button
                      type="button"
                      className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold"
                      onClick={() => openEdit(signer)}
                    >
                      Editar
                    </button>
                    <button
                      type="button"
                      className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
                      style={{ color: "#FF4E00", borderColor: "#f0c38e" }}
                      disabled={busyId === signer.id}
                      aria-label={`Inactivar mandatario ${signer.fullName}`}
                      onClick={() => void handleInactivate(signer)}
                    >
                      Inactivar
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </UiStateBoundary>

      <MandatarioFormPanel
        open={formOpen}
        editing={editing}
        companies={companies}
        onClose={() => setFormOpen(false)}
        onSubmit={handleSubmit}
        onSaved={() => {
          setFormOpen(false);
          show(editing ? "Mandatario actualizado." : "Mandatario registrado.", "success");
          void load();
        }}
        onError={(message) => show(message, "error")}
      />
    </div>
  );
}

/** Enmascara el número de documento (PII, Ley 1581): solo se muestran los últimos 4 dígitos. */
function maskDocument(documentNumber: string): string {
  if (documentNumber.length <= 4) {
    return "••••";
  }
  return `••••${documentNumber.slice(-4)}`;
}

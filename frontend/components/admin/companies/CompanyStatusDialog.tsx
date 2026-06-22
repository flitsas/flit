"use client";

// Modal de confirmación para activar/desactivar una compañía (#10118). Al confirmar
// llama a PUT /companies/{tenantId}/status; en error muestra el mensaje y NO cierra.
import { useState } from "react";
import { setCompanyStatus } from "@/lib/api/admin-companies";
import type { CompanyListItem } from "@/lib/api/types";

export interface CompanyStatusDialogProps {
  company: CompanyListItem;
  onClose: () => void;
  onConfirmed: (updated: CompanyListItem) => void;
}

export function CompanyStatusDialog({ company, onClose, onConfirmed }: CompanyStatusDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const deactivating = company.estadoActivo;
  const nextActivo = !company.estadoActivo;

  async function confirm() {
    setError(null);
    setBusy(true);
    try {
      const updated = await setCompanyStatus(company.id, nextActivo);
      onConfirmed(updated);
    } catch (err) {
      const code = (err as { status?: number }).status;
      setError(code === 404 ? "La compañía no existe." : "No se pudo cambiar el estado de la compañía.");
      setBusy(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="company-status-title"
    >
      <div className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]">
        <h2 id="company-status-title" className="text-lg font-semibold" style={{ color: "#162744" }}>
          {deactivating ? "Desactivar compañía" : "Activar compañía"}
        </h2>

        <p className="mt-2 text-sm opacity-80">
          ¿Confirmas {deactivating ? "desactivar" : "activar"} la compañía{" "}
          <strong>{company.razonSocial}</strong>?
          {deactivating
            ? " No podrá operar en la plataforma hasta que la reactives."
            : " Quedará habilitada para operar en la plataforma."}
        </p>

        {error && (
          <p role="alert" className="mt-3 text-sm" style={{ color: "#FF4E00" }}>
            {error}
          </p>
        )}

        <div className="mt-5 flex gap-3">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
            style={{ borderColor: "#DFE5ED" }}
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={confirm}
            disabled={busy}
            className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
            style={{ background: deactivating ? "#FF4E00" : "#00DBD5" }}
          >
            {busy ? "Procesando…" : deactivating ? "Desactivar" : "Activar"}
          </button>
        </div>
      </div>
    </div>
  );
}

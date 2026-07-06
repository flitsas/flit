"use client";

// Modal de confirmación para activar/desactivar una compañía (#10118). Al confirmar
// llama a PUT /companies/{tenantId}/status; en error muestra el mensaje y NO cierra.
import { useState } from "react";
import { Modal } from "@/components/atom/Modal";
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
    <Modal
      open
      onClose={onClose}
      busy={busy}
      size="sm"
      zClassName="z-[90]"
      title={deactivating ? "Desactivar compañía" : "Activar compañía"}
      titleClassName="text-lg font-semibold text-[#162744] dark:text-white"
    >
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
    </Modal>
  );
}

"use client";

import { useState } from "react";
import { Modal } from "@/components/atom/Modal";
import { setChildCompanyStatus } from "@/lib/api/admin-companies";
import type { CompanyChildListItem } from "@/lib/api/types";

export interface ChildCompanyStatusDialogProps {
  headTenantId: string;
  child: CompanyChildListItem;
  onClose: () => void;
  onConfirmed: (updated: CompanyChildListItem) => void;
}

/** HU #12356 AC4 — confirmación explícita al activar/desactivar un hijo. */
export function ChildCompanyStatusDialog({
  headTenantId,
  child,
  onClose,
  onConfirmed,
}: ChildCompanyStatusDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const deactivating = child.estadoActivo;
  const nextActivo = !child.estadoActivo;

  async function confirm() {
    setError(null);
    setBusy(true);
    try {
      const updated = await setChildCompanyStatus(headTenantId, child.id, nextActivo);
      onConfirmed({
        ...child,
        estadoActivo: updated.estadoActivo,
        rowVersion: updated.rowVersion,
      });
    } catch {
      setError("No se pudo cambiar el estado del cliente.");
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
      title={deactivating ? "Desactivar cliente" : "Activar cliente"}
      titleClassName="text-lg font-semibold text-[#162744] dark:text-white"
    >
      <p className="mt-2 text-sm opacity-80">
        ¿Confirmas {deactivating ? "desactivar" : "activar"} a <strong>{child.razonSocial}</strong> en tu
        red?
        {deactivating
          ? " Dejará de operar hasta que lo reactives."
          : " Quedará habilitado para operar en la plataforma."}
      </p>

      {error && (
        <p role="alert" className="mt-3 text-sm" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}

      <div className="mt-5 flex gap-3">
        <button type="button" onClick={onClose} disabled={busy} className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60">
          Cancelar
        </button>
        <button
          type="button"
          onClick={() => void confirm()}
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

"use client";

import { useState } from "react";
import { Modal } from "@/components/atom/Modal";
import { unlinkCompanyFromParent } from "@/lib/api/admin-companies";
import type { CompanyChildListItem } from "@/lib/api/types";

export interface UnlinkCompanyDialogProps {
  child: CompanyChildListItem;
  headName: string;
  onClose: () => void;
  onUnlinked: (childId: string) => void;
}

/** HU #12357 AC3 — desvincular con advertencia explícita. */
export function UnlinkCompanyDialog({ child, headName, onClose, onUnlinked }: UnlinkCompanyDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function confirm() {
    setError(null);
    setBusy(true);
    try {
      await unlinkCompanyFromParent(child.id);
      onUnlinked(child.id);
    } catch {
      setError("No se pudo desvincular el cliente. Intenta de nuevo.");
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
      title="Desvincular cliente"
      titleClassName="text-lg font-semibold text-[#162744] dark:text-white"
    >
      <p className="mt-2 text-sm opacity-80">
        ¿Confirmas desvincular a <strong>{child.razonSocial}</strong> de la cabeza{" "}
        <strong>{headName}</strong>?
      </p>
      <p className="mt-2 text-sm font-medium" style={{ color: "#8a6000" }} role="note">
        La cabeza de red dejará de ver los datos de este cliente tras la desvinculación.
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
          style={{ background: "#FF4E00" }}
        >
          {busy ? "Procesando…" : "Desvincular"}
        </button>
      </div>
    </Modal>
  );
}

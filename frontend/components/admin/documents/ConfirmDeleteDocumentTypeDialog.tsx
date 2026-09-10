"use client";

import { useState } from "react";
import { Trash2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import type { DocumentType } from "@/lib/api/types-documents";

export function ConfirmDeleteDocumentTypeDialog({
  documentType,
  onClose,
  onConfirm,
}: {
  documentType: DocumentType;
  onClose: () => void;
  onConfirm: () => Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const confirm = async () => {
    setError(null);
    setBusy(true);
    try {
      await onConfirm();
    } catch {
      setError("No se pudo eliminar el documento.");
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      size="sm"
      icon={Trash2}
      iconBg="#FF4E00"
      title="Eliminar tipo de documento"
      titleClassName="text-base font-bold text-[#FF4E00]"
    >
      <p className="text-xs opacity-80">
        ¿Eliminar <strong>«{documentType.nombre}»</strong> del catálogo? Se quitará
        también de los trámites donde esté asociado. Esta acción no se puede deshacer.
      </p>
      {error && (
        <p role="alert" className="mt-3 text-xs font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
      <div className="mt-5 flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          disabled={busy}
          className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
        >
          Cancelar
        </button>
        <button
          type="button"
          onClick={() => void confirm()}
          disabled={busy}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ background: "#FF4E00" }}
        >
          {busy ? "Eliminando…" : "Eliminar"}
        </button>
      </div>
    </Modal>
  );
}

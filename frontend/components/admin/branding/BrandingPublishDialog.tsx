"use client";

import { Rocket } from "lucide-react";
import { Modal } from "@/components/atom/Modal";

export interface BrandingPublishDialogProps {
  open: boolean;
  busy: boolean;
  onClose: () => void;
  onConfirm: () => void;
}

/** HU #12414 AC5 — confirmación de publicación con el alcance explícito. */
export function BrandingPublishDialog({ open, busy, onClose, onConfirm }: BrandingPublishDialogProps) {
  return (
    <Modal open={open} onClose={onClose} busy={busy} size="sm" icon={Rocket} title="Publicar identidad de marca">
      <p className="text-sm opacity-80">
        Al publicar, estos cambios se aplican de inmediato a las sesiones nuevas y a los correos
        nuevos que se envíen desde esta red.
      </p>
      <p className="mt-2 text-sm font-medium" style={{ color: "#8a6000" }} role="note">
        Las comunicaciones ya enviadas no cambian.
      </p>

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
          onClick={onConfirm}
          disabled={busy}
          className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          {busy ? "Publicando…" : "Publicar"}
        </button>
      </div>
    </Modal>
  );
}

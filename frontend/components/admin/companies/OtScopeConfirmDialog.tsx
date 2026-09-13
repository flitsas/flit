"use client";

import { AlertTriangle } from "lucide-react";
import { Modal } from "@/components/atom/Modal";

export interface OtScopeConfirmDialogProps {
  open: boolean;
  action: "habilitar" | "deshabilitar" | "bloquear" | "desbloquear";
  officeName: string;
  affectedChildrenCount: number;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/** Confirmación de alcance de cambios de OT en red (HUs #12351 AC5, #12408 AC2). */
export function OtScopeConfirmDialog({
  open,
  action,
  officeName,
  affectedChildrenCount,
  busy = false,
  onConfirm,
  onCancel,
}: OtScopeConfirmDialogProps) {
  const childrenLabel =
    affectedChildrenCount === 1
      ? "1 cliente hijo vigente"
      : `${affectedChildrenCount} clientes hijos vigentes`;

  return (
    <Modal
      open={open}
      onClose={onCancel}
      busy={busy}
      icon={AlertTriangle}
      iconBg="#F9AC00"
      title="Confirmar cambio de organismos"
      titleClassName="text-base font-bold text-[#162744]"
      size="md"
    >
      <div className="space-y-3 text-xs">
        <p>
          Vas a <strong>{action}</strong> el organismo <strong>{officeName}</strong>.
        </p>
        <p
          className="rounded-xl border px-3 py-2 leading-relaxed"
          style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
          role="note"
        >
          Este cambio aplica a {childrenLabel} y a las radicaciones futuras de toda la red. Los
          trámites ya en curso conservan la configuración con la que se iniciaron.
        </p>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={busy}
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            Confirmar cambio
          </button>
        </div>
      </div>
    </Modal>
  );
}

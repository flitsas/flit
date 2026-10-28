"use client";

// HU #14068 AC3 — Confirmación de las acciones sobre la integración: rotar el secreto (con la opción de revocar el
// anterior al instante), bloquear, desbloquear y desactivar. Toda acción pide confirmación con un mensaje claro; si
// la API la rechaza (409), el motivo se muestra aquí mismo y el diálogo no se cierra.
import { useState } from "react";
import { Loader2, LockKeyhole, LockOpen, Power, RefreshCcw, type LucideIcon } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import type { ExternalIntegration } from "@/lib/api/external-integrations";
import { externalIntegrationErrorMessage } from "./externalIntegrationErrors";

export type ExternalIntegrationAction = "rotate" | "block" | "unblock" | "deactivate";

interface Copy {
  title: string;
  icon: LucideIcon;
  body: string;
  warning?: string;
  confirm: string;
  danger: boolean;
}

const PASES_VIGENTES = "Los pases que ya obtuvo siguen valiendo hasta que vencen (máximo 30 minutos).";

function copyFor(action: ExternalIntegrationAction, integration: ExternalIntegration): Copy {
  const name = `«${integration.displayName || integration.clientId}»`;
  switch (action) {
    case "rotate":
      return {
        title: "Rotar secreto",
        icon: RefreshCcw,
        body: `Se genera un secreto nuevo para ${name} y se muestra una sola vez. Entréguelo al servicio del portal por un canal seguro.`,
        confirm: "Rotar secreto",
        danger: false,
      };
    case "block":
      return {
        title: "Bloquear integración",
        icon: LockKeyhole,
        body: `${name} no podrá obtener pases nuevos hasta que usted la desbloquee.`,
        warning: PASES_VIGENTES,
        confirm: "Bloquear",
        danger: true,
      };
    case "unblock":
      return {
        title: "Desbloquear integración",
        icon: LockOpen,
        body: `${name} podrá volver a pedir pases de inmediato con su secreto vigente.`,
        confirm: "Desbloquear",
        danger: false,
      };
    case "deactivate":
    default:
      return {
        title: "Desactivar integración",
        icon: Power,
        body: `${name} se desactiva de forma definitiva: no podrá obtener pases y no se puede volver a activar. Para reconectar el portal tendrá que crear una integración nueva.`,
        warning: PASES_VIGENTES,
        confirm: "Desactivar",
        danger: true,
      };
  }
}

export interface ExternalIntegrationActionDialogProps {
  action: ExternalIntegrationAction;
  integration: ExternalIntegration;
  onClose: () => void;
  /** Ejecuta la acción. Para `rotate` recibe si se revoca el secreto anterior. */
  onConfirm: (options: { revocarAnterior: boolean }) => Promise<void>;
}

export function ExternalIntegrationActionDialog({
  action,
  integration,
  onClose,
  onConfirm,
}: ExternalIntegrationActionDialogProps) {
  const copy = copyFor(action, integration);
  const [revocarAnterior, setRevocarAnterior] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleConfirm() {
    setBusy(true);
    setError(null);
    try {
      await onConfirm({ revocarAnterior });
    } catch (err) {
      setError(externalIntegrationErrorMessage(err));
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={copy.title}
      icon={copy.icon}
      iconBg={copy.danger ? "#FF4E00" : "var(--color-flit-brand)"}
      size="sm"
      footer={
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-full border border-[#DFE5ED] px-5 py-2.5 text-sm font-semibold text-flit-primary hover:bg-[#F4F8FF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-70 dark:border-white/15 dark:text-white dark:hover:bg-white/5"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void handleConfirm()}
            disabled={busy}
            className="inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-70"
            style={{
              background: copy.danger
                ? "linear-gradient(135deg, #FF4E00 0%, #E43D30 100%)"
                : "linear-gradient(135deg, var(--color-flit-brand) 0%, var(--color-flit-tech) 100%)",
            }}
          >
            {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
            {copy.confirm}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm text-flit-primary dark:text-white">
        <p>{copy.body}</p>
        {action === "rotate" && (
          <label className="flex cursor-pointer items-start gap-3 rounded-[10px] border border-[#DFE5ED] p-3 focus-within:ring-2 focus-within:ring-flit-brand focus-within:ring-offset-2 dark:border-white/15">
            <input
              type="checkbox"
              checked={revocarAnterior}
              onChange={(e) => setRevocarAnterior(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-[#FF4E00] focus-visible:outline-none"
            />
            <span>
              <span className="font-semibold">Revocar el secreto anterior de inmediato</span>
              <span className="block text-xs text-[#59677D] dark:text-white/70">
                Úselo si el secreto se filtró. Si no lo marca, el anterior sigue valiendo 24 horas para que el portal
                alcance a cambiarlo.
              </span>
            </span>
          </label>
        )}
        {copy.warning && (
          <p
            className="rounded-[10px] border px-3 py-2 text-xs font-semibold"
            style={{
              background: "var(--badge-warning-bg)",
              color: "var(--badge-warning-fg)",
              borderColor: "var(--badge-warning-border)",
            }}
          >
            {copy.warning}
          </p>
        )}
        {error && (
          <p role="alert" className="text-sm font-semibold text-[#C2410C]">
            {error}
          </p>
        )}
      </div>
    </Modal>
  );
}

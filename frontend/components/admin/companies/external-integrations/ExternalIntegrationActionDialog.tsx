"use client";

// HU #14068 AC3 — Confirmación de las acciones sobre la integración: rotar el secreto (con la opción de revocar el
// anterior al instante), bloquear, desbloquear y desactivar. Toda acción pide confirmación con un mensaje claro; si
// la API la rechaza (409), el motivo se muestra aquí mismo y el diálogo no se cierra.
import { useState } from "react";
import { KeyRound, LockKeyhole, LockOpen, Power, type LucideIcon } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { Modal } from "@/components/atom/Modal";
import { PnButton } from "@/components/admin/plataforma/persona-natural/pn-componentes";
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

const PASES_VIGENTES = "Los accesos que ya tiene siguen valiendo hasta que vencen (máximo 30 minutos).";

function copyFor(action: ExternalIntegrationAction, integration: ExternalIntegration): Copy {
  const name = `«${integration.displayName || integration.clientId}»`;
  switch (action) {
    case "rotate":
      return {
        title: "Generar una clave nueva",
        icon: KeyRound,
        body: `Se genera una clave secreta nueva para ${name} y se muestra una sola vez. Entrégala al servicio del portal por un canal seguro.`,
        confirm: "Generar clave nueva",
        danger: false,
      };
    case "block":
      return {
        title: "Bloquear conexión",
        icon: LockKeyhole,
        body: `${name} no podrá obtener accesos nuevos hasta que la desbloquees.`,
        warning: PASES_VIGENTES,
        confirm: "Bloquear",
        danger: true,
      };
    case "unblock":
      return {
        title: "Desbloquear conexión",
        icon: LockOpen,
        body: `${name} podrá volver a pedir accesos de inmediato con su clave vigente.`,
        confirm: "Desbloquear",
        danger: false,
      };
    case "deactivate":
    default:
      return {
        title: "Desactivar conexión",
        icon: Power,
        body: `${name} se desactiva de forma definitiva: no podrá obtener accesos y no se puede volver a activar. Para reconectar el portal tendrás que crear una conexión nueva.`,
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
          <PnButton variant="secondary" onClick={onClose} disabled={busy}>
            Cancelar
          </PnButton>
          <PnButton
            variant={copy.danger ? "danger" : "primary"}
            icon={copy.icon}
            loading={busy}
            onClick={() => void handleConfirm()}
          >
            {copy.confirm}
          </PnButton>
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
              <span className="font-semibold">Dejar sin efecto la clave anterior ahora mismo</span>
              <span className="block text-xs text-[#59677D] dark:text-white/70">
                Márcalo si la clave se filtró. Si no lo marcas, la anterior sigue valiendo 24 horas para que el portal alcance a cambiarla.
              </span>
            </span>
          </label>
        )}
        {copy.warning && <InlineAlert tone="warning">{copy.warning}</InlineAlert>}
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}

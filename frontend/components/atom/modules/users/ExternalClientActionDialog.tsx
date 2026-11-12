"use client";

// HU #13200 — Confirmación de las acciones sobre un cliente de integración externo (AC4, AC5): regenerar el
// secreto (con la opción de revocar el anterior al instante), desbloquear, activar o desactivar y obligar a
// rotar. Toda acción pide confirmación. Mientras la API no invalide los pases ya emitidos, las acciones que
// cortan el acceso lo advierten: un pase emitido sigue valiendo hasta que vence (30 min).
import { useState } from "react";
import { Icono, type NombreIcono } from "@flit/ui/Icono";
import { Modal } from "@/components/atom/Modal";
import type { ExternalClient } from "@/lib/api/external-clients";
import { externalClientErrorMessage } from "./externalClientErrors";

export type ExternalClientAction = "regenerate" | "unlock" | "toggle-active" | "must-rotate";

const PASES_VIGENTES =
  "Los pases que el cliente ya obtuvo siguen valiendo hasta que vencen (máximo 30 minutos).";

interface Copy {
  title: string;
  icon: NombreIcono;
  body: string;
  warning?: string;
  confirm: string;
  danger: boolean;
}

function copyFor(action: ExternalClientAction, client: ExternalClient): Copy {
  switch (action) {
    case "regenerate":
      return {
        title: "Regenerar secreto",
        icon: "reintento",
        body: `Se genera un secreto nuevo para «${client.clientId}» y se muestra una sola vez. Entrégueselo al sistema externo por un canal seguro.`,
        warning: PASES_VIGENTES,
        confirm: "Regenerar secreto",
        danger: false,
      };
    case "unlock":
      return {
        title: "Desbloquear cliente",
        icon: "candado-abierto",
        body: `«${client.clientId}» quedó bloqueado por intentos fallidos. Al desbloquearlo podrá volver a pedir pases de inmediato.`,
        confirm: "Desbloquear",
        danger: false,
      };
    case "must-rotate":
      return {
        title: "Obligar a rotar el secreto",
        icon: "rol",
        body: `«${client.clientId}» no podrá obtener pases nuevos hasta que se le regenere el secreto.`,
        warning: PASES_VIGENTES,
        confirm: "Obligar a rotar",
        danger: true,
      };
    case "toggle-active":
    default:
      return client.isActive
        ? {
            title: "Desactivar cliente",
            icon: "encendido",
            body: `«${client.clientId}» no podrá obtener pases nuevos mientras esté inactivo.`,
            warning: PASES_VIGENTES,
            confirm: "Desactivar",
            danger: true,
          }
        : {
            title: "Activar cliente",
            icon: "encendido",
            body: `«${client.clientId}» podrá volver a obtener pases con su secreto vigente.`,
            confirm: "Activar",
            danger: false,
          };
  }
}

export interface ExternalClientActionDialogProps {
  action: ExternalClientAction;
  client: ExternalClient;
  onClose: () => void;
  /** Ejecuta la acción. Para `regenerate` recibe si se revoca el secreto anterior. */
  onConfirm: (options: { revocarAnterior: boolean }) => Promise<void>;
}

export function ExternalClientActionDialog({ action, client, onClose, onConfirm }: ExternalClientActionDialogProps) {
  const copy = copyFor(action, client);
  const [revocarAnterior, setRevocarAnterior] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleConfirm() {
    setBusy(true);
    setError(null);
    try {
      await onConfirm({ revocarAnterior });
    } catch (err) {
      setError(externalClientErrorMessage(err));
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={copy.title}
      icono={copy.icon}
      iconBg={copy.danger ? "var(--destructive)" : "var(--color-flit-brand)"}
      size="sm"
      footer={
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-full border border-border px-5 py-2.5 text-sm font-semibold text-flit-primary hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-70"
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
                ? "var(--destructive)"
                : "var(--gradient-action)",
              color: "var(--destructive-foreground)",
            }}
          >
            {busy && <Icono nombre="girador" className="h-4 w-4 animate-spin" />}
            {copy.confirm}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm text-flit-primary">
        <p>{copy.body}</p>
        {action === "regenerate" && (
          <label className="flex cursor-pointer items-start gap-3 rounded-[10px] border border-border p-3 focus-within:ring-2 focus-within:ring-flit-brand focus-within:ring-offset-2">
            <input
              type="checkbox"
              checked={revocarAnterior}
              onChange={(e) => setRevocarAnterior(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-destructive focus-visible:outline-none"
            />
            <span>
              <span className="font-semibold">Revocar el secreto anterior de inmediato</span>
              <span className="block text-xs text-muted-foreground">
                Úselo si el secreto se filtró. Si no lo marca, el anterior sigue valiendo 24 horas para que el sistema
                externo alcance a cambiarlo.
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
          <p role="alert" className="text-sm font-semibold text-destructive">
            {error}
          </p>
        )}
      </div>
    </Modal>
  );
}

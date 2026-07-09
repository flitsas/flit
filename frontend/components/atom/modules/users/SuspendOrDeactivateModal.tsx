"use client";

import { useState } from "react";
import { Ban } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ApiError } from "@/lib/api/types";

/** Modo con el que se abre el modal (preseteado por el botón de fila que lo abrió). */
export type SuspendMode = "temporary" | "indefinite";

export interface SuspendOrDeactivateUser {
  id: string;
  fullName: string;
}

export interface SuspendOrDeactivateModalProps {
  user: SuspendOrDeactivateUser;
  /** Modo inicial; el admin puede cambiarlo dentro del modal vía el toggle "Sin fecha de fin". */
  mode: SuspendMode;
  onClose: () => void;
  /** `endsAt` viaja `null` cuando el modo activo es "indefinite" (desactivación indefinida). */
  onConfirm: (reason: string, endsAt: string | null) => Promise<void>;
}

/**
 * Modal unificado de suspensión/desactivación de usuarios (HU #10620, Feature #10618).
 *
 * Reemplaza `SuspendModal` (Usuarios.tsx) y `OtSuspendUserDialog` (OtUsersSection.tsx):
 * antes eran dos formularios casi idénticos que solo permitían suspender temporalmente
 * (`endsAt` siempre obligatorio). El backend (HU #10619) ya acepta `endsAt` nulo para
 * desactivar indefinidamente, así que este componente resuelve ambos casos con un único
 * toggle "Sin fecha de fin (desactivar indefinidamente)":
 * - Marcado → AC1: oculta el campo de fecha, no lo exige, `endsAt` se envía `null`.
 * - Desmarcado → AC2: exige la fecha de fin (mismo `min`=ahora y default de 30 días que
 *   el comportamiento previo).
 *
 * El título, el texto del botón de submit y el color de acento cambian según el modo. El
 * modo inicial lo decide el botón de fila que abrió el modal (`mode`), pero el admin puede
 * cambiar de opinión sin cerrarlo.
 *
 * AC3 (negativo): si el backend rechaza la acción por ser el último administrador activo
 * del tenant (`LAST_ACTIVE_ADMIN`, 409 — `SecurityEndpoints` usa el campo `code`,
 * `AdminOtEndpoints` usa `error`; se comprueban ambos por paridad AC4), se muestra un
 * mensaje claro en el propio modal en vez del genérico.
 */
export function SuspendOrDeactivateModal({
  user,
  mode,
  onClose,
  onConfirm,
}: SuspendOrDeactivateModalProps) {
  const [indefinite, setIndefinite] = useState(mode === "indefinite");
  const [reason, setReason] = useState("");
  const [endsAt, setEndsAt] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // eslint-disable-next-line react-hooks/purity
  const defaultEndsAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000)
    .toISOString()
    .slice(0, 16);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!reason.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const endsAtValue = indefinite ? null : new Date(endsAt || defaultEndsAt).toISOString();
      await onConfirm(reason.trim(), endsAtValue);
    } catch (err) {
      setError(mapSuspendError(err));
      setBusy(false);
    }
  }

  const accentColor = indefinite ? "#557EFF" : "#FF4E00";
  const title = indefinite ? "Desactivar usuario" : "Suspender usuario";
  const submitLabel = busy ? "Aplicando…" : indefinite ? "Desactivar usuario" : "Suspender usuario";

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      size="sm"
      title={title}
      icon={Ban}
      iconBg={accentColor}
      description={
        <>
          <strong>{user.fullName}</strong>{" "}
          {indefinite
            ? "quedará desactivado indefinidamente y no podrá iniciar sesión hasta que lo reactives."
            : "no podrá iniciar sesión durante el periodo indicado."}
        </>
      }
    >
      <form onSubmit={handleSubmit} className="space-y-3">
        <div>
          <label htmlFor="suspend-reason" className="text-xs font-semibold block mb-1">
            Motivo *
          </label>
          <textarea
            id="suspend-reason"
            required
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Ej. Incumplimiento de políticas de uso"
            rows={3}
            className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:ring-2 focus:ring-blue-400 resize-none"
          />
        </div>

        <label className="flex items-center gap-2 text-xs cursor-pointer">
          <input
            type="checkbox"
            checked={indefinite}
            onChange={(e) => setIndefinite(e.target.checked)}
            className="h-3.5 w-3.5 accent-[#557EFF]"
          />
          Sin fecha de fin (desactivar indefinidamente)
        </label>

        {!indefinite && (
          <div>
            <label htmlFor="suspend-ends-at" className="text-xs font-semibold block mb-1">
              Suspendido hasta *
            </label>
            <input
              id="suspend-ends-at"
              type="datetime-local"
              required
              value={endsAt || defaultEndsAt}
              onChange={(e) => setEndsAt(e.target.value)}
              min={new Date().toISOString().slice(0, 16)}
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:ring-2 focus:ring-blue-400"
            />
          </div>
        )}

        {error && (
          <p
            role="alert"
            className="text-xs py-2 px-3 rounded-xl font-medium"
            style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}
          >
            {error}
          </p>
        )}

        <div className="flex gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="flex-1 py-2.5 rounded-xl text-sm font-semibold border transition disabled:opacity-60"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={busy}
            className="flex-1 py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-60 transition"
            style={{ background: accentColor }}
          >
            {submitLabel}
          </button>
        </div>
      </form>
    </Modal>
  );
}

/** AC3 — mapea el 409 de "último administrador activo" a un mensaje explicativo claro. */
function mapSuspendError(err: unknown): string {
  if (err instanceof ApiError && err.status === 409) {
    const body = err.body as { code?: string; error?: string } | undefined;
    const businessCode = body?.code ?? body?.error;
    if (businessCode === "LAST_ACTIVE_ADMIN") {
      return "No puedes dejar este tenant sin administradores activos: este usuario es el último administrador activo y debe conservar su acceso.";
    }
  }
  return "No se pudo aplicar la acción. Inténtalo de nuevo.";
}

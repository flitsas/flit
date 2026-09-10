"use client";

import { useState } from "react";
import { Loader2, RotateCw } from "lucide-react";
import { useCooldown } from "@/hooks/useCooldown";
import { ApiError } from "@/lib/api/types";
import { EMAIL_ALREADY_ASSOCIATED_MESSAGE, emailConflictErrorCode, isEmailConflictCode } from "@/lib/users/emailConflict";
// HU19 — misma area clickeable minima que la columna de acciones unificada (RowActions).
import { ICON_BUTTON_HIT_AREA } from "@/components/atom/RowActions";

// HU #11552 / ADR-0048 — Botón "Reactivar invitación" del menú de acciones de la tabla (módulo
// Compañía "Usuarios y Permisos" y pestaña "Usuarios" del hub OT). Espejo de
// ResendInvitationButton: misma semántica de cooldown optimista + reemplazo por el
// `retryAfterSeconds` real en 429, y mismo mensaje inline con `role="alert"`/`aria-live`. El
// padre solo lo renderiza cuando `status === "cancelled"` (el `id` de la fila YA es el
// `invitationId` en ese caso). AC3: si el correo ya se ocupó (409 EMAIL_ALREADY_IN_USE,
// HU #11580), muestra el literal unificado de `emailConflict.ts` — el estado de la
// invitación NO cambia.
const OPTIMISTIC_COOLDOWN_SECONDS = 120;

export interface ReactivateInvitationOutcome {
  email: string;
  emailSent: boolean;
}

export interface ReactivateInvitationButtonProps {
  /** El `id` de la fila de una invitación "cancelled" — ya es el invitationId, sin campo nuevo. */
  invitationId: string;
  fullName: string;
  reactivate: (invitationId: string) => Promise<ReactivateInvitationOutcome>;
  /** Gancho opcional para que el padre reaccione a una reactivación exitosa (refresco de la
   *  fila a "Pendiente" sin recargar la página, y/o un toast). */
  onReactivated?: (outcome: ReactivateInvitationOutcome) => void;
}

function formatWait(seconds: number): string {
  if (seconds >= 60) {
    const minutes = Math.ceil(seconds / 60);
    return `${minutes} minuto${minutes !== 1 ? "s" : ""}`;
  }
  return `${seconds} segundo${seconds !== 1 ? "s" : ""}`;
}

export function ReactivateInvitationButton({
  invitationId,
  fullName,
  reactivate,
  onReactivated,
}: ReactivateInvitationButtonProps) {
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ type: "success" | "error"; text: string } | null>(null);
  const cooldown = useCooldown();

  const disabled = busy || cooldown.active;

  async function handleClick() {
    setBusy(true);
    setMessage(null);
    try {
      const outcome = await reactivate(invitationId);
      // El 200 no informa retryAfterSeconds (solo el 429) — se asume el cooldown anti-abuso
      // configurado por defecto, compartido con `resend` (InvitationOptions.ResendCooldown).
      cooldown.start(OPTIMISTIC_COOLDOWN_SECONDS);
      setMessage({
        type: "success",
        text: outcome.emailSent
          ? `Invitación reactivada y reenviada a ${outcome.email}.`
          : `Invitación reactivada, pero el correo no pudo entregarse a ${outcome.email}. Puedes reintentar más tarde.`,
      });
      onReactivated?.(outcome);
    } catch (err) {
      if (err instanceof ApiError && err.status === 429) {
        const body = err.body as { message?: string; retryAfterSeconds?: number } | undefined;
        const retryAfterSeconds =
          typeof body?.retryAfterSeconds === "number" && body.retryAfterSeconds > 0
            ? body.retryAfterSeconds
            : OPTIMISTIC_COOLDOWN_SECONDS;
        cooldown.start(retryAfterSeconds);
        setMessage({
          type: "error",
          text: body?.message ?? `Debes esperar ${formatWait(retryAfterSeconds)} antes de reactivar de nuevo.`,
        });
      } else if (err instanceof ApiError && err.status === 409) {
        // AC3 — el correo ya se ocupó (invitación pendiente, cuenta activa o eliminada) o la
        // invitación ya no está cancelada: en cualquier caso, el estado NO cambia.
        const code = emailConflictErrorCode(err.body);
        if (isEmailConflictCode(code)) {
          setMessage({ type: "error", text: EMAIL_ALREADY_ASSOCIATED_MESSAGE });
        } else if (code === "INVITATION_NOT_CANCELLED") {
          setMessage({
            type: "error",
            text: "La invitación ya no está cancelada (fue reactivada o creada de nuevo).",
          });
        } else {
          setMessage({ type: "error", text: "No se pudo reactivar la invitación por un conflicto." });
        }
      } else {
        setMessage({ type: "error", text: "No se pudo reactivar la invitación. Inténtalo de nuevo." });
      }
    } finally {
      setBusy(false);
    }
  }

  const ariaLabel = cooldown.active
    ? `Reactivar invitación a ${fullName} (disponible en ${formatWait(cooldown.remainingSeconds)})`
    : `Reactivar invitación a ${fullName}`;

  return (
    <div className="flex flex-col items-end gap-0.5">
      <button
        type="button"
        title="Reactivar invitación"
        aria-label={ariaLabel}
        onClick={handleClick}
        disabled={disabled}
        className={`${ICON_BUTTON_HIT_AREA} p-1.5 rounded-lg transition hover:bg-blue-50 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent`}
        style={{ color: "#557EFF" }}
      >
        {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <RotateCw className="h-4 w-4" />}
      </button>
      {message && (
        <p
          role={message.type === "error" ? "alert" : "status"}
          aria-live={message.type === "error" ? "assertive" : "polite"}
          className="text-[10px] font-medium max-w-[180px] text-right"
          style={{ color: message.type === "error" ? "#FF4E00" : "#00948F" }}
        >
          {message.text}
        </p>
      )}
    </div>
  );
}

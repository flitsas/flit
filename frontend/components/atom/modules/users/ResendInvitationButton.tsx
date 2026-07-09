"use client";

import { useState } from "react";
import { Loader2, Send } from "lucide-react";
import { useCooldown } from "@/hooks/useCooldown";
import { ApiError } from "@/lib/api/types";

// HU #10626 — Botón "Reenviar invitación" del menú de acciones de la tabla (módulo Compañía
// "Usuarios y Permisos" y pestaña "Usuarios" del hub OT). AC3: el padre solo lo renderiza cuando
// `status === "pending"` (el `id` de la fila YA es el `invitationId` en ese caso — ver
// TenantUser/OtUserItem). AC1: reenvío exitoso deja un cooldown visual (useCooldown) para evitar
// reenvíos inmediatos repetidos. AC2: si el backend rechaza con 429 (cooldown anti-abuso real,
// ~2 min, aún activo), se reemplaza el cooldown "optimista" del cliente por el `retryAfterSeconds`
// real informado por el backend — nunca se reinicia desde cero con un valor fijo del cliente.
//
// Self-contained: no depende de ToastProvider (Usuarios.tsx, a diferencia de OtUsersSection.tsx,
// no envuelve el módulo en uno) — la confirmación/error vive en un mensaje inline con
// aria-live junto al botón. `onResent` es un gancho opcional para que el padre añada además un
// toast (p. ej. OtUsersSection, que ya sigue ese patrón para sus otras acciones).
const OPTIMISTIC_COOLDOWN_SECONDS = 120;

export interface ResendInvitationOutcome {
  email: string;
  emailSent: boolean;
}

export interface ResendInvitationButtonProps {
  /** El `id` de la fila de un usuario "pending" — ya es el invitationId, sin campo nuevo. */
  invitationId: string;
  fullName: string;
  resend: (invitationId: string) => Promise<ResendInvitationOutcome>;
  /** Gancho opcional para que el padre reaccione a un reenvío exitoso (p. ej. mostrar un toast). */
  onResent?: (outcome: ResendInvitationOutcome) => void;
}

function formatWait(seconds: number): string {
  if (seconds >= 60) {
    const minutes = Math.ceil(seconds / 60);
    return `${minutes} minuto${minutes !== 1 ? "s" : ""}`;
  }
  return `${seconds} segundo${seconds !== 1 ? "s" : ""}`;
}

export function ResendInvitationButton({
  invitationId,
  fullName,
  resend,
  onResent,
}: ResendInvitationButtonProps) {
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ type: "success" | "error"; text: string } | null>(null);
  const cooldown = useCooldown();

  const disabled = busy || cooldown.active;

  async function handleClick() {
    setBusy(true);
    setMessage(null);
    try {
      const outcome = await resend(invitationId);
      // AC1 — cooldown "optimista": el 200 no informa retryAfterSeconds (solo el 429), se asume
      // el cooldown anti-abuso configurado por defecto (InvitationOptions.ResendCooldown, ~2 min).
      cooldown.start(OPTIMISTIC_COOLDOWN_SECONDS);
      setMessage({
        type: "success",
        text: outcome.emailSent
          ? `Invitación reenviada a ${outcome.email}.`
          : `Invitación reenviada, pero el correo no pudo entregarse a ${outcome.email}. Puedes reintentar más tarde.`,
      });
      onResent?.(outcome);
    } catch (err) {
      if (err instanceof ApiError && err.status === 429) {
        // AC2 — 429: el cooldown anti-abuso real del backend sigue activo. Se lee
        // `retryAfterSeconds` del cuerpo (mismo campo en Compañía y OT) para reflejar el tiempo
        // de espera REAL, reemplazando el cooldown optimista en vez de sumarle nada.
        const body = err.body as { message?: string; retryAfterSeconds?: number } | undefined;
        const retryAfterSeconds =
          typeof body?.retryAfterSeconds === "number" && body.retryAfterSeconds > 0
            ? body.retryAfterSeconds
            : OPTIMISTIC_COOLDOWN_SECONDS;
        cooldown.start(retryAfterSeconds);
        setMessage({
          type: "error",
          text: body?.message ?? `Debes esperar ${formatWait(retryAfterSeconds)} antes de reenviar de nuevo.`,
        });
      } else if (err instanceof ApiError && err.status === 409) {
        setMessage({
          type: "error",
          text: "La invitación ya no está pendiente (fue aceptada o cancelada).",
        });
      } else {
        setMessage({ type: "error", text: "No se pudo reenviar la invitación. Inténtalo de nuevo." });
      }
    } finally {
      setBusy(false);
    }
  }

  const ariaLabel = cooldown.active
    ? `Reenviar invitación a ${fullName} (disponible en ${formatWait(cooldown.remainingSeconds)})`
    : `Reenviar invitación a ${fullName}`;

  return (
    <div className="flex flex-col items-end gap-0.5">
      <button
        type="button"
        title="Reenviar invitación"
        aria-label={ariaLabel}
        onClick={handleClick}
        disabled={disabled}
        className="p-1.5 rounded-lg transition hover:bg-blue-50 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent"
        style={{ color: "#557EFF" }}
      >
        {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
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

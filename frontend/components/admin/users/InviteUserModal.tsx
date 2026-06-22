"use client";

import { useState } from "react";
import { createInvitation } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";

interface InviteUserModalProps {
  onClose: () => void;
  onSuccess: (email: string) => void;
}

const INPUT_CLASS =
  "w-full bg-white border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20";

export function InviteUserModal({ onClose, onSuccess }: InviteUserModalProps) {
  const [email, setEmail] = useState("");
  const [roleId, setRoleId] = useState("");
  const [status, setStatus] = useState<"idle" | "loading" | "done" | "done_no_email">("idle");
  const [error, setError] = useState<string | null>(null);
  const [invitedEmail, setInvitedEmail] = useState("");

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setStatus("loading");
    try {
      const result = await createInvitation(email.trim(), roleId.trim());
      setInvitedEmail(result.email);
      setStatus(result.emailSent ? "done" : "done_no_email");
      onSuccess(result.email);
    } catch (err) {
      const status = (err as ApiError).status;
      setError(
        status === 409
          ? "Ya existe una invitación pendiente para este correo."
          : status === 404
            ? "El rol especificado no existe en el tenant."
            : "No se pudo enviar la invitación. Inténtalo de nuevo.",
      );
      setStatus("idle");
    }
  }

  const isDone = status === "done" || status === "done_no_email";

  return (
    <div
      className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 backdrop-blur-sm px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="invite-modal-title"
    >
      <div className="bg-white rounded-2xl w-full max-w-md p-6 shadow-2xl">
        <h2 id="invite-modal-title" className="text-lg font-semibold text-[#162744]">
          Invitar usuario
        </h2>

        {isDone ? (
          <>
            <p role="status" className="mt-3 text-sm text-slate-700">
              Invitación enviada a <strong>{invitedEmail}</strong>.
            </p>
            {status === "done_no_email" && (
              <p role="alert" className="mt-2 text-sm text-amber-600">
                El correo de activación no pudo enviarse. El admin puede reintentar más tarde.
              </p>
            )}
            <button
              type="button"
              onClick={onClose}
              className="mt-5 w-full rounded-xl py-2.5 text-sm font-semibold text-white"
              style={{ background: "#557eff" }}
            >
              Cerrar
            </button>
          </>
        ) : (
          <form onSubmit={handleSubmit} className="mt-4 space-y-4" noValidate>
            <div>
              <label htmlFor="invite-email" className="block text-sm font-medium text-[#162744] mb-1">
                Correo electrónico
              </label>
              <input
                id="invite-email"
                type="email"
                autoComplete="off"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className={INPUT_CLASS}
                placeholder="usuario@empresa.com"
              />
            </div>
            <div>
              <label htmlFor="invite-role" className="block text-sm font-medium text-[#162744] mb-1">
                ID de rol
              </label>
              <input
                id="invite-role"
                type="text"
                required
                value={roleId}
                onChange={(e) => setRoleId(e.target.value)}
                className={INPUT_CLASS}
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              />
            </div>
            {error && (
              <p role="alert" className="text-sm text-[#ff4e00]">
                {error}
              </p>
            )}
            <div className="flex gap-3 pt-1">
              <button
                type="button"
                onClick={onClose}
                className="flex-1 rounded-xl py-2.5 text-sm font-medium border border-slate-200"
              >
                Cancelar
              </button>
              <button
                type="submit"
                disabled={status === "loading"}
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557eff" }}
              >
                {status === "loading" ? "Enviando…" : "Enviar invitación"}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}

"use client";

// Modal admin de bloqueo temporal (HU #10174, AC2). Al confirmar llama a
// POST /auth/admin/block-user; si la API responde 403 (fuera de ámbito) muestra el
// error y NO aplica cambios.
import { useState } from "react";
import { adminBlockUser } from "@/lib/api/auth";

export function BlockUserModal({ email, onClose }: { email: string; onClose: () => void }) {
  const [days, setDays] = useState(7);
  const [status, setStatus] = useState<"idle" | "loading" | "done">("idle");
  const [error, setError] = useState<string | null>(null);

  async function confirm() {
    setError(null);
    setStatus("loading");
    try {
      await adminBlockUser(email, days);
      setStatus("done");
    } catch (err) {
      const code = (err as { status?: number }).status;
      setError(
        code === 403
          ? "Acceso restringido: se requiere ámbito sobre el usuario (otro tenant)."
          : code === 404
            ? "El usuario no existe."
            : "No se pudo aplicar el bloqueo.",
      );
      setStatus("idle");
    }
  }

  return (
    <div
      className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 backdrop-blur-sm px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="block-modal-title"
    >
      <div className="bg-white rounded-2xl w-full max-w-md p-6 shadow-2xl">
        <h2 id="block-modal-title" className="text-lg font-semibold text-[#162744]">
          Bloqueo temporal
        </h2>

        {status === "done" ? (
          <>
            <p role="status" className="mt-2 text-sm text-slate-700">
              <strong>{email}</strong> quedó bloqueado temporalmente por {days} día(s).
            </p>
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
          <>
            <p className="mt-2 text-sm text-slate-600">
              Define la duración del bloqueo temporal para <strong>{email}</strong>.
            </p>
            <label htmlFor="block-days" className="block mt-4 text-sm font-medium text-[#162744]">
              Días de bloqueo
            </label>
            <input
              id="block-days"
              type="number"
              min={1}
              max={90}
              value={days}
              onChange={(e) => setDays(Number(e.target.value))}
              className="mt-1 w-full bg-white border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20"
            />
            {error && (
              <p role="alert" className="mt-3 text-sm text-[#ff4e00]">
                {error}
              </p>
            )}
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                onClick={onClose}
                className="flex-1 rounded-xl py-2.5 text-sm font-medium border border-slate-200"
              >
                Cancelar
              </button>
              <button
                type="button"
                onClick={confirm}
                disabled={status === "loading"}
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#ff4e00" }}
              >
                {status === "loading" ? "Procesando…" : "Aplicar bloqueo"}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

"use client";

// Modal admin de restablecimiento de contraseña (HU #10174, AC1). Al confirmar llama
// a POST /auth/admin/reset-password; en éxito informa que el usuario deberá cambiarla
// en su próximo acceso. Maneja 403 (fuera de ámbito) y 404 sin aplicar cambios.
import { useState } from "react";
import { adminResetPassword } from "@/lib/api/auth";

export function ResetPasswordModal({ email, onClose }: { email: string; onClose: () => void }) {
  const [status, setStatus] = useState<"idle" | "loading" | "done">("idle");
  const [error, setError] = useState<string | null>(null);

  async function confirm() {
    setError(null);
    setStatus("loading");
    try {
      await adminResetPassword(email);
      setStatus("done");
    } catch (err) {
      const code = (err as { status?: number }).status;
      setError(
        code === 403
          ? "Acceso restringido: no tienes ámbito sobre este usuario."
          : code === 404
            ? "El usuario no existe."
            : "No se pudo restablecer la contraseña.",
      );
      setStatus("idle");
    }
  }

  return (
    <div
      className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 backdrop-blur-sm px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="reset-modal-title"
    >
      <div className="bg-white rounded-2xl w-full max-w-md p-6 shadow-2xl">
        <h2 id="reset-modal-title" className="text-lg font-semibold text-[#162744]">
          Restablecer contraseña
        </h2>

        {status === "done" ? (
          <>
            <p role="status" className="mt-2 text-sm text-slate-700">
              Contraseña restablecida. <strong>{email}</strong> deberá definir una nueva en su
              próximo inicio de sesión.
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
              Se enviará una contraseña temporal a <strong>{email}</strong> y se le exigirá
              cambiarla en el próximo acceso.
            </p>
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
                style={{ background: "#557eff" }}
              >
                {status === "loading" ? "Procesando…" : "Confirmar reset"}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

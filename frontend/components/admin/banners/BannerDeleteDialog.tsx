"use client";

// Diálogo de confirmación de borrado de un banner (HU #12241 AC3 — negativo: exige confirmación
// explícita antes de eliminar). Mismo patrón que `CompanyStatusDialog.tsx`: al confirmar llama al
// DELETE (`deleteBanner`, que ya manda `confirm=true`); en error muestra el mensaje y NO cierra.
import { useState } from "react";
import { Modal } from "@/components/atom/Modal";
import { deleteBanner, type Banner } from "@/lib/api/admin-banners";

export interface BannerDeleteDialogProps {
  banner: Banner;
  onClose: () => void;
  onDeleted: (id: string) => void;
}

export function BannerDeleteDialog({ banner, onClose, onDeleted }: BannerDeleteDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function confirm() {
    setError(null);
    setBusy(true);
    try {
      await deleteBanner(banner.id);
      onDeleted(banner.id);
    } catch (err) {
      const code = (err as { status?: number }).status;
      setError(
        code === 404
          ? "El banner ya no existe."
          : err instanceof Error
            ? err.message
            : "No se pudo eliminar el banner.",
      );
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      size="sm"
      zClassName="z-[90]"
      title="Eliminar banner"
      titleClassName="text-lg font-semibold text-[#162744] dark:text-white"
    >
      <p className="mt-2 text-sm opacity-80">
        ¿Confirmas eliminar el banner <strong>{banner.name}</strong>? Dejará de mostrarse en el
        carrusel público de inmediato.
      </p>

      {error && (
        <p role="alert" className="mt-3 text-sm" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}

      <div className="mt-5 flex gap-3">
        <button
          type="button"
          onClick={onClose}
          disabled={busy}
          className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
        >
          Cancelar
        </button>
        <button
          type="button"
          onClick={() => void confirm()}
          disabled={busy}
          className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
          style={{ background: "#FF4E00" }}
        >
          {busy ? "Eliminando…" : "Eliminar"}
        </button>
      </div>
    </Modal>
  );
}

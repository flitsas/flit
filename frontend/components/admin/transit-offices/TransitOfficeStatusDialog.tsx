"use client";

// Confirmación de activar/desactivar un tenant OT desde el listado (RF02/RF03). Al
// confirmar llama a PUT /transit-office-tenants/{tenantId}/status; en error muestra el
// mensaje y NO cierra. Mismo patrón que CompanyStatusDialog (#10118).
import { useState } from "react";
import { Modal } from "@/components/atom/Modal";
import {
  setTransitOfficeTenantStatus,
  type TransitOfficeOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";

export interface TransitOfficeStatusDialogProps {
  /** Oficina con tenant OT (tenantId y estadoActivo no nulos). */
  office: TransitOfficeOperationalStatus;
  onClose: () => void;
  onConfirmed: (nextActivo: boolean) => void;
}

export function TransitOfficeStatusDialog({
  office,
  onClose,
  onConfirmed,
}: TransitOfficeStatusDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const deactivating = office.estadoActivo === true;
  const nextActivo = !office.estadoActivo;

  async function confirm() {
    if (!office.tenantId) {
      return;
    }
    setError(null);
    setBusy(true);
    try {
      await setTransitOfficeTenantStatus(office.tenantId, nextActivo);
      onConfirmed(nextActivo);
    } catch (err) {
      const code = (err as { status?: number }).status;
      setError(
        code === 404
          ? "El organismo de tránsito no existe."
          : "No se pudo cambiar el estado del organismo de tránsito.",
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
      title={deactivating ? "Desactivar organismo de tránsito" : "Activar organismo de tránsito"}
      titleClassName="text-lg font-semibold text-foreground"
    >
      <p className="mt-2 text-sm opacity-80">
        ¿Confirmas {deactivating ? "desactivar" : "activar"} el organismo de tránsito{" "}
        <strong>{office.name}</strong>?
        {deactivating
          ? " No podrá operar en la plataforma hasta que lo reactives (los datos se conservan)."
          : " Quedará habilitado para operar en la plataforma."}
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
          onClick={confirm}
          disabled={busy}
          className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
          style={{ background: deactivating ? "#FF4E00" : "#00DBD5" }}
        >
          {busy ? "Procesando…" : deactivating ? "Desactivar" : "Activar"}
        </button>
      </div>
    </Modal>
  );
}

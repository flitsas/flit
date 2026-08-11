"use client";

import { useEffect, useState } from "react";
import { Eye } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  getNotificationSample,
  type NotificationSample,
  type NotificationTestChannel,
} from "@/lib/api/admin-plataforma-notificaciones";

export interface NotificacionVistaPreviaModalProps {
  open: boolean;
  onClose: () => void;
  templateId: string;
  templateName: string;
  /** Variante HTML cuando la plantilla difiere por canal (p. ej. Trámite Aprobado/Rechazado). */
  channel?: NotificationTestChannel;
  /** Etiqueta corta para el título (p. ej. "FLIT" / "Renting"). */
  formatLabel?: string;
}

/**
 * SuperAdmin — Plataforma → Notificaciones → "Ver en vivo" (HU #11371, AC1/AC2).
 */
export function NotificacionVistaPreviaModal({
  open,
  onClose,
  templateId,
  templateName,
  channel,
  formatLabel,
}: NotificacionVistaPreviaModalProps) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const [sample, setSample] = useState<NotificationSample | null>(null);

  const load = () => {
    setStatus("loading");
    setSample(null);
    getNotificationSample(templateId, channel ? { channel } : undefined)
      .then((data) => {
        setSample(data);
        setStatus(data.html.trim().length === 0 ? "empty" : "ready");
      })
      .catch(() => setStatus("error"));
  };

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga al abrir el modal
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, templateId, channel]);

  const titleSuffix = formatLabel ? ` (${formatLabel})` : "";

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Ver en vivo — ${templateName}${titleSuffix}`}
      icon={Eye}
      size="xl"
    >
      <UiStateBoundary
        status={status}
        onRetry={load}
        errorMessage="No se pudo cargar el render de muestra de esta plantilla."
        emptyMessage="Esta plantilla no tiene contenido de muestra para mostrar."
        skeletonRows={3}
      >
        {sample ? (
          <div className="flex flex-col gap-2">
            <p className="text-xs text-[#59677D] dark:text-white/55">
              <span className="font-semibold text-[#162244] dark:text-white">Asunto: </span>
              {sample.subject}
            </p>
            <p
              role="note"
              className="text-[11px] text-[#59677D] dark:text-white/45"
              data-testid="notificaciones-vista-previa-aviso"
            >
              Este render es una muestra aislada. No se envía ningún correo al mostrarla.
            </p>
            <iframe
              title={`Vista previa aislada de ${templateName}${titleSuffix}`}
              srcDoc={sample.html}
              sandbox=""
              className="h-[520px] w-full rounded-xl border border-[#DFE5ED] bg-white dark:border-white/10"
              data-testid="notificaciones-vista-previa-iframe"
            />
          </div>
        ) : null}
      </UiStateBoundary>
    </Modal>
  );
}

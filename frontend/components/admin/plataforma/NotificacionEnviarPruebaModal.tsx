"use client";

import { useEffect, useState } from "react";
import { CircleCheck, Send, TriangleAlert } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import {
  sendNotificationTest,
  type NotificationTestChannel,
} from "@/lib/api/admin-plataforma-notificaciones";
import {
  mapSendTestApiError,
  mapSendTestOutcome,
  type SendTestFailure,
  type SendTestOutcome,
} from "@/lib/notificaciones/mensajes-envio-prueba";

type Phase = "mailbox-required" | "sending" | "result" | "failure";

export interface NotificacionEnviarPruebaModalProps {
  open: boolean;
  onClose: () => void;
  templateId: string;
  templateName: string;
  channel: NotificationTestChannel;
  channelLabel: string;
  /** AC5 — si el buzón no está configurado, el modal NO ejecuta el envío. */
  mailboxConfigured: boolean;
  /** Cierra este modal y lleva el foco a la sección de buzón de pruebas (AC5/AC6). */
  onRequestConfigureMailbox: () => void;
}

/**
 * SuperAdmin — Plataforma → Notificaciones → "Enviar prueba" (HU #11371, AC3/AC4/AC5).
 *
 * Uso de ejemplo:
 * <NotificacionEnviarPruebaModal open={open} onClose={close} templateId="security.invitation"
 *   templateName="Invitación a la plataforma" channel="FLIT_SMTP" channelLabel="Colas FLIT"
 *   mailboxConfigured={mailbox?.isConfigured ?? false} onRequestConfigureMailbox={focusBuzon} />
 */
export function NotificacionEnviarPruebaModal({
  open,
  onClose,
  templateId,
  templateName,
  channel,
  channelLabel,
  mailboxConfigured,
  onRequestConfigureMailbox,
}: NotificacionEnviarPruebaModalProps) {
  const [phase, setPhase] = useState<Phase>("sending");
  const [outcome, setOutcome] = useState<SendTestOutcome | null>(null);
  const [failure, setFailure] = useState<SendTestFailure | null>(null);

  useEffect(() => {
    if (!open) return;

    // AC5 — sin buzón configurado, NO se ejecuta el envío; la pantalla lo solicita.
    if (!mailboxConfigured) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- decisión de fase al abrir
      setPhase("mailbox-required");
      return;
    }

    // eslint-disable-next-line react-hooks/set-state-in-effect -- inicio del envío al abrir
    setPhase("sending");
    setOutcome(null);
    setFailure(null);

    sendNotificationTest(templateId, channel)
      .then((result) => {
        setOutcome(mapSendTestOutcome(result));
        setPhase("result");
      })
      .catch((err) => {
        setFailure(mapSendTestApiError(err));
        setPhase("failure");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, mailboxConfigured, templateId, channel]);

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Enviar prueba — ${templateName}`}
      icon={Send}
      size="sm"
    >
      <div className="flex flex-col gap-3 text-sm" data-testid="notificaciones-enviar-prueba-modal">
        {phase === "mailbox-required" ? (
          <div
            className="flex flex-col gap-2 rounded-xl border border-[#FF4E00]/30 bg-[#FF4E00]/10 p-3 text-xs text-[#9A3412] dark:text-[#FFB199]"
            role="alert"
            data-testid="notificaciones-enviar-prueba-sin-buzon"
          >
            <p className="inline-flex items-center gap-1.5 font-semibold">
              <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              Configura el buzón de pruebas primero
            </p>
            <p>No hay un buzón de pruebas configurado, así que no se envió ningún correo.</p>
            <button
              type="button"
              onClick={() => {
                onClose();
                onRequestConfigureMailbox();
              }}
              className="mt-1 w-fit rounded-full bg-gradient-to-r from-[#22D3C5] to-[#557EFF] px-3 py-1.5 text-[11px] font-semibold text-white"
            >
              Configurar buzón
            </button>
          </div>
        ) : null}

        {phase === "sending" ? (
          <p role="status" aria-live="polite" data-testid="notificaciones-enviar-prueba-cargando">
            Enviando correo de prueba por {channelLabel}…
          </p>
        ) : null}

        {phase === "failure" && failure ? (
          <div
            className="flex flex-col gap-1.5 rounded-xl border border-[#FF4E00]/30 bg-[#FF4E00]/10 p-3 text-xs text-[#9A3412] dark:text-[#FFB199]"
            role="alert"
            data-testid="notificaciones-enviar-prueba-error"
          >
            <p className="inline-flex items-center gap-1.5 font-semibold">
              <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              El envío no se completó
            </p>
            <p>{failure.productMessage}</p>
          </div>
        ) : null}

        {phase === "result" && outcome ? (
          <div
            className="flex flex-col gap-1.5 rounded-xl border p-3 text-xs"
            style={
              outcome.kind === "sent"
                ? {
                    background: "var(--badge-success-bg)",
                    color: "var(--badge-success-fg)",
                    borderColor: "var(--badge-success-border)",
                  }
                : {
                    background: "var(--badge-danger-bg)",
                    color: "var(--badge-danger-fg)",
                    borderColor: "var(--badge-danger-border)",
                  }
            }
            role="status"
            data-testid="notificaciones-enviar-prueba-resultado"
          >
            <p className="inline-flex items-center gap-1.5 font-semibold">
              {outcome.kind === "sent" ? (
                <CircleCheck className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              ) : (
                <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              )}
              {outcome.kind === "sent" ? "El transporte aceptó el correo" : "El transporte falló"}
            </p>
            <p>{outcome.productMessage}</p>
            {/* AC3 — siempre se advierte que la aceptación del transporte no garantiza la entrega. */}
            <p className="opacity-80">{outcome.disclaimer}</p>
            {outcome.consoleTransportNotice ? (
              <p className="font-semibold" data-testid="notificaciones-enviar-prueba-consola">
                {outcome.consoleTransportNotice}
              </p>
            ) : null}
          </div>
        ) : null}
      </div>
    </Modal>
  );
}

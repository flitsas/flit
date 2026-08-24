"use client";

import { useCallback, useEffect, useState } from "react";
import { CircleCheck, Send, TriangleAlert } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import type { UiStatus } from "@/components/admin/UiStateBoundary";
import {
  sendNotificationTest,
  type NotificationTestChannel,
} from "@/lib/api/admin-plataforma-notificaciones";
import { superadminClient } from "@/lib/api/superadmin-client";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import {
  mapSendTestApiError,
  mapSendTestOutcome,
  type SendTestFailure,
  type SendTestOutcome,
} from "@/lib/notificaciones/mensajes-envio-prueba";
import {
  isTramiteCambioEstadoTemplate,
  NotificacionProcedureTypeFields,
} from "./NotificacionProcedureTypeFields";

type Phase = "mailbox-required" | "select-type" | "sending" | "result" | "failure";

export interface NotificacionEnviarPruebaModalProps {
  open: boolean;
  onClose: () => void;
  templateId: string;
  templateName: string;
  channel: NotificationTestChannel;
  channelLabel: string;
  mailboxConfigured: boolean;
  onRequestConfigureMailbox: () => void;
}

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
  const requiresType = isTramiteCambioEstadoTemplate(templateId);
  const [phase, setPhase] = useState<Phase>("sending");
  const [outcome, setOutcome] = useState<SendTestOutcome | null>(null);
  const [failure, setFailure] = useState<SendTestFailure | null>(null);
  const [types, setTypes] = useState<ProcedureTypeSummary[]>([]);
  const [catalogStatus, setCatalogStatus] = useState<UiStatus>("loading");
  const [family, setFamily] = useState("");
  const [typeId, setTypeId] = useState("");

  const loadCatalog = useCallback(() => {
    setCatalogStatus("loading");
    superadminClient
      .listProcedureTypes()
      .then((items) => {
        const active = items.filter((t) => t.isActive);
        setTypes(active);
        setCatalogStatus(active.length === 0 ? "empty" : "ready");
      })
      .catch(() => setCatalogStatus("error"));
  }, []);

  const runSend = useCallback(
    (procedureTypeId?: string) => {
      setPhase("sending");
      setOutcome(null);
      setFailure(null);
      const send = procedureTypeId
        ? sendNotificationTest(templateId, channel, procedureTypeId)
        : sendNotificationTest(templateId, channel);
      send
        .then((result) => {
          setOutcome(mapSendTestOutcome(result));
          setPhase("result");
        })
        .catch((err) => {
          setFailure(mapSendTestApiError(err));
          setPhase("failure");
        });
    },
    [channel, templateId],
  );

  useEffect(() => {
    if (!open) return;

    if (!mailboxConfigured) {
      setPhase("mailbox-required");
      return;
    }

    setFamily("");
    setTypeId("");
    setOutcome(null);
    setFailure(null);

    if (requiresType) {
      setPhase("select-type");
      loadCatalog();
      return;
    }

    runSend();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, mailboxConfigured, templateId, channel, requiresType, loadCatalog]);

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Enviar prueba — ${templateName}`}
      icon={Send}
      size={requiresType ? "lg" : "sm"}
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

        {phase === "select-type" ? (
          <div className="flex flex-col gap-3">
            <NotificacionProcedureTypeFields
              types={types}
              catalogStatus={catalogStatus}
              onRetryCatalog={loadCatalog}
              family={family}
              typeId={typeId}
              onFamilyChange={setFamily}
              onTypeIdChange={setTypeId}
              idPrefix="notificaciones-envio"
            />
            <button
              type="button"
              disabled={!typeId}
              onClick={() => runSend(typeId)}
              className="w-fit rounded-full bg-gradient-to-r from-[#22D3C5] to-[#557EFF] px-3 py-1.5 text-[11px] font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
              data-testid="notificaciones-enviar-prueba-confirmar"
            >
              Enviar
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
            <p className="opacity-80">{outcome.disclaimer}</p>
            {outcome.consoleTransportNotice ? (
              <p className="font-semibold" data-testid="notificaciones-enviar-prueba-consola">
                {outcome.consoleTransportNotice}
              </p>
            ) : null}
            {outcome.recipientDivertedNotice ? (
              <p
                role="alert"
                className="inline-flex items-start gap-1.5 rounded-lg border p-2 font-semibold"
                style={{
                  background: "var(--badge-warning-bg)",
                  color: "var(--badge-warning-fg)",
                  borderColor: "var(--badge-warning-border)",
                }}
                data-testid="notificaciones-enviar-prueba-desvio"
              >
                <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{outcome.recipientDivertedNotice}</span>
              </p>
            ) : null}
          </div>
        ) : null}
      </div>
    </Modal>
  );
}

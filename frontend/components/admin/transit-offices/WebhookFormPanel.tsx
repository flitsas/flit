"use client";

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import type { CreateOtWebhookRequest, OtWebhook, UpdateOtWebhookRequest } from "@/lib/api/types-ot";
import { OtSidePanel } from "./OtSidePanel";
import { OT_INPUT_CLS } from "./ot-form-styles";
import { OT_WEBHOOK_EVENT_TYPES } from "./ot-utils";

export interface WebhookFormPanelProps {
  open: boolean;
  editing: OtWebhook | null;
  onClose: () => void;
  onCreate: (body: CreateOtWebhookRequest) => Promise<OtWebhook>;
  onUpdate: (id: string, body: UpdateOtWebhookRequest) => Promise<OtWebhook>;
  onSaved: (webhook: OtWebhook, isNew: boolean) => void;
}

/** Formulario lateral crear/editar webhook (HU #10219 AC2/AC3). */
export function WebhookFormPanel({
  open,
  editing,
  onClose,
  onCreate,
  onUpdate,
  onSaved,
}: WebhookFormPanelProps) {
  const [eventType, setEventType] = useState(editing?.eventType ?? OT_WEBHOOK_EVENT_TYPES[0].value);
  const [targetUrl, setTargetUrl] = useState(editing?.targetUrl ?? "");
  const [secret, setSecret] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset de formulario al abrir panel lateral
    setEventType(editing?.eventType ?? OT_WEBHOOK_EVENT_TYPES[0].value);
    setTargetUrl(editing?.targetUrl ?? "");
    setSecret("");
  }, [open, editing]);

  const isEdit = editing !== null;

  const submit = async () => {
    setSubmitting(true);
    try {
      if (isEdit) {
        const updated = await onUpdate(editing.id, { targetUrl: targetUrl.trim() });
        onSaved(updated, false);
      } else {
        const created = await onCreate({
          eventType,
          targetUrl: targetUrl.trim(),
          secret,
        });
        onSaved(created, true);
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <OtSidePanel
      open={open}
      title={isEdit ? "Editar webhook" : "Nuevo webhook"}
      ariaLabel={isEdit ? "Editar webhook" : "Nuevo webhook"}
      onClose={onClose}
      disabled={submitting}
      footer={
        <button
          type="button"
          disabled={submitting || !targetUrl.trim() || (!isEdit && !secret.trim())}
          className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: "#557EFF" }}
          onClick={() => void submit()}
        >
          {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
          Guardar
        </button>
      }
    >
      <div className="space-y-3">
        {!isEdit && (
          <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
            Tipo de evento
            <select
              className={`mt-1 ${OT_INPUT_CLS}`}
              value={eventType}
              onChange={(e) => setEventType(e.target.value)}
            >
              {OT_WEBHOOK_EVENT_TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          </label>
        )}
        <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
          URL destino (HTTPS)
          <input
            type="url"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={targetUrl}
            onChange={(e) => setTargetUrl(e.target.value)}
          />
        </label>
        {!isEdit && (
          <label className="block text-xs font-semibold" style={{ color: "#162744" }}>
            Secreto
            <input
              type="password"
              className={`mt-1 ${OT_INPUT_CLS}`}
              value={secret}
              onChange={(e) => setSecret(e.target.value)}
            />
          </label>
        )}
      </div>
    </OtSidePanel>
  );
}

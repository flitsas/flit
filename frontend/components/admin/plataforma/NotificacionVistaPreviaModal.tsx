"use client";

import { useCallback, useEffect, useState } from "react";
import { Eye } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  getNotificationSample,
  type NotificationSample,
  type NotificationTestChannel,
} from "@/lib/api/admin-plataforma-notificaciones";
import { superadminClient } from "@/lib/api/superadmin-client";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import {
  isTramiteCambioEstadoTemplate,
  NotificacionProcedureTypeFields,
} from "./NotificacionProcedureTypeFields";

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
  const requiresType = isTramiteCambioEstadoTemplate(templateId);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [sample, setSample] = useState<NotificationSample | null>(null);
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

  const loadSample = useCallback(() => {
    if (requiresType && !typeId) {
      setStatus("empty");
      setSample(null);
      return;
    }
    setStatus("loading");
    setSample(null);
    getNotificationSample(templateId, {
      channel,
      procedureTypeId: requiresType ? typeId : undefined,
    })
      .then((data) => {
        setSample(data);
        setStatus(data.html.trim().length === 0 ? "empty" : "ready");
      })
      .catch(() => setStatus("error"));
  }, [channel, requiresType, templateId, typeId]);

  useEffect(() => {
    if (!open) return;
    /* eslint-disable react-hooks/set-state-in-effect -- reset de selects y muestra al abrir el modal */
    setFamily("");
    setTypeId("");
    setSample(null);
    if (requiresType) {
      setStatus("empty");
      loadCatalog();
      return;
    }
    setStatus("loading");
    getNotificationSample(templateId, { channel })
      .then((data) => {
        setSample(data);
        setStatus(data.html.trim().length === 0 ? "empty" : "ready");
      })
      .catch(() => setStatus("error"));
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, templateId, channel, requiresType, loadCatalog]);

  useEffect(() => {
    if (!open || !requiresType || !typeId) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async de la muestra al elegir tipo
    loadSample();
  }, [open, requiresType, typeId, loadSample]);

  const titleSuffix = formatLabel ? ` (${formatLabel})` : "";
  const selectedName = types.find((t) => t.id === typeId)?.name;
  const typeSuffix = selectedName ? ` — ${selectedName}` : "";

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Ver en vivo — ${templateName}${titleSuffix}${typeSuffix}`}
      icon={Eye}
      size="xl"
    >
      <div className="flex flex-col gap-4">
        {requiresType ? (
          <NotificacionProcedureTypeFields
            types={types}
            catalogStatus={catalogStatus}
            onRetryCatalog={loadCatalog}
            family={family}
            typeId={typeId}
            onFamilyChange={setFamily}
            onTypeIdChange={setTypeId}
            idPrefix="notificaciones-preview"
          />
        ) : null}

        {requiresType && !typeId ? (
          <p className="text-xs text-[#59677D] dark:text-white/55" role="status">
            Elige un tipo de trámite activo para generar la muestra.
          </p>
        ) : (
          <UiStateBoundary
            status={status}
            onRetry={loadSample}
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
        )}
      </div>
    </Modal>
  );
}

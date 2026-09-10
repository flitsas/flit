"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Eye, Info, Mail, Send } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  getTestMailbox,
  listNotificationTemplates,
  type NotificationTemplateItem,
  type NotificationTestChannel,
  type NotificationTestMailbox,
} from "@/lib/api/admin-plataforma-notificaciones";
import { NotificacionBuzonPruebasSection } from "./NotificacionBuzonPruebasSection";
import { NotificacionVistaPreviaModal } from "./NotificacionVistaPreviaModal";
import { NotificacionEnviarPruebaModal } from "./NotificacionEnviarPruebaModal";

/**
 * Fila del banco de pruebas. Las plantillas vienen del catálogo FLIT; la fila `kyverum` es
 * SINTÉTICA — no existe endpoint de plantilla para ella porque el correo lo emite Kyverum Verify.
 */
interface BankRow {
  id: string;
  name: string;
  module: string;
  triggers: string[];
  kind: "plantilla" | "kyverum";
}

const KYVERUM_ROW: BankRow = {
  id: "kyverum.identity-verification",
  name: "Validación de identidad (Kyverum Verify)",
  module: "Identidad",
  triggers: ["Prevalidación biométrica", "Reenvío de validación"],
  kind: "kyverum",
};

type PanelStatus = "loading" | "error" | "ready";

interface PreviewTarget {
  row: BankRow;
  channel?: NotificationTestChannel;
  formatLabel?: string;
}

interface SendTarget {
  row: BankRow;
  channel: NotificationTestChannel;
  channelLabel: string;
}

/**
 * SuperAdmin — Plataforma → Notificaciones → Banco de pruebas.
 * Buzón arriba; en Acciones, FLIT y Renting apilados en dos columnas internas.
 */
export function NotificacionesBankPanel() {
  const [templates, setTemplates] = useState<NotificationTemplateItem[]>([]);
  const [status, setStatus] = useState<PanelStatus>("loading");

  const [mailbox, setMailbox] = useState<NotificationTestMailbox | null>(null);
  const [mailboxStatus, setMailboxStatus] = useState<PanelStatus>("loading");
  const buzonSectionRef = useRef<HTMLDivElement | null>(null);

  const [previewTarget, setPreviewTarget] = useState<PreviewTarget | null>(null);
  const [sendTarget, setSendTarget] = useState<SendTarget | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const templateItems = await listNotificationTemplates();
      setTemplates(templateItems);
      setStatus("ready");
    } catch {
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API
    void load();
  }, [load]);

  const loadMailbox = useCallback(async () => {
    setMailboxStatus("loading");
    try {
      const data = await getTestMailbox();
      setMailbox(data);
      setMailboxStatus("ready");
    } catch {
      setMailboxStatus("error");
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API
    void loadMailbox();
  }, [loadMailbox]);

  const focusBuzonSection = useCallback(() => {
    buzonSectionRef.current?.scrollIntoView?.({ behavior: "smooth", block: "center" });
    buzonSectionRef.current?.querySelector("button")?.focus();
  }, []);

  const rows: BankRow[] = useMemo(
    () => [
      ...templates.map((t) => ({
        id: t.id,
        name: t.name,
        module: t.module,
        triggers: t.triggers,
        kind: "plantilla" as const,
      })),
      KYVERUM_ROW,
    ],
    [templates],
  );

  const senderText = (row: BankRow): string => {
    if (row.kind === "kyverum") {
      return "Kyverum Verify (proveedor externo)";
    }
    if (row.module === "Security") {
      return "Colas FLIT";
    }
    return "Según botón: FLIT o Renting";
  };

  /** Correos de cuenta (Security): solo acciones FLIT — no se muestran botones Renting. */
  const showRentingActions = (row: BankRow): boolean => row.module !== "Security";

  const actionButtonClass =
    "inline-flex items-center gap-1 rounded-full border border-[#DFE5ED] px-2.5 py-1 text-[11px] font-semibold text-[#162244] transition-colors hover:bg-[#F4F7FC] dark:border-white/10 dark:text-white dark:hover:bg-white/5";

  const actionStackClass = "flex flex-col items-stretch gap-1.5";

  const renderPlantillaActions = (row: BankRow) => {
    const rentingVisible = showRentingActions(row);
    return (
      <div
        className="inline-flex flex-row items-start justify-end gap-3"
        data-testid={`notificaciones-acciones-${row.id}`}
      >
        <div className={actionStackClass} data-testid={`notificaciones-acciones-flit-${row.id}`}>
          <button
            type="button"
            onClick={() => setPreviewTarget({ row, channel: "FLIT_SMTP", formatLabel: "FLIT" })}
            aria-label={`Preview FLIT de ${row.name}`}
            className={actionButtonClass}
          >
            <Eye className="h-3 w-3" aria-hidden="true" />
            Preview FLIT
          </button>
          <button
            type="button"
            onClick={() =>
              setSendTarget({
                row,
                channel: "FLIT_SMTP",
                channelLabel: "Colas FLIT",
              })
            }
            aria-label={`Enviar FLIT de ${row.name}`}
            className={actionButtonClass}
          >
            <Send className="h-3 w-3" aria-hidden="true" />
            Enviar FLIT
          </button>
        </div>
        {rentingVisible ? (
          <div
            className={actionStackClass}
            data-testid={`notificaciones-acciones-renting-${row.id}`}
          >
            <button
              type="button"
              onClick={() =>
                setPreviewTarget({ row, channel: "TENANT_API", formatLabel: "Renting" })
              }
              aria-label={`Preview Renting de ${row.name}`}
              className={actionButtonClass}
            >
              <Eye className="h-3 w-3" aria-hidden="true" />
              Preview Renting
            </button>
            <button
              type="button"
              onClick={() =>
                setSendTarget({
                  row,
                  channel: "TENANT_API",
                  channelLabel: "API Renting cliente",
                })
              }
              aria-label={`Enviar Renting de ${row.name}`}
              className={actionButtonClass}
            >
              <Send className="h-3 w-3" aria-hidden="true" />
              Enviar Renting
            </button>
          </div>
        ) : null}
      </div>
    );
  };

  const columns: DataTableColumn<BankRow>[] = [
    {
      key: "plantilla",
      header: "Plantilla",
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-semibold text-[#162244] dark:text-white">{row.name}</span>
          <span className="text-[11px] text-[#59677D] dark:text-white/55">
            {row.triggers.join(" · ")}
          </span>
        </div>
      ),
    },
    {
      key: "modulo",
      header: "Módulo",
      render: (row) => (
        <StatusBadge label={row.module} tone={row.kind === "kyverum" ? "neutral" : "info"} />
      ),
    },
    {
      key: "remitente",
      header: "Remitente",
      render: (row) => (
        <span className="inline-flex items-center gap-1.5 text-xs text-[#162244] dark:text-white/80">
          <Mail className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          {senderText(row)}
        </span>
      ),
    },
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (row) => {
        if (row.kind === "kyverum") {
          return (
            <span className="inline-flex max-w-[220px] items-start justify-end gap-1.5 text-right text-[11px] text-[#59677D] dark:text-white/55">
              <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              El correo lo emite el proveedor; FLIT no controla su contenido.
            </span>
          );
        }
        return renderPlantillaActions(row);
      },
    },
  ];

  return (
    <div className="flex flex-col gap-6" data-testid="notificaciones-bank-panel">
      <div ref={buzonSectionRef}>
        <NotificacionBuzonPruebasSection
          mailbox={mailbox}
          status={mailboxStatus}
          onRetry={() => void loadMailbox()}
          onSaved={setMailbox}
        />
      </div>

      <section aria-labelledby="notificaciones-tabla-heading" className="flex flex-col gap-3">
        <h2
          id="notificaciones-tabla-heading"
          className="text-sm font-semibold text-[#162244] dark:text-white"
        >
          Banco de pruebas ({rows.length})
        </h2>
        <DataTable
          columns={columns}
          rows={rows}
          getRowKey={(row) => row.id}
          status={status === "loading" ? "loading" : status === "error" ? "error" : undefined}
          onRetry={() => void load()}
          errorMessage="No se pudo cargar el banco de pruebas de notificaciones."
          ariaLabel="Banco de pruebas de notificaciones"
          emptyMessage="No hay plantillas disponibles."
          minWidth={980}
        />
      </section>

      <NotificacionVistaPreviaModal
        open={previewTarget !== null}
        onClose={() => setPreviewTarget(null)}
        templateId={previewTarget?.row.id ?? ""}
        templateName={previewTarget?.row.name ?? ""}
        channel={previewTarget?.channel}
        formatLabel={previewTarget?.formatLabel}
      />

      <NotificacionEnviarPruebaModal
        open={sendTarget !== null}
        onClose={() => setSendTarget(null)}
        templateId={sendTarget?.row.id ?? ""}
        templateName={sendTarget?.row.name ?? ""}
        channel={sendTarget?.channel ?? "FLIT_SMTP"}
        channelLabel={sendTarget?.channelLabel ?? "canal seleccionado"}
        mailboxConfigured={mailbox?.isConfigured ?? false}
        onRequestConfigureMailbox={focusBuzonSection}
      />
    </div>
  );
}

"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Building2, Eye, Info, Mail, Send, TriangleAlert } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  getTestMailbox,
  listNotificationChannels,
  listNotificationTemplates,
  type NotificationChannelItem,
  type NotificationTemplateItem,
  type NotificationTestMailbox,
} from "@/lib/api/admin-plataforma-notificaciones";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import type { CompanyListItem } from "@/lib/api/types";
import { NotificacionBuzonPruebasSection } from "./NotificacionBuzonPruebasSection";
import { NotificacionVistaPreviaModal } from "./NotificacionVistaPreviaModal";
import { NotificacionEnviarPruebaModal } from "./NotificacionEnviarPruebaModal";

/**
 * Fila del banco de pruebas. Las 5 primeras vienen del catálogo de plantillas propias de FLIT
 * (HU #11356); la fila `kyverum` es SINTÉTICA — no existe endpoint de plantilla para ella porque
 * el correo lo emite Kyverum Verify, no FLIT (ver `docs/inventario-correos-plataforma.md` §3).
 * Se agrega a mano para cumplir el AC1 (6 filas) sin inventar un contrato de plantilla que no existe.
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

/**
 * SuperAdmin — Plataforma → Notificaciones → Banco de pruebas (HU #11370, Feature #11349).
 * Pantalla de lectura: selector de canal, selector de compañía, tabla de 6 filas (5 plantillas
 * FLIT + Kyverum), remitente resuelto por canal y aviso de disponibilidad del canal.
 *
 * HU #11371 añade el cableado de "ver en vivo" (render de muestra en `iframe` aislado) y
 * "enviar prueba" (POST al buzón de pruebas configurado), más la sección visible del buzón.
 */
export function NotificacionesBankPanel() {
  const [templates, setTemplates] = useState<NotificationTemplateItem[]>([]);
  const [channels, setChannels] = useState<NotificationChannelItem[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [status, setStatus] = useState<PanelStatus>("loading");
  const [selectedChannel, setSelectedChannel] = useState<string>("");
  const [selectedCompanyId, setSelectedCompanyId] = useState<string>("");

  // HU #11371 — buzón de pruebas, cargado aparte porque su reintento es independiente de la
  // carga principal (plantillas/canales/compañías).
  const [mailbox, setMailbox] = useState<NotificationTestMailbox | null>(null);
  const [mailboxStatus, setMailboxStatus] = useState<PanelStatus>("loading");
  const buzonSectionRef = useRef<HTMLDivElement | null>(null);

  // HU #11371 — modales "ver en vivo" / "enviar prueba", una fila a la vez.
  const [previewRow, setPreviewRow] = useState<BankRow | null>(null);
  const [sendRow, setSendRow] = useState<BankRow | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const [templateItems, channelItems, companiesPage] = await Promise.all([
        listNotificationTemplates(),
        listNotificationChannels(),
        fetchCompaniesIndex({ page: 1, pageSize: 50 }),
      ]);
      setTemplates(templateItems);
      setChannels(channelItems);
      setCompanies(companiesPage.data ?? []);

      // AC1 — el canal seleccionado al terminar la carga es el default (Colas FLIT).
      const defaultChannel = channelItems.find((c) => c.isDefault) ?? channelItems[0];
      setSelectedChannel((prev) => prev || defaultChannel?.channel || "");
      setSelectedCompanyId((prev) => prev || companiesPage.data?.[0]?.id || "");

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
    // `scrollIntoView` no existe en algunos entornos de test (jsdom sin stub); no es crítico
    // para la accesibilidad (el foco sí lo es), así que se protege con un guard.
    buzonSectionRef.current?.scrollIntoView?.({ behavior: "smooth", block: "center" });
    buzonSectionRef.current?.querySelector("button")?.focus();
  }, []);

  const selectedChannelObj = useMemo(
    () => channels.find((c) => c.channel === selectedChannel) ?? channels[0],
    [channels, selectedChannel],
  );

  const selectedCompany = useMemo(
    () => companies.find((c) => c.id === selectedCompanyId) ?? null,
    [companies, selectedCompanyId],
  );

  // AC1/AC2 — siempre las 5 plantillas del catálogo + la fila Kyverum; cambiar de canal
  // recalcula el remitente mostrado pero nunca agrega ni quita filas.
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

  // AC6 — decisión del PO (ver HU #11370): el literal del AC pedía "el canal elegido todavía no
  // altera el transporte del correo", una frase que sería falsa el día del despliegue porque el
  // enrutamiento (#11348) entra en el mismo PR que esta pantalla. El aviso se deriva de
  // `isConfigured` del canal seleccionado y solo advierte sobre disponibilidad en este ambiente;
  // nunca afirma ni insinúa que el Bug #11311 siga vivo.
  const channelWarning =
    selectedChannelObj && !selectedChannelObj.isConfigured
      ? `El canal “${selectedChannelObj.label}” no está configurado ni habilitado en este ambiente. Un envío de prueba por este canal no va a salir hasta que se configure aquí.`
      : null;

  const senderText = (row: BankRow): string => {
    if (row.kind === "kyverum") {
      return "Kyverum Verify (proveedor externo)";
    }
    if (!selectedChannelObj?.isConfigured) {
      return "Sin remitente configurado en este canal";
    }
    const name = selectedChannelObj.senderName ?? "";
    const email = selectedChannelObj.senderEmail ?? "";
    return name ? `${name} <${email}>` : email || "—";
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
      render: (row) => <StatusBadge label={row.module} tone={row.kind === "kyverum" ? "neutral" : "info"} />,
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
        // AC3 — la fila Kyverum es informativa: sin "ver en vivo" ni "enviar prueba", con el
        // motivo visible en la propia celda (no en un tooltip escondido).
        if (row.kind === "kyverum") {
          return (
            <span className="inline-flex items-center justify-end gap-1.5 text-right text-[11px] text-[#59677D] dark:text-white/55">
              <Info className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              El correo lo emite el proveedor; FLIT no controla su contenido.
            </span>
          );
        }
        // HU #11371 — "ver en vivo" abre el render de muestra aislado (AC1/AC2); "enviar
        // prueba" dispara el envío honesto (AC3/AC4/AC5).
        return (
          <div className="inline-flex items-center justify-end gap-1.5">
            <button
              type="button"
              onClick={() => setPreviewRow(row)}
              aria-label={`Ver en vivo ${row.name}`}
              className="inline-flex items-center gap-1 rounded-full border border-[#DFE5ED] px-2.5 py-1 text-[11px] font-semibold text-[#162244] transition-colors hover:bg-[#F4F7FC] dark:border-white/10 dark:text-white dark:hover:bg-white/5"
            >
              <Eye className="h-3 w-3" aria-hidden="true" />
              Ver en vivo
            </button>
            <button
              type="button"
              onClick={() => setSendRow(row)}
              aria-label={`Enviar prueba de ${row.name}`}
              className="inline-flex items-center gap-1 rounded-full border border-[#DFE5ED] px-2.5 py-1 text-[11px] font-semibold text-[#162244] transition-colors hover:bg-[#F4F7FC] dark:border-white/10 dark:text-white dark:hover:bg-white/5"
            >
              <Send className="h-3 w-3" aria-hidden="true" />
              Enviar prueba
            </button>
          </div>
        );
      },
    },
  ];

  return (
    <div className="flex flex-col gap-6" data-testid="notificaciones-bank-panel">
      <section aria-labelledby="notificaciones-selectores-heading" className="flex flex-col gap-3">
        <h2 id="notificaciones-selectores-heading" className="sr-only">
          Canal y compañía
        </h2>
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="flex flex-col gap-1 text-xs font-semibold text-[#162244] dark:text-white">
            Canal
            <select
              value={selectedChannel}
              onChange={(e) => setSelectedChannel(e.target.value)}
              disabled={status !== "ready" || channels.length === 0}
              className="rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              aria-label="Canal de notificación"
            >
              {channels.map((c) => (
                <option key={c.channel} value={c.channel}>
                  {c.label}
                </option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-xs font-semibold text-[#162244] dark:text-white">
            Compañía
            <select
              value={selectedCompanyId}
              onChange={(e) => setSelectedCompanyId(e.target.value)}
              disabled={status !== "ready" || companies.length === 0}
              className="rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              aria-label="Compañía"
            >
              {companies.length === 0 ? (
                <option value="">Sin compañías disponibles</option>
              ) : (
                companies.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.razonSocial} · {c.nit}
                  </option>
                ))
              )}
            </select>
          </label>
        </div>

        {channelWarning ? (
          <div
            role="status"
            aria-live="polite"
            className="flex items-start gap-2 rounded-xl border border-[#FF4E00]/30 bg-[#FF4E00]/10 px-3 py-2 text-xs text-[#9A3412] dark:text-[#FFB199]"
            data-testid="notificaciones-canal-aviso"
          >
            <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            <span>{channelWarning}</span>
          </div>
        ) : null}
      </section>

      <section aria-labelledby="notificaciones-identidad-heading" className="flex flex-col gap-3">
        <h2 id="notificaciones-identidad-heading" className="sr-only">
          Vista previa e identidad de la compañía
        </h2>
        <div className="grid gap-3 md:grid-cols-[2fr_1fr]">
          <div className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]">
            <p className="text-xs font-semibold text-[#162244] dark:text-white">Vista previa</p>
            <p className="mt-2 text-xs leading-relaxed text-[#59677D] dark:text-white/65">
              El contenido de la plantilla es fijo (lo compone el catálogo del backend) y no cambia
              al cambiar de compañía. Usa “Ver en vivo” en cada fila para ver el render completo
              en un marco aislado, sin enviar ningún correo.
            </p>
          </div>

          {/* AC5 — bloque de identidad SIN logotipo ni control de carga: `Tenant` no tiene campo de
              logo por compañía, así que no se promete algo que el modelo no puede dar. */}
          <div
            className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]"
            data-testid="notificaciones-identidad-compania"
          >
            <p className="inline-flex items-center gap-1.5 text-xs font-semibold text-[#162244] dark:text-white">
              <Building2 className="h-3.5 w-3.5" aria-hidden="true" />
              Compañía
            </p>
            {selectedCompany ? (
              <div className="mt-2 flex flex-col gap-0.5">
                <span className="text-sm font-semibold text-[#162244] dark:text-white">
                  {selectedCompany.razonSocial}
                </span>
                <span className="font-mono text-[11px] text-[#59677D] dark:text-white/55">
                  NIT {selectedCompany.nit}
                </span>
              </div>
            ) : (
              <p className="mt-2 text-xs text-[#59677D] dark:text-white/55">
                No hay compañías registradas para mostrar identidad.
              </p>
            )}
          </div>
        </div>
      </section>

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
          minWidth={860}
        />
      </section>

      {/* HU #11371 (AC5/AC6) — buzón de pruebas visible y modificable. */}
      <div ref={buzonSectionRef}>
        <NotificacionBuzonPruebasSection
          mailbox={mailbox}
          status={mailboxStatus}
          onRetry={() => void loadMailbox()}
          onSaved={setMailbox}
        />
      </div>

      {/* HU #11371 (AC1/AC2) — "ver en vivo" en marco aislado, sin enviar correo. */}
      <NotificacionVistaPreviaModal
        open={previewRow !== null}
        onClose={() => setPreviewRow(null)}
        templateId={previewRow?.id ?? ""}
        templateName={previewRow?.name ?? ""}
      />

      {/* HU #11371 (AC3/AC4/AC5) — envío de prueba con resultado honesto. */}
      <NotificacionEnviarPruebaModal
        open={sendRow !== null}
        onClose={() => setSendRow(null)}
        templateId={sendRow?.id ?? ""}
        templateName={sendRow?.name ?? ""}
        channel={(selectedChannelObj?.channel as "FLIT_SMTP" | "TENANT_API") ?? "FLIT_SMTP"}
        channelLabel={selectedChannelObj?.label ?? "canal seleccionado"}
        mailboxConfigured={mailbox?.isConfigured ?? false}
        onRequestConfigureMailbox={focusBuzonSection}
      />
    </div>
  );
}

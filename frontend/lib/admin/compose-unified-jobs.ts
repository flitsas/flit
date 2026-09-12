import type { StatusTone } from "@/components/atom/StatusBadge";
import { formatFechaHora } from "@/lib/format/date";
import type { IctJobCatalogItem } from "@/lib/api/admin-ict-job-catalog";
import type { IctJobSettings } from "@/lib/api/admin-ict-job-settings";
import type { QuipuxSettings } from "@/lib/api/admin-quipux-settings";

export type JobModule = "ICT" | "Quipux";

export interface UnifiedJobRow {
  key: string;
  /** Nombre que lee el SuperAdmin (español). */
  displayName: string;
  /** Identificador técnico, línea secundaria. */
  technicalName: string;
  module: JobModule;
  owner: string;
  types: string[];
  typeLabels: string[];
  enabledLabel: string;
  enabledTone: StatusTone;
  intervalLabel: string;
  lastRunLabel: string;
  lastRunPrimary: string;
  lastRunSecondary: string | null;
  lastRunTone: StatusTone;
  notes?: string;
}

const TYPE_LABEL: Record<string, string> = {
  BD: "Base de datos",
  ENDPOINT_INTERNO: "API interna",
  ENDPOINT_EXTERNO: "API externa",
};

const ICT_TITLE: Record<string, string> = {
  "business-validation": "Validación de negocio",
  "external-validation": "Validación externa",
  orchestrator: "Consultas RUNT",
  "send-to-core-api": "Envío a FLIT",
  "webhook-notification": "Notificaciones a gestores",
  retention: "Retención de logs",
};

const ICT_POLL: Record<string, (s: IctJobSettings) => string> = {
  "business-validation": (s) => `Cada ${s.businessPollSeconds} s`,
  "external-validation": (s) => `Cada ${s.externalPollSeconds} s`,
  orchestrator: (s) => `Cada ${s.orchestratorPollSeconds} s`,
  "send-to-core-api": (s) => `Cada ${s.sendPollSeconds} s`,
  "webhook-notification": (s) => `Cada ${s.webhookPollSeconds} s`,
};

export function composeUnifiedJobs(
  ict: IctJobCatalogItem[],
  settings: IctJobSettings | null,
  quipux: QuipuxSettings | null,
): UnifiedJobRow[] {
  const ictRows: UnifiedJobRow[] = ict.map((job) => {
    const title = ICT_TITLE[job.key] ?? job.displayName;
    const last = formatLastRun(job.lastRun);
    return {
      key: `ict-${job.key}`,
      displayName: title,
      technicalName: job.displayName,
      module: "ICT",
      owner: job.owner,
      types: job.types,
      typeLabels: job.types.map((t) => TYPE_LABEL[t] ?? t),
      enabledLabel: "Activo",
      enabledTone: "success",
      intervalLabel:
        job.key === "retention"
          ? "Continua (24 h)"
          : settings
            ? (ICT_POLL[job.key]?.(settings) ?? "—")
            : "—",
      lastRunLabel: last.label,
      lastRunPrimary: last.primary,
      lastRunSecondary: last.secondary,
      lastRunTone: last.tone,
      notes: job.notes ?? undefined,
    };
  });

  const qxOn = Boolean(quipux?.enabled);
  const quipuxRows: UnifiedJobRow[] = [
    {
      key: "quipux-register",
      displayName: "Radicar en Quipux",
      technicalName: "RegisterProcessor",
      module: "Quipux",
      owner: "core-api",
      types: ["BD", "ENDPOINT_EXTERNO"],
      typeLabels: ["Base de datos", "API externa"],
      enabledLabel: qxOn ? "Encendido" : "Apagado",
      enabledTone: qxOn ? "success" : "neutral",
      intervalLabel: quipux ? `Cada ${quipux.registerIntervalMinutes} min` : "—",
      lastRunLabel: "Se consulta en Quipux",
      lastRunPrimary: "Se consulta en Quipux",
      lastRunSecondary: null,
      lastRunTone: "neutral",
    },
    {
      key: "quipux-status-poll",
      displayName: "Consultar estado Quipux",
      technicalName: "StatusPollProcessor",
      module: "Quipux",
      owner: "core-api",
      types: ["BD", "ENDPOINT_EXTERNO"],
      typeLabels: ["Base de datos", "API externa"],
      enabledLabel: qxOn ? "Encendido" : "Apagado",
      enabledTone: qxOn ? "success" : "neutral",
      intervalLabel: quipux ? `Cada ${quipux.pollIntervalMinutes} min` : "—",
      lastRunLabel: "Se consulta en Quipux",
      lastRunPrimary: "Se consulta en Quipux",
      lastRunSecondary: null,
      lastRunTone: "neutral",
    },
  ];

  return [...ictRows, ...quipuxRows];
}

function formatLastRun(
  run: IctJobCatalogItem["lastRun"],
): { label: string; primary: string; secondary: string | null; tone: StatusTone } {
  if (!run) {
    return {
      label: "Sin ejecución",
      primary: "Sin ejecución",
      secondary: null,
      tone: "neutral",
    };
  }

  const outcome = run.outcome.trim().toLowerCase();
  const primary =
    outcome === "ok" || outcome === "success" ? "Completado" : outcome === "error" || outcome === "failed" ? "Error" : run.outcome;
  const tone: StatusTone =
    primary === "Completado" ? "success" : primary === "Error" ? "danger" : "info";
  const when = formatFechaHora(run.startedAt);
  const secondary = `${when} · ${run.durationMs} ms`;
  return {
    label: `${primary} · ${secondary}`,
    primary,
    secondary,
    tone,
  };
}

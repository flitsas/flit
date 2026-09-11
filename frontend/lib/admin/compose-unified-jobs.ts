import type { IctJobCatalogItem } from "@/lib/api/admin-ict-job-catalog";
import type { IctJobSettings } from "@/lib/api/admin-ict-job-settings";
import type { QuipuxSettings } from "@/lib/api/admin-quipux-settings";

export interface UnifiedJobRow {
  key: string;
  displayName: string;
  owner: string;
  types: string[];
  enabledLabel: string;
  intervalLabel: string;
  lastRunLabel: string;
  href: string;
  notes?: string;
}

const ICT_POLL: Record<string, (s: IctJobSettings) => string> = {
  "business-validation": (s) => `${s.businessPollSeconds} s`,
  "external-validation": (s) => `${s.externalPollSeconds} s`,
  orchestrator: (s) => `${s.orchestratorPollSeconds} s`,
  "send-to-core-api": (s) => `${s.sendPollSeconds} s`,
  "webhook-notification": (s) => `${s.webhookPollSeconds} s`,
};

export function composeUnifiedJobs(
  ict: IctJobCatalogItem[],
  settings: IctJobSettings | null,
  quipux: QuipuxSettings | null,
): UnifiedJobRow[] {
  const ictRows: UnifiedJobRow[] = ict.map((job) => ({
    key: `ict-${job.key}`,
    displayName: job.displayName,
    owner: job.owner,
    types: job.types,
    enabledLabel: "Activo (pipeline)",
    intervalLabel:
      job.key === "retention"
        ? "Horas (appsettings, 24/7)"
        : settings
          ? (ICT_POLL[job.key]?.(settings) ?? "—")
          : "—",
    lastRunLabel: formatLastRun(job.lastRun),
    href: "/admin/jobs/ict",
    notes: job.notes ?? undefined,
  }));

  const qxEnabled = quipux?.enabled ? "Encendido" : "Apagado";
  const quipuxRows: UnifiedJobRow[] = [
    {
      key: "quipux-register",
      displayName: "RegisterProcessor",
      owner: "core-api",
      types: ["BD", "ENDPOINT_EXTERNO"],
      enabledLabel: qxEnabled,
      intervalLabel: quipux ? `${quipux.registerIntervalMinutes} min` : "—",
      lastRunLabel: "Ver /admin/quipux",
      href: "/admin/quipux",
    },
    {
      key: "quipux-status-poll",
      displayName: "StatusPollProcessor",
      owner: "core-api",
      types: ["BD", "ENDPOINT_EXTERNO"],
      enabledLabel: qxEnabled,
      intervalLabel: quipux ? `${quipux.pollIntervalMinutes} min` : "—",
      lastRunLabel: "Ver /admin/quipux",
      href: "/admin/quipux",
    },
  ];

  return [...ictRows, ...quipuxRows];
}

function formatLastRun(run: IctJobCatalogItem["lastRun"]): string {
  if (!run) return "Sin bitácora";
  const when = new Date(run.startedAt).toLocaleString("es-CO", { timeZone: "America/Bogota" });
  return `${run.outcome} · ${when} · ${run.durationMs} ms`;
}

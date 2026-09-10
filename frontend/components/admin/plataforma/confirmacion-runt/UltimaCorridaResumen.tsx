import type { RuntConfirmationRun } from "@/lib/api/admin-runt-confirmation";
import { formatFechaHora } from "@/lib/format/date";

export const PROVEEDOR_LABEL: Record<string, string> = {
  kyverum_runt: "Kyverum",
  verifik: "Verifik",
};

export const SKIPPED_LABEL: Record<string, string> = {
  disabled: "Saltada: la consulta programada estaba apagada",
  already_running: "Saltada: había una corrida en curso",
};

/** Duración legible entre inicio y fin; «en curso» si aún no terminó. */
export function duracionDe(run: Pick<RuntConfirmationRun, "startedAt" | "finishedAt">): string {
  if (!run.finishedAt) return "en curso";
  const ms = new Date(run.finishedAt).getTime() - new Date(run.startedAt).getTime();
  if (!Number.isFinite(ms) || ms < 0) return "—";
  const s = Math.round(ms / 1000);
  if (s < 60) return `${s} s`;
  const m = Math.floor(s / 60);
  return `${m} min ${s % 60} s`;
}

/**
 * Resumen de una corrida (HU #12279 AC5, HU #12311 AC2): fecha, proveedor, contadores por
 * veredicto, duración y llamadas al proveedor. Con `run` nulo dice «Sin corridas todavía».
 */
export function UltimaCorridaResumen({ run, titulo }: { run: RuntConfirmationRun | null; titulo?: string }) {
  if (!run) {
    return (
      <p className="rounded-2xl border border-dashed border-[#DFE5ED] px-4 py-6 text-center text-sm text-[#59677D] dark:border-white/10 dark:text-white/60" data-testid="confirmacion-runt-sin-corridas">
        Sin corridas todavía
      </p>
    );
  }

  const celdas: Array<{ label: string; value: string }> = [
    { label: "Fecha y hora", value: formatFechaHora(run.startedAt) },
    { label: "Origen", value: run.trigger === "manual" ? "Manual (Consultar ahora)" : "Programada" },
    { label: "Proveedor", value: run.providerKey ? (PROVEEDOR_LABEL[run.providerKey] ?? run.providerKey) : "—" },
    { label: "Consultados", value: String(run.consulted) },
    { label: "Confirmados", value: String(run.confirmed) },
    { label: "Pendientes", value: String(run.pending) },
    { label: "Discrepancias", value: String(run.discrepancies) },
    { label: "No verificables", value: String(run.unverifiable) },
    { label: "Errores", value: String(run.errors) },
    { label: "Duración", value: duracionDe(run) },
    { label: "Llamadas al proveedor", value: String(run.providerCalls) },
  ];

  return (
    <div className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]" data-testid="confirmacion-runt-ultima-corrida">
      {titulo ? <p className="mb-3 text-xs font-semibold text-[#162744] dark:text-white">{titulo}</p> : null}
      {run.skippedReason ? (
        <p className="mb-3 text-xs font-semibold text-[#B33600] dark:text-[#FF8A5B]">{SKIPPED_LABEL[run.skippedReason] ?? run.skippedReason}</p>
      ) : null}
      {run.errorMessage ? (
        <p className="mb-3 text-xs text-[#B33600] dark:text-[#FF8A5B]">{run.errorMessage}</p>
      ) : null}
      <dl className="grid grid-cols-2 gap-x-4 gap-y-3 sm:grid-cols-3 lg:grid-cols-4">
        {celdas.map((c) => (
          <div key={c.label} className="flex flex-col">
            <dt className="text-[10px] font-semibold uppercase tracking-wide text-[#59677D] dark:text-white/55">{c.label}</dt>
            <dd className="text-sm font-semibold tabular-nums text-[#162744] dark:text-white">{c.value}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}

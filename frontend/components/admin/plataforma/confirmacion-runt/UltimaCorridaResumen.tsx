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

/** Tarjetas de resumen (patrón del artifact): la cifra manda, con su tono semántico. */
const STAT_TONE = {
  neutral: "text-[#162744] dark:text-white",
  ok: "text-[#15803D]",
  warn: "text-[#C2410C]",
  bad: "text-[#991B1B]",
  mute: "text-[#64748B]",
} as const;

/**
 * Resumen de una corrida (HU #12279 AC5, HU #12311 AC2): una tarjeta por cifra —consultados y los
 * cuatro veredictos— y una línea con duración, llamadas y errores. Con `run` nulo dice «Sin
 * corridas todavía».
 */
export function UltimaCorridaResumen({ run, titulo }: { run: RuntConfirmationRun | null; titulo?: string }) {
  if (!run) {
    return (
      <p
        className="rounded-2xl border border-dashed border-[#DFE5ED] px-4 py-6 text-center text-sm text-[#59677D] dark:border-white/10 dark:text-white/60"
        data-testid="confirmacion-runt-sin-corridas"
      >
        Sin corridas todavía
      </p>
    );
  }

  const stats: Array<{ label: string; value: string; tone: keyof typeof STAT_TONE; small?: string }> = [
    {
      label: "Ejecutada",
      value: formatFechaHora(run.startedAt),
      tone: "neutral",
      small: `${run.trigger === "manual" ? "Manual" : "Programada"} · ${run.providerKey ? (PROVEEDOR_LABEL[run.providerKey] ?? run.providerKey) : "—"}`,
    },
    { label: "Trámites consultados", value: String(run.consulted), tone: "neutral" },
    { label: "Confirmados", value: String(run.confirmed), tone: "ok" },
    { label: "Pendientes", value: String(run.pending), tone: "warn" },
    { label: "Discrepancias", value: String(run.discrepancies), tone: "bad" },
    { label: "No verificables", value: String(run.unverifiable), tone: "mute" },
  ];

  return (
    <div className="flex flex-col gap-2" data-testid="confirmacion-runt-ultima-corrida">
      {titulo ? <p className="text-xs font-semibold text-[#162744] dark:text-white">{titulo}</p> : null}
      {run.skippedReason ? (
        <p className="rounded-lg bg-[#FF4E00]/10 px-3 py-2 text-xs font-semibold text-[#B33600] dark:text-[#FF8A5B]">
          {SKIPPED_LABEL[run.skippedReason] ?? run.skippedReason}
        </p>
      ) : null}
      {run.errorMessage ? (
        <p className="rounded-lg bg-[#991B1B]/10 px-3 py-2 text-xs text-[#991B1B]">{run.errorMessage}</p>
      ) : null}
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        {stats.map((s) => (
          <div key={s.label} className="flex flex-col gap-0.5 rounded-xl border border-[#DFE5ED] bg-white px-3.5 py-3 dark:border-white/10 dark:bg-[#0B0F14]">
            <dt className="text-[10px] font-semibold uppercase tracking-wide text-[#59677D] dark:text-white/55">{s.label}</dt>
            <dd className={`${s.small ? "text-sm" : "text-2xl"} font-semibold leading-tight tabular-nums ${STAT_TONE[s.tone]}`}>
              {s.value}
              {s.small ? <span className="block text-[11px] font-medium text-[#59677D] dark:text-white/60">{s.small}</span> : null}
            </dd>
          </div>
        ))}
      </dl>
      <p className="text-[12px] text-[#59677D] dark:text-white/60">
        Duración {duracionDe(run)} · {run.providerCalls} {run.providerCalls === 1 ? "llamada" : "llamadas"} al proveedor · {run.errors}{" "}
        {run.errors === 1 ? "error" : "errores"} de proveedor
      </p>
    </div>
  );
}

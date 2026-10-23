"use client";

import { AlertCircle, CheckCircle2, ExternalLink } from "lucide-react";
import type { DrFlitSupportCaseCreated as CaseCreated } from "./dr-flit-chat-types";

/**
 * HU #12930 AC1/AC2/AC4 — caso radicado. El número de caso siempre; el enlace al work item solo si
 * el backend lo envió (lo hace únicamente para SuperAdmin); y cuántos adjuntos no se pudieron incluir.
 */
export function DrFlitSupportCaseCreated({
  result,
  onOpen,
}: {
  result: CaseCreated;
  onOpen: (href: string) => void;
}) {
  return (
    <section
      aria-label="Caso de soporte radicado"
      className="space-y-2 rounded-[var(--dr-flit-radius-card)] border p-4"
      style={{
        borderColor: "var(--dr-flit-border)",
        background: "var(--dr-flit-card-bg)",
        boxShadow: "var(--dr-flit-shadow-card)",
      }}
    >
      <p className="flex items-center gap-2 text-sm font-semibold" style={{ color: "var(--dr-flit-text)" }}>
        <CheckCircle2 className="h-5 w-5 shrink-0" style={{ color: "var(--dr-flit-success)" }} aria-hidden="true" />
        Tu caso #{result.caseId} quedó radicado
      </p>
      {result.attachmentsFailed > 0 ? (
        <p className="flex items-center gap-2 text-xs" style={{ color: "var(--dr-flit-text)" }}>
          <AlertCircle className="h-4 w-4 shrink-0" style={{ color: "var(--dr-flit-accent)" }} aria-hidden="true" />
          {result.attachmentsFailed === 1
            ? "1 adjunto no se pudo incluir en el caso."
            : `${result.attachmentsFailed} adjuntos no se pudieron incluir en el caso.`}{" "}
          Si son importantes, envíalos respondiendo al correo de soporte.
        </p>
      ) : null}
      {result.caseUrl ? (
        <button
          type="button"
          onClick={() => onOpen(result.caseUrl as string)}
          className="inline-flex items-center gap-1.5 rounded-full px-1 py-1 text-xs font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)]"
          style={{ color: "var(--dr-flit-brand-title)" }}
        >
          Abrir el caso en Azure DevOps
          <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
      ) : null}
    </section>
  );
}

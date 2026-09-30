"use client";

import type { DrFlitSupportCaseDraft } from "./dr-flit-chat-types";
import { DR_FLIT_FREQUENCY_LABELS } from "./dr-flit-support-case";

/**
 * HU #12930 AC1 — resumen de lo que se va a enviar a soporte. «Confirmar y radicar caso» es el único
 * control que radica: es el guardarraíl de confirmación explícita (ADR-0060 §8.2.3).
 */
export function DrFlitSupportCaseConfirm({
  draft,
  submitting,
  onConfirm,
  onEdit,
}: {
  draft: DrFlitSupportCaseDraft;
  submitting: boolean;
  onConfirm: () => void;
  onEdit: () => void;
}) {
  const rows: [string, string][] = [
    ["Título", draft.titulo],
    ["Detalle del error", draft.detalle],
    ["Resultado esperado", draft.resultadoEsperado],
    ["Frecuencia", draft.frecuencia ? DR_FLIT_FREQUENCY_LABELS[draft.frecuencia] : ""],
    ["Prioridad", draft.prioridad],
    ["Nombre", draft.nombre],
    ["Correo", draft.email],
    ["Teléfono", draft.telefono.trim() || "No indicado"],
    ["Compañía", draft.compania],
    ["Fecha", draft.fecha],
    [
      "Adjuntos",
      draft.adjuntar && draft.attachments.length > 0
        ? draft.attachments.map((a) => a.filename).join(", ")
        : "Sin adjuntos",
    ],
  ];

  return (
    <section
      aria-label="Resumen del caso de soporte"
      className="space-y-3 rounded-[var(--dr-flit-radius-card)] border p-4"
      style={{
        borderColor: "var(--dr-flit-border)",
        background: "var(--dr-flit-card-bg)",
        boxShadow: "var(--dr-flit-shadow-card)",
      }}
    >
      <p className="text-xs font-semibold uppercase tracking-[0.12em]" style={{ color: "var(--dr-flit-text-muted)" }}>
        Revisa antes de radicar
      </p>
      <dl className="m-0 space-y-2">
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt className="text-xs font-semibold" style={{ color: "var(--dr-flit-text-secondary)" }}>
              {label}
            </dt>
            <dd className="m-0 whitespace-pre-wrap break-words text-sm" style={{ color: "var(--dr-flit-text)" }}>
              {value}
            </dd>
          </div>
        ))}
      </dl>
      <p className="text-xs" style={{ color: "var(--dr-flit-text-secondary)" }}>
        Al confirmar, estos datos se envían al equipo de soporte de FLIT para atender tu caso.
      </p>
      <div className="flex flex-col gap-2">
        <button
          type="button"
          onClick={onConfirm}
          disabled={submitting}
          className="w-full rounded-full px-4 py-3 text-sm font-semibold text-white transition-opacity hover:opacity-95 disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
          style={{ background: "var(--dr-flit-gradient-primary)" }}
        >
          {submitting ? "Radicando…" : "Confirmar y radicar caso"}
        </button>
        <button
          type="button"
          onClick={onEdit}
          disabled={submitting}
          className="w-full rounded-full border px-4 py-2.5 text-sm font-semibold disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
          style={{ borderColor: "var(--dr-flit-brand)", color: "var(--dr-flit-brand)", background: "var(--dr-flit-card-bg)" }}
        >
          Editar el caso
        </button>
      </div>
    </section>
  );
}

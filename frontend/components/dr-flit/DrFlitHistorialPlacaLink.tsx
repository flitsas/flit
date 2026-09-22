"use client";

import { History } from "lucide-react";

/** HU-C — atajo al módulo «Historial por placa» (HU #12194) con la placa ya consultada. */
export function DrFlitHistorialPlacaLink({
  href,
  onOpen,
}: {
  href: string;
  onOpen: (href: string) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => onOpen(href)}
      className="flex w-full items-center gap-3 rounded-[var(--dr-flit-radius-card)] border p-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
      style={{
        borderColor: "var(--dr-flit-border)",
        background: "var(--dr-flit-card-bg)",
        boxShadow: "var(--dr-flit-shadow-card)",
      }}
    >
      <span
        className="grid h-10 w-10 shrink-0 place-items-center rounded-full"
        style={{ background: "var(--dr-flit-icon-tint)" }}
        aria-hidden="true"
      >
        <History className="h-5 w-5" style={{ color: "var(--dr-flit-brand-blue)" }} />
      </span>
      <span className="min-w-0 flex-1">
        <span
          className="block text-sm font-semibold"
          style={{ color: "var(--dr-flit-brand-title)" }}
        >
          Ver historial completo de la placa
        </span>
        <span
          className="mt-0.5 block text-xs"
          style={{ color: "var(--dr-flit-text-secondary)" }}
        >
          Todos los trámites de esta placa en orden cronológico
        </span>
      </span>
    </button>
  );
}

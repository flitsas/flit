"use client";

import { ExternalLink, ShieldCheck } from "lucide-react";
import type { DrFlitValidacionResult } from "./dr-flit-types";

export function DrFlitValidacionResults({
  results,
  onOpen,
}: {
  results: DrFlitValidacionResult[];
  onOpen: (href: string) => void;
}) {
  if (results.length === 0) {
    return null;
  }

  return (
    <ul
      className="m-0 flex list-none flex-col gap-2.5 p-0"
      aria-label="Resultados de validaciones"
    >
      {results.map((row) => (
        <li
          key={row.id}
          className="rounded-[var(--dr-flit-radius-card)] border p-3"
          style={{
            borderColor: "var(--dr-flit-border)",
            background: "var(--dr-flit-card-bg)",
            boxShadow: "var(--dr-flit-shadow-card)",
          }}
        >
          <div className="mb-2 flex items-start gap-2">
            <span
              className="grid h-8 w-8 shrink-0 place-items-center rounded-full"
              style={{ background: "var(--dr-flit-icon-tint)" }}
              aria-hidden="true"
            >
              <ShieldCheck
                className="h-4 w-4"
                style={{ color: "var(--dr-flit-brand-blue)" }}
              />
            </span>
            <div className="min-w-0 flex-1">
              <p
                className="truncate text-sm font-semibold"
                style={{ color: "var(--dr-flit-brand-title)" }}
              >
                {row.name || "Sin nombre"}
              </p>
              <p
                className="mt-0.5 text-xs"
                style={{ color: "var(--dr-flit-text-secondary)" }}
              >
                {row.documentType} {row.documentNumber} · {row.status}
              </p>
              <p
                className="mt-0.5 text-[11px]"
                style={{ color: "var(--dr-flit-text-muted)" }}
              >
                {row.createdAt}
              </p>
            </div>
          </div>

          <div className="flex flex-col gap-2">
            <button
              type="button"
              onClick={() => onOpen(row.href)}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-full px-3 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)]"
              style={{ background: "var(--dr-flit-gradient-primary)" }}
            >
              Ir a Validaciones
              <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
            {row.tramiteHref && (
              <button
                type="button"
                onClick={() => onOpen(row.tramiteHref!)}
                className="inline-flex w-full items-center justify-center gap-1.5 rounded-full border px-3 py-2 text-xs font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)]"
                style={{
                  borderColor: "var(--dr-flit-brand)",
                  color: "var(--dr-flit-brand)",
                  background: "var(--dr-flit-card-bg)",
                }}
              >
                Ver trámite ligado
              </button>
            )}
          </div>
        </li>
      ))}
    </ul>
  );
}

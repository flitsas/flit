"use client";

import { ExternalLink } from "lucide-react";
import { estadoChipStyle, estadoLabel } from "@/lib/tramites/estados";
import type { DrFlitTramiteResult } from "./dr-flit-types";

export function DrFlitTramiteResults({
  results,
  onOpen,
}: {
  results: DrFlitTramiteResult[];
  onOpen: (href: string) => void;
}) {
  if (results.length === 0) {
    return (
      <p className="text-sm" style={{ color: "var(--dr-flit-text-secondary)" }}>
        Sin trámites en el alcance de tu usuario.
      </p>
    );
  }

  return (
    <ul
      className="m-0 flex list-none flex-col gap-2.5 p-0"
      aria-label="Resultados de trámites"
    >
      {results.map((row) => {
        const chip = estadoChipStyle(row.estado);
        const placa = (row.placa || "—").toUpperCase();
        return (
          <li
            key={row.id}
            className="rounded-[var(--dr-flit-radius-card)] border p-3"
            style={{
              borderColor: "var(--dr-flit-border)",
              background: "var(--dr-flit-card-bg)",
              boxShadow: "var(--dr-flit-shadow-card)",
            }}
          >
            <div className="mb-2 flex items-start justify-between gap-2">
              <div className="min-w-0">
                <p
                  className="truncate text-sm font-semibold"
                  style={{ color: "var(--dr-flit-brand-title)" }}
                >
                  {row.tipoTramite}
                </p>
                <p
                  className="mt-0.5 font-mono text-[11px]"
                  style={{ color: "var(--dr-flit-text-secondary)" }}
                  title={row.id}
                >
                  ID {row.id.slice(0, 8)}…
                </p>
              </div>
              <span
                className="shrink-0 rounded-full border px-2 py-0.5 text-[10px] font-semibold"
                style={{
                  background: chip.bg,
                  color: chip.color,
                  borderColor: chip.border,
                }}
              >
                {estadoLabel(row.estado)}
              </span>
            </div>

            <dl className="m-0 grid grid-cols-2 gap-x-3 gap-y-1.5 text-xs">
              <div>
                <dt
                  className="font-medium"
                  style={{ color: "var(--dr-flit-text-secondary)" }}
                >
                  Fecha
                </dt>
                <dd
                  className="m-0 font-medium"
                  style={{ color: "var(--dr-flit-text)" }}
                >
                  {row.fecha}
                </dd>
              </div>
              <div>
                <dt
                  className="font-medium"
                  style={{ color: "var(--dr-flit-text-secondary)" }}
                >
                  Placa
                </dt>
                <dd
                  className="m-0 font-semibold uppercase tracking-[0.18em]"
                  style={{ color: "var(--dr-flit-text)" }}
                >
                  {placa === "—" ? "—" : placa.split("").join(" ")}
                </dd>
              </div>
              <div className="col-span-2">
                <dt
                  className="font-medium"
                  style={{ color: "var(--dr-flit-text-secondary)" }}
                >
                  VIN
                </dt>
                <dd
                  className="m-0 truncate font-mono text-[11px]"
                  style={{ color: "var(--dr-flit-text)" }}
                  title={row.vin}
                >
                  {row.vin}
                </dd>
              </div>
            </dl>

            <button
              type="button"
              onClick={() => onOpen(row.href)}
              className="mt-3 inline-flex w-full items-center justify-center gap-1.5 rounded-full px-3 py-2.5 text-xs font-semibold text-white transition-opacity hover:opacity-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
              style={{
                background: "var(--dr-flit-gradient-primary)",
                boxShadow: "var(--dr-flit-shadow-fab)",
              }}
            >
              Ver trámite
              <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          </li>
        );
      })}
    </ul>
  );
}

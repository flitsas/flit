"use client";

import { ArrowUpRight, FileStack, ShieldCheck } from "lucide-react";
import {
  DR_FLIT_CLIENT_BRANCHES,
  type DrFlitClientBranch,
} from "./dr-flit-intents";

const ICONS = {
  tramites: FileStack,
  validaciones: ShieldCheck,
} as const;

export function DrFlitClientBranchChoices({
  onSelect,
  disabled,
}: {
  onSelect: (branch: DrFlitClientBranch) => void;
  disabled?: boolean;
}) {
  return (
    <div className="flex flex-col gap-2" aria-label="Opciones por cliente">
      <p
        className="text-[11px] font-semibold uppercase tracking-[0.14em]"
        style={{ color: "var(--dr-flit-text-muted)" }}
      >
        Elige una opción
      </p>
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {DR_FLIT_CLIENT_BRANCHES.map((branch) => {
          const Icon = ICONS[branch.id];
          return (
            <li key={branch.id}>
              <button
                type="button"
                disabled={disabled}
                onClick={() => onSelect(branch.id)}
                className="flex w-full items-center gap-2.5 rounded-full border px-3.5 py-2.5 text-left text-sm font-medium transition-colors disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
                style={{
                  borderColor: "var(--dr-flit-border)",
                  color: "var(--dr-flit-text)",
                  background: "var(--dr-flit-card-bg)",
                  boxShadow: "var(--dr-flit-shadow-card)",
                }}
                onMouseEnter={(e) => {
                  e.currentTarget.style.background = "var(--dr-flit-bubble)";
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.background = "var(--dr-flit-card-bg)";
                }}
              >
                <span
                  className="grid h-8 w-8 shrink-0 place-items-center rounded-full"
                  style={{ background: "var(--dr-flit-icon-tint)" }}
                  aria-hidden="true"
                >
                  <Icon
                    className="h-4 w-4"
                    style={{ color: "var(--dr-flit-brand-blue)" }}
                  />
                </span>
                <span className="flex-1">{branch.label}</span>
                <ArrowUpRight
                  className="h-4 w-4 shrink-0"
                  style={{ color: "var(--dr-flit-brand-blue)" }}
                  aria-hidden="true"
                />
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

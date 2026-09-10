"use client";

import { ArrowUpRight, BookOpen, Headphones } from "lucide-react";
import { DR_FLIT_HELP_OPTIONS, type DrFlitHelpOptionId } from "./dr-flit-intents";

const ICONS: Record<DrFlitHelpOptionId, typeof BookOpen> = {
  "necesito-ayuda": BookOpen,
  soporte: Headphones,
};

export function DrFlitHelpOptions({
  onSelect,
  disabled,
}: {
  onSelect: (id: DrFlitHelpOptionId) => void;
  disabled?: boolean;
}) {
  return (
    <div className="flex flex-col gap-2" aria-label="Ayuda">
      <p
        className="text-[11px] font-semibold uppercase tracking-[0.14em]"
        style={{ color: "var(--dr-flit-text-muted)" }}
      >
        Ayuda
      </p>
      <ul className="flex flex-col gap-2 list-none m-0 p-0">
        {DR_FLIT_HELP_OPTIONS.map((option) => {
          const Icon = ICONS[option.id];
          return (
            <li key={option.id}>
              <button
                type="button"
                disabled={disabled}
                onClick={() => onSelect(option.id)}
                className="flex w-full items-center gap-3 rounded-[var(--dr-flit-radius-card)] border p-3 text-left transition-colors disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
                style={{
                  borderColor: "var(--dr-flit-border)",
                  background: "var(--dr-flit-card-bg)",
                  boxShadow: "var(--dr-flit-shadow-card)",
                }}
              >
                <span
                  className="grid h-10 w-10 shrink-0 place-items-center rounded-full"
                  style={{ background: "var(--dr-flit-icon-tint)" }}
                  aria-hidden
                >
                  <Icon
                    className="h-5 w-5"
                    style={{ color: "var(--dr-flit-brand-blue)" }}
                  />
                </span>
                <span className="min-w-0 flex-1">
                  <span
                    className="flex items-center justify-between gap-2 text-sm font-semibold"
                    style={{ color: "var(--dr-flit-brand-title)" }}
                  >
                    {option.label}
                    <ArrowUpRight
                      className="h-4 w-4 shrink-0"
                      style={{ color: "var(--dr-flit-brand-blue)" }}
                      aria-hidden
                    />
                  </span>
                  {option.description && (
                    <span
                      className="mt-0.5 block text-xs"
                      style={{ color: "var(--dr-flit-text-secondary)" }}
                    >
                      {option.description}
                    </span>
                  )}
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

"use client";

import { ArrowUpRight } from "lucide-react";
import { DR_FLIT_GESTION_INTENTS, type DrFlitIntentId } from "./dr-flit-intents";

export function DrFlitSuggestions({
  onSelect,
  disabled,
}: {
  onSelect: (id: DrFlitIntentId) => void;
  disabled?: boolean;
}) {
  return (
    <div className="flex flex-col gap-2" aria-label="Gestión">
      <p
        className="text-[11px] font-semibold uppercase tracking-[0.14em]"
        style={{ color: "var(--dr-flit-text-muted)" }}
      >
        Gestión
      </p>
      <ul className="flex flex-col gap-2 list-none m-0 p-0">
        {DR_FLIT_GESTION_INTENTS.map((intent) => (
          <li key={intent.id}>
            <button
              type="button"
              disabled={disabled}
              onClick={() => onSelect(intent.id)}
              className="w-full flex items-center justify-between gap-2 rounded-full border px-3.5 py-2.5 text-left text-sm font-medium transition-colors disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
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
              <span>{intent.label}</span>
              <ArrowUpRight
                className="h-4 w-4 shrink-0"
                style={{ color: "var(--dr-flit-brand-blue)" }}
                aria-hidden="true"
              />
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

"use client";

import { Search } from "lucide-react";
import { DR_FLIT_GESTION_INTENTS, gestionChipLabel, type DrFlitIntentId } from "./dr-flit-intents";

export function DrFlitSuggestions({
  onSelect,
  disabled,
}: {
  onSelect: (id: DrFlitIntentId) => void;
  disabled?: boolean;
}) {
  // Chips compactos en una fila: Ayuda y la caja para escribir quedan a la vista sin desplazarse.
  // El nombre accesible conserva la acción completa («Buscar por placa»).
  return (
    <div className="flex flex-col gap-2" aria-label="Gestión">
      <p
        className="text-[11px] font-semibold uppercase tracking-[0.14em]"
        style={{ color: "var(--dr-flit-text-muted)" }}
      >
        Gestión · buscar por
      </p>
      <ul className="m-0 flex list-none flex-wrap gap-2 p-0">
        {DR_FLIT_GESTION_INTENTS.map((intent) => (
          <li key={intent.id}>
            <button
              type="button"
              disabled={disabled}
              onClick={() => onSelect(intent.id)}
              aria-label={intent.label}
              className="flex items-center gap-1.5 rounded-full border px-3.5 py-2 text-sm font-medium transition-colors disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
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
              <Search
                className="h-3.5 w-3.5 shrink-0"
                style={{ color: "var(--dr-flit-brand-blue)" }}
                aria-hidden="true"
              />
              {gestionChipLabel(intent)}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

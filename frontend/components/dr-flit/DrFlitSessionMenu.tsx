"use client";

import type { DrFlitHelpOptionId, DrFlitIntentId } from "./dr-flit-intents";
import { DrFlitHelpOptions } from "./DrFlitHelpOptions";
import { DrFlitSuggestions } from "./DrFlitSuggestions";

export function DrFlitSessionMenu({
  onSelectIntent,
  onSelectHelpOption,
  disabled,
}: {
  onSelectIntent: (id: DrFlitIntentId) => void;
  onSelectHelpOption: (id: DrFlitHelpOptionId) => void;
  disabled?: boolean;
}) {
  return (
    <div className="flex flex-col gap-5 pt-1" aria-label="Sesiones del chat">
      <DrFlitSuggestions onSelect={onSelectIntent} disabled={disabled} />
      <DrFlitHelpOptions onSelect={onSelectHelpOption} disabled={disabled} />
    </div>
  );
}

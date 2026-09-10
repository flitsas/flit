"use client";

import { RotateCcw } from "lucide-react";
import { DR_FLIT_BACK_LABEL } from "./dr-flit-intents";

export function DrFlitBackToSearch({
  onBack,
  disabled,
}: {
  onBack: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onBack}
      disabled={disabled}
      className="inline-flex w-full items-center justify-center gap-2 rounded-full border px-3.5 py-2.5 text-sm font-semibold transition-colors disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
      style={{
        borderColor: "var(--dr-flit-brand)",
        color: "var(--dr-flit-brand)",
        background: "var(--dr-flit-card-bg)",
      }}
    >
      <RotateCcw className="h-4 w-4" aria-hidden="true" />
      {DR_FLIT_BACK_LABEL}
    </button>
  );
}

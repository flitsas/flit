"use client";

import { MessageCircleOff } from "lucide-react";

export function DrFlitEndChatButton({
  onEnd,
  disabled,
}: {
  onEnd: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onEnd}
      disabled={disabled}
      aria-label="Terminar chat y borrar la conversación"
      className="inline-flex w-full items-center justify-center gap-2 rounded-full px-3.5 py-2.5 text-sm font-semibold text-white transition-opacity disabled:opacity-50 hover:opacity-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
      style={{
        background: "var(--dr-flit-accent)",
        boxShadow: "var(--dr-flit-shadow-card)",
      }}
    >
      <MessageCircleOff className="h-4 w-4 shrink-0" aria-hidden="true" />
      Terminar chat
    </button>
  );
}

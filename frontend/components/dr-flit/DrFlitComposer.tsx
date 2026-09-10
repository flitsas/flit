"use client";

import { Send } from "lucide-react";
import { FormEvent, type RefObject, useState } from "react";

export function DrFlitComposer({
  onSend,
  inputRef,
  disabled,
}: {
  onSend: (text: string) => void;
  inputRef: RefObject<HTMLInputElement | null>;
  disabled?: boolean;
}) {
  const [value, setValue] = useState("");

  const submit = (e?: FormEvent) => {
    e?.preventDefault();
    const text = value.trim();
    if (!text || disabled) return;
    onSend(text);
    setValue("");
  };

  return (
    <form
      onSubmit={submit}
      className="flex items-center gap-2 border-t px-3 py-3"
      style={{
        borderColor: "var(--dr-flit-border)",
        background: "var(--dr-flit-card-bg)",
      }}
    >
      <label htmlFor="dr-flit-input" className="sr-only">
        Pregúntale a DR. FLIT
      </label>
      <input
        id="dr-flit-input"
        ref={inputRef}
        type="text"
        value={value}
        disabled={disabled}
        onChange={(e) => setValue(e.target.value)}
        placeholder="Pregúntale a DR. FLIT..."
        autoComplete="off"
        className="flex-1 min-w-0 rounded-[10px] border px-4 py-2.5 text-sm outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] disabled:opacity-50"
        style={{
          borderColor: "var(--dr-flit-border-input)",
          background: "var(--dr-flit-card-bg)",
          color: "var(--dr-flit-text)",
          height: "48px",
        }}
      />
      <button
        type="submit"
        disabled={disabled || !value.trim()}
        aria-label="Enviar mensaje"
        className="h-10 w-10 shrink-0 rounded-full grid place-items-center text-white transition-opacity disabled:opacity-40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
        style={{
          background: "var(--dr-flit-gradient-primary)",
          boxShadow: "var(--dr-flit-shadow-fab)",
        }}
      >
        <Send className="h-4 w-4" aria-hidden="true" />
      </button>
    </form>
  );
}

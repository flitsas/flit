"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { cn } from "@/lib/utils";
import { isValidEmail, MAX_RECIPIENTS } from "./labels";

interface RecipientsInputProps {
  value: string[];
  onChange: (recipients: string[]) => void;
  /** Error externo (validación al enviar). */
  error?: string | null;
  disabled?: boolean;
}

/**
 * Chips de destinatarios de correo (Reportes 2.0, HU-D). Enter/coma/blur agregan el chip;
 * valida el formato al agregar y muestra el error en español sin bloquear la edición.
 */
export function RecipientsInput({ value, onChange, error, disabled }: RecipientsInputProps) {
  const [draft, setDraft] = useState("");
  const [localError, setLocalError] = useState<string | null>(null);

  function tryAdd(raw: string) {
    const email = raw.trim().replace(/,$/, "");
    if (!email) return;
    if (!isValidEmail(email)) {
      setLocalError(`El correo '${email}' no es una dirección válida.`);
      return;
    }
    if (value.some((r) => r.toLowerCase() === email.toLowerCase())) {
      setDraft("");
      return;
    }
    if (value.length >= MAX_RECIPIENTS) {
      setLocalError(`No puede indicar más de ${MAX_RECIPIENTS} destinatarios.`);
      return;
    }
    setLocalError(null);
    onChange([...value, email]);
    setDraft("");
  }

  const message = localError ?? error ?? null;

  return (
    <div>
      <div
        className={cn(
          "flex flex-wrap items-center gap-1.5 rounded-xl border px-2 py-1.5 min-h-[42px]",
          "bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-700",
          message && "border-red-400 dark:border-red-500",
        )}
      >
        {value.map((email) => (
          <span
            key={email}
            data-testid="recipient-chip"
            className="flex items-center gap-1 rounded-full px-2.5 py-1 text-xs font-medium text-white"
            style={{ background: "#557EFF" }}
          >
            {email}
            {!disabled && (
              <button
                type="button"
                aria-label={`Quitar ${email}`}
                onClick={() => onChange(value.filter((r) => r !== email))}
                className="rounded-full hover:bg-white/20"
              >
                <X className="h-3 w-3" aria-hidden="true" />
              </button>
            )}
          </span>
        ))}
        <input
          type="text"
          value={draft}
          disabled={disabled}
          aria-label="Agregar destinatario"
          placeholder={value.length === 0 ? "correo@empresa.co" : ""}
          className="flex-1 min-w-[140px] bg-transparent text-sm outline-none py-1 text-[#162744] dark:text-slate-100 placeholder:text-slate-400"
          onChange={(e) => {
            setLocalError(null);
            if (e.target.value.endsWith(",")) {
              tryAdd(e.target.value);
            } else {
              setDraft(e.target.value);
            }
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              tryAdd(draft);
            } else if (e.key === "Backspace" && !draft && value.length > 0) {
              onChange(value.slice(0, -1));
            }
          }}
          onBlur={() => tryAdd(draft)}
        />
      </div>
      <p className="mt-1 text-[11px] text-slate-500 dark:text-slate-400">
        Presiona Enter o coma para agregar. Máximo {MAX_RECIPIENTS} destinatarios.
      </p>
      {message && (
        <p role="alert" data-testid="recipients-error" className="mt-1 text-xs font-medium text-red-600 dark:text-red-400">
          {message}
        </p>
      )}
    </div>
  );
}

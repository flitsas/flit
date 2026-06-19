"use client";

import { useMemo, useState } from "react";
import { Mail, Save } from "lucide-react";
import type { WhitelistEntry } from "@/lib/api/types";

// Gestión de la lista blanca de correos (HU #10194, AC3). Textarea multilínea
// (uno por línea o separados por coma) con validación inline por línea. La
// textarea NO se limpia hasta que el guardado es exitoso.
export interface WhitelistTextareaProps {
  /** Correos ya exentos (solo lectura, contexto). */
  entries: WhitelistEntry[];
  /**
   * Persiste los correos válidos. Debe lanzar un error con `errors: [{ value, message }]`
   * (ApiValidationError) ante un 422 del backend.
   */
  onSave: (emails: string[]) => Promise<void>;
}

interface LineError {
  line: number;
  value: string;
  message: string;
}

// Regex pragmático (no RFC completo) para descartar entradas obviamente inválidas
// en cliente antes de llamar a la API.
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function WhitelistTextarea({ entries, onSave }: WhitelistTextareaProps) {
  const [text, setText] = useState("");
  const [errors, setErrors] = useState<LineError[]>([]);
  const [saving, setSaving] = useState(false);
  const [savedMessage, setSavedMessage] = useState<string | null>(null);

  const lines = useMemo(() => parseLines(text), [text]);

  const handleSave = async () => {
    setSavedMessage(null);

    if (lines.length === 0) {
      setErrors([{ line: 1, value: "", message: "Ingresa al menos un correo." }]);
      return;
    }

    // 1) Validación client-side por línea.
    const clientErrors: LineError[] = lines
      .filter((l) => !EMAIL_RE.test(l.value))
      .map((l) => ({ line: l.line, value: l.value, message: "Correo con formato inválido." }));

    if (clientErrors.length > 0) {
      setErrors(clientErrors); // textarea NO se limpia
      return;
    }

    // 2) Persistencia server-side.
    setSaving(true);
    try {
      await onSave(lines.map((l) => l.value));
      setErrors([]);
      setText(""); // limpiar solo en éxito
      setSavedMessage("Lista blanca actualizada correctamente.");
    } catch (error) {
      setErrors(mapServerErrors(error, lines)); // textarea NO se limpia
    } finally {
      setSaving(false);
    }
  };

  return (
    <section aria-label="Lista blanca de correos" className="space-y-3">
      <div className="flex items-center gap-2">
        <Mail className="h-4 w-4" style={{ color: "#557EFF" }} />
        <h4 className="text-sm font-semibold">Lista Blanca</h4>
      </div>
      <p className="text-[11px] opacity-70">
        Correos exentos de la regla de propiedad vehicular. Uno por línea o separados por coma.
      </p>

      <label htmlFor="whitelist-textarea" className="sr-only">
        Correos de la lista blanca
      </label>
      <textarea
        id="whitelist-textarea"
        value={text}
        onChange={(e) => setText(e.target.value)}
        rows={5}
        placeholder={"comprador@dominio.com\nradicador@dominio.com"}
        aria-invalid={errors.length > 0}
        className="w-full rounded-xl border bg-transparent px-3 py-2 font-mono text-xs outline-none focus:border-[#557EFF]"
        style={{ borderColor: errors.length > 0 ? "#FF4E00" : "#DFE5ED" }}
      />

      {errors.length > 0 && (
        <ul className="space-y-1" role="alert" data-testid="whitelist-errors">
          {errors.map((e, i) => (
            <li key={`${e.line}-${i}`} className="text-[11px] font-medium" style={{ color: "#FF4E00" }}>
              Línea {e.line}: {e.value ? `"${e.value}" — ` : ""}
              {e.message}
            </li>
          ))}
        </ul>
      )}

      {savedMessage && (
        <p className="text-[11px] font-medium" style={{ color: "#00DBD5" }} role="status">
          {savedMessage}
        </p>
      )}

      <button
        type="button"
        onClick={handleSave}
        disabled={saving}
        className="flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
        style={{ background: "#557EFF" }}
      >
        <Save className="h-3.5 w-3.5" /> {saving ? "Guardando…" : "Guardar lista blanca"}
      </button>

      {entries.length > 0 && (
        <div className="rounded-xl border p-3" style={{ borderColor: "#DFE5ED" }}>
          <p className="mb-2 text-[10px] font-semibold uppercase opacity-60">
            Correos exentos actuales ({entries.length})
          </p>
          <ul className="space-y-1">
            {entries.map((entry) => (
              <li key={entry.email} className="font-mono text-[11px] opacity-80">
                {entry.email}
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

interface ParsedLine {
  line: number;
  value: string;
}

function parseLines(text: string): ParsedLine[] {
  const result: ParsedLine[] = [];
  const rawLines = text.split(/\n/);

  rawLines.forEach((rawLine, index) => {
    const pieces = rawLine.split(",").map((p) => p.trim()).filter(Boolean);
    for (const piece of pieces) {
      result.push({ line: index + 1, value: piece });
    }
  });

  return result;
}

function mapServerErrors(error: unknown, lines: ParsedLine[]): LineError[] {
  const apiErrors = (error as { errors?: { value?: string | null; message: string }[] })?.errors;

  if (!Array.isArray(apiErrors) || apiErrors.length === 0) {
    return [{ line: 1, value: "", message: "No se pudo guardar la lista blanca. Intenta de nuevo." }];
  }

  return apiErrors.map((e) => {
    const match = lines.find((l) => l.value === e.value);
    return {
      line: match?.line ?? 1,
      value: e.value ?? "",
      message: e.message,
    };
  });
}

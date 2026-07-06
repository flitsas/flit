"use client";

import { CheckCircle2 } from "lucide-react";
import type { ConfigChangeGroup, ConfigChangeItem } from "./settingsForm";

const TONE_COLORS: Record<ConfigChangeItem["tone"], string> = {
  on: "#0a8f8b",
  off: "#FF4E00",
  neutral: "#557EFF",
};

// Ventana emergente de confirmación de "Guardar todo" (HU #10194, AC2). Detecta los cambios
// realizados en las pestañas de configuración y pide confirmar antes de persistirlos (un único
// PUT atómico). Tras confirmar muestra el resultado en la misma ventana (fase "success"), en
// lugar de un banner que quede fijo en la vista.
export type SaveConfigPhase = "confirm" | "saving" | "success";

export interface SaveConfigDialogProps {
  /** Cambios detectados, agrupados por módulo. Vacío = sin cambios. */
  changes: ConfigChangeGroup[];
  phase: SaveConfigPhase;
  /** Error genérico (no de validación) al guardar; se muestra en la fase de confirmación. */
  error?: string | null;
  onConfirm: () => void;
  onCancel: () => void;
  onClose: () => void;
}

export function SaveConfigDialog({
  changes,
  phase,
  error,
  onConfirm,
  onCancel,
  onClose,
}: SaveConfigDialogProps) {
  const hasChanges = changes.length > 0;
  const saving = phase === "saving";

  return (
    <div
      className="fixed inset-0 z-[95] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="save-config-title"
    >
      <div className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]">
        {phase === "success" ? (
          <>
            <div className="flex items-center gap-2">
              <CheckCircle2 className="h-5 w-5 shrink-0" style={{ color: "#00DBD5" }} />
              <h2 id="save-config-title" className="text-lg font-semibold" style={{ color: "#162744" }}>
                Cambios guardados
              </h2>
            </div>
            <p className="mt-2 text-sm opacity-80">
              {hasChanges
                ? "Se guardaron los siguientes cambios de configuración:"
                : "La configuración se guardó correctamente."}
            </p>
            {hasChanges && <ChangeList changes={changes} />}
            <div className="mt-5 flex justify-end">
              <button
                type="button"
                onClick={onClose}
                className="rounded-xl px-5 py-2.5 text-sm font-semibold text-white"
                style={{ background: "#557EFF" }}
              >
                Cerrar
              </button>
            </div>
          </>
        ) : (
          <>
            <h2 id="save-config-title" className="text-lg font-semibold" style={{ color: "#162744" }}>
              Confirmar guardado
            </h2>
            <p className="mt-2 text-sm opacity-80">
              {hasChanges
                ? "Vas a guardar los siguientes cambios de configuración. ¿Confirmas?"
                : "No se detectaron cambios en la configuración. ¿Deseas guardar de todos modos?"}
            </p>
            {hasChanges && <ChangeList changes={changes} />}
            {error && (
              <p role="alert" className="mt-3 text-sm" style={{ color: "#FF4E00" }}>
                {error}
              </p>
            )}
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                onClick={onCancel}
                disabled={saving}
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
              >
                Cancelar
              </button>
              <button
                type="button"
                onClick={onConfirm}
                disabled={saving}
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
              >
                {saving ? "Guardando…" : "Guardar cambios"}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function ChangeList({ changes }: { changes: ConfigChangeGroup[] }) {
  return (
    <div className="mt-3 space-y-2.5 rounded-xl border p-3 text-xs">
      {changes.map((group) => (
        <div key={group.module}>
          <p className="font-semibold" style={{ color: "#557EFF" }}>
            {group.module}
          </p>
          <ul className="mt-1 space-y-1">
            {group.items.map((item) => (
              <li key={item.label} className="flex items-center justify-between gap-3">
                <span className="opacity-80">{item.label}</span>
                <span
                  className="shrink-0 rounded-md px-1.5 py-0.5 text-[10px] font-semibold"
                  style={{ color: TONE_COLORS[item.tone], background: `${TONE_COLORS[item.tone]}1a` }}
                >
                  {item.detail}
                </span>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  );
}

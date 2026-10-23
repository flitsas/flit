"use client";

import { MessageCircle } from "lucide-react";

/**
 * HU #12928 AC1 — aviso discreto de que quedan pocos mensajes con el asistente hoy. No interrumpe la
 * conversación: es una línea informativa (`role="status"`), no un diálogo. El conteo viene del backend.
 */
export function DrFlitUsageNotice({
  remaining,
  used,
  limit,
}: {
  remaining: number;
  used: number;
  limit: number;
}) {
  const quedan = remaining === 1 ? "Te queda 1 mensaje" : `Te quedan ${remaining} mensajes`;
  return (
    <p
      role="status"
      className="flex items-center gap-2 rounded-[var(--dr-flit-radius-card)] border px-3 py-2 text-xs"
      style={{
        borderColor: "var(--dr-flit-border)",
        background: "var(--dr-flit-card-bg)",
        color: "var(--dr-flit-text-secondary)",
      }}
    >
      <MessageCircle className="h-4 w-4 shrink-0" style={{ color: "var(--dr-flit-brand-blue)" }} aria-hidden="true" />
      <span>
        {quedan} con el asistente hoy ({used} de {limit}). El menú de Gestión y Ayuda sigue disponible sin límite.
      </span>
    </p>
  );
}

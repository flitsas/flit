"use client";

import { InlineAlert, Modal } from "@flit/ui";
import { apiFetch } from "@/lib/api";
import { formatoFechaHora } from "@/lib/format/date";
import { cambiosDeAuditoria } from "@/lib/parametrizador";
import type { AuditoriaConfiguracion } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const ACCION: Record<string, string> = { CREAR: "Creó", ACTUALIZAR: "Cambió", EDITAR: "Cambió", ACTIVAR: "Activó", DESACTIVAR: "Desactivó", PAUSAR: "Pausó" };

/**
 * Historial de cambios de una configuración global (scheduler, catálogo de tipologías…): quién, cuándo y de qué a qué.
 * Sale de la auditoría del SuperAdmin (GET /admin/auditoria?global=true).
 */
export function HistorialAuditoria({
  entidad,
  titulo,
  nombre,
  onCerrar,
  ruta,
  query,
}: {
  entidad: string;
  titulo: string;
  /** Por defecto la auditoría global del SuperAdmin; p. ej. el historial de un NIT usa /monitoreo/nits/{id}/historial. */
  ruta?: string;
  query?: Record<string, string | null>;
  /** Cómo se llama el registro afectado a partir de su id en la auditoría. */
  nombre: (entidadId: string) => string;
  onCerrar: () => void;
}) {
  const lista = useCarga(`auditoria:${entidad}:${ruta ?? ""}:${JSON.stringify(query ?? {})}`, (signal) =>
    apiFetch<{ items: AuditoriaConfiguracion[] }>(ruta ?? "/admin/auditoria", { signal, query: ruta ? (query ?? {}) : { entidad, global: true, tamano: 100 } }),
  );
  const items = lista.datos?.items ?? [];
  return (
    <Modal open onClose={onCerrar} title={titulo} size="lg">
      {lista.estado === "loading" && <p className="text-sm opacity-60">Cargando…</p>}
      {lista.estado === "error" && <InlineAlert tone="error">{lista.error}</InlineAlert>}
      {lista.estado === "ready" && items.length === 0 && <p className="text-sm opacity-70">Sin cambios registrados.</p>}
      <ol className="flex flex-col gap-3">
        {items.map((a) => (
          <li key={a.id} className="border-l-2 border-flit-brand pl-3 text-xs">
            <p className="font-semibold">
              {ACCION[a.accion] ? `${ACCION[a.accion]} · ` : ""}
              {nombre(a.entidadId)}
            </p>
            <p className="opacity-70">
              {formatoFechaHora(a.registradoEn)} · {a.usuarioNombre}
            </p>
            <ul className="mt-1">
              {cambiosDeAuditoria(a.cambios).map((c) => (
                <li key={c.campo}>
                  <span className="font-semibold">{c.campo}:</span> {c.anterior} → {c.nuevo}
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ol>
    </Modal>
  );
}

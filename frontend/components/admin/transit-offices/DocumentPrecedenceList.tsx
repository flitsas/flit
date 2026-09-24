"use client";

import { useEffect, useId, useState, type KeyboardEvent } from "react";
import { GripVertical } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import type { OtDocumentPrecedenceItem } from "@/lib/api/types-ot";

export interface DocumentPrecedenceListProps {
  items: OtDocumentPrecedenceItem[];
  onReorder: (items: OtDocumentPrecedenceItem[]) => Promise<void>;
  disabled?: boolean;
}

function reorderList(
  list: OtDocumentPrecedenceItem[],
  from: number,
  to: number,
): OtDocumentPrecedenceItem[] {
  if (from === to || from < 0 || to < 0 || from >= list.length || to >= list.length) {
    return list;
  }
  const next = [...list];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next.map((item, index) => ({ ...item, sort_order: index + 1 }));
}

/** Lista reordenable con DnD y teclado WCAG (HU #10224 AC2–AC3). */
export function DocumentPrecedenceList({
  items,
  onReorder,
  disabled = false,
}: DocumentPrecedenceListProps) {
  const listId = useId();
  const [order, setOrder] = useState(items);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  // HU #12883 AC2 — fila destino resaltada mientras se arrastra con mouse (dragover).
  const [dragOverIndex, setDragOverIndex] = useState<number | null>(null);
  const [keyboardIndex, setKeyboardIndex] = useState<number | null>(null);
  const [pendingKeyboard, setPendingKeyboard] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincronizar orden tras respuesta API
    setOrder(items);
  }, [items]);

  const commitOrder = async (next: OtDocumentPrecedenceItem[], previous: OtDocumentPrecedenceItem[]) => {
    setSaving(true);
    try {
      await onReorder(next);
    } catch {
      // HU #11185 AC5 — si el guardado falla, la lista no puede quedarse mostrando un orden que
      // el organismo cree guardado: vuelve al anterior. El aviso lo da la sección (toast).
      setOrder(previous);
    } finally {
      setSaving(false);
      setPendingKeyboard(false);
    }
  };

  const onDrop = (targetIndex: number) => {
    setDragOverIndex(null);
    if (dragIndex === null || disabled || saving) return;
    const previous = order;
    const next = reorderList(order, dragIndex, targetIndex);
    setOrder(next);
    setDragIndex(null);
    void commitOrder(next, previous);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (disabled || saving) return;

    if (keyboardIndex === null && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
      event.preventDefault();
      setKeyboardIndex(index);
      setPendingKeyboard(true);
      return;
    }

    if (keyboardIndex !== index) return;

    if (event.key === "ArrowUp" && index > 0) {
      event.preventDefault();
      setOrder((prev) => reorderList(prev, index, index - 1));
      setKeyboardIndex(index - 1);
      setPendingKeyboard(true);
    }
    if (event.key === "ArrowDown" && index < order.length - 1) {
      event.preventDefault();
      setOrder((prev) => reorderList(prev, index, index + 1));
      setKeyboardIndex(index + 1);
      setPendingKeyboard(true);
    }
    if ((event.key === "Enter" || event.key === " ") && pendingKeyboard) {
      event.preventDefault();
      void commitOrder(order, items);
      setKeyboardIndex(null);
    }
    if (event.key === "Escape") {
      event.preventDefault();
      setOrder(items);
      setKeyboardIndex(null);
      setPendingKeyboard(false);
    }
  };

  return (
    <ul
      id={listId}
      className="space-y-2"
      aria-label="Prelación de documentos"
      aria-busy={saving}
    >
      {order.map((item, index) => {
        const highlighted = keyboardIndex === index || dragOverIndex === index;
        return (
          <li
            key={item.document_type_id}
            draggable={!disabled && !saving}
            onDragStart={() => setDragIndex(index)}
            onDragOver={(e) => {
              e.preventDefault();
              if (dragIndex !== null) setDragOverIndex(index);
            }}
            onDragLeave={() => setDragOverIndex((cur) => (cur === index ? null : cur))}
            onDragEnd={() => {
              setDragIndex(null);
              setDragOverIndex(null);
            }}
            onDrop={() => onDrop(index)}
            className="flex items-center gap-3 rounded-xl border bg-card px-3 py-2.5"
            style={{
              borderColor: highlighted ? "#557EFF" : undefined,
              boxShadow: highlighted ? "0 0 0 2px #557EFF33" : undefined,
            }}
          >
            {/* HU #12883 AC2 — posición a la IZQUIERDA, antes del agarrador. */}
            <span
              className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold"
              style={{ background: "var(--badge-info-bg)", color: "var(--badge-info-fg)" }}
            >
              <span className="sr-only">Posición </span>
              {item.sort_order}
            </span>
            <button
              type="button"
              className="cursor-grab rounded p-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
              aria-label={`Reordenar ${item.document_name}. Usa flechas arriba o abajo y Enter para confirmar.`}
              disabled={disabled || saving}
              onKeyDown={(e) => onKeyDown(e, index)}
            >
              <GripVertical className="h-4 w-4 opacity-50" aria-hidden="true" />
            </button>
            <span className="flex-1 text-xs font-semibold text-foreground">
              {item.document_name}
            </span>
            {/* HU #11181 — el organismo necesita distinguir lo que adjunta el gestor de lo que
                produce FLIT: ambos se reordenan, pero solo los primeros se piden en el checklist.
                HU #12883 AC2 — badge tintado en ambos casos (antes solo marcaba el generado). */}
            <StatusBadge
              label={item.is_system_generated ? "Generado por FLIT" : "Lo adjunta el gestor"}
              tone={item.is_system_generated ? "info" : "neutral"}
            />
          </li>
        );
      })}
    </ul>
  );
}

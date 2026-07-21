"use client";

import { useEffect, useId, useState, type KeyboardEvent } from "react";
import { GripVertical } from "lucide-react";
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
  const [keyboardIndex, setKeyboardIndex] = useState<number | null>(null);
  const [pendingKeyboard, setPendingKeyboard] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincronizar orden tras respuesta API
    setOrder(items);
  }, [items]);

  const commitOrder = async (next: OtDocumentPrecedenceItem[]) => {
    setSaving(true);
    try {
      await onReorder(next);
    } finally {
      setSaving(false);
      setPendingKeyboard(false);
    }
  };

  const onDrop = (targetIndex: number) => {
    if (dragIndex === null || disabled || saving) return;
    const next = reorderList(order, dragIndex, targetIndex);
    setOrder(next);
    setDragIndex(null);
    void commitOrder(next);
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
      void commitOrder(order);
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
      {order.map((item, index) => (
        <li
          key={item.document_type_id}
          draggable={!disabled && !saving}
          onDragStart={() => setDragIndex(index)}
          onDragOver={(e) => e.preventDefault()}
          onDrop={() => onDrop(index)}
          className="flex items-center gap-3 rounded-xl border bg-card px-3 py-2.5"
          style={{
            borderColor: keyboardIndex === index ? "#557EFF" : undefined,
            boxShadow: keyboardIndex === index ? "0 0 0 2px #557EFF33" : undefined,
          }}
        >
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
          <span className="text-[10px] opacity-60">#{item.sort_order}</span>
        </li>
      ))}
    </ul>
  );
}

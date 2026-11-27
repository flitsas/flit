'use client';

import { useCallback, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react';

/**
 * HU #14741 — tirador para ajustar el ancho de una columna arrastrando el borde derecho de su
 * cabecera. Va DENTRO del `<th>` (que ya es `sticky`, o sea posicionado: el tirador se ancla a él).
 *
 * - Arrastrar: la columna cambia al momento (`onPreview`) y al soltar se guarda UNA vez (`onCommit`).
 * - Doble clic: vuelve al ancho por defecto (`onReset`).
 * - Teclado: es un `separator` enfocable; ← / → bajan o suben un paso y guardan.
 * - Nunca baja de `min` ni sube de `max`.
 */

/** Ancho mínimo legible por defecto de una columna redimensionable. */
export const COLUMN_RESIZE_MIN_PX = 64;
/** Tope para que una columna no se salga de toda proporción. */
export const COLUMN_RESIZE_MAX_PX = 640;
/** Paso del teclado. */
export const COLUMN_RESIZE_STEP_PX = 16;

export interface ColumnResizeHandleProps {
  /** Nombre visible de la columna: arma el `aria-label` «Ancho de <columna>». */
  label: string;
  /** Ancho actual (px) — alimenta `aria-valuenow`; se usa como base si el `<th>` no se puede medir. */
  width: number;
  /** Ancho mínimo (px). */
  min?: number;
  /** Ancho máximo (px). */
  max?: number;
  /** Cada cambio mientras se arrastra (no persiste). */
  onPreview: (px: number) => void;
  /** Valor final (al soltar o con una tecla): aquí se guarda. */
  onCommit: (px: number) => void;
  /** Vuelve al ancho por defecto de la columna. */
  onReset: () => void;
}

export function ColumnResizeHandle({
  label,
  width,
  min = COLUMN_RESIZE_MIN_PX,
  max = COLUMN_RESIZE_MAX_PX,
  onPreview,
  onCommit,
  onReset,
}: ColumnResizeHandleProps) {
  const ref = useRef<HTMLSpanElement>(null);
  const drag = useRef<{ startX: number; startW: number; last: number } | null>(null);
  const [dragging, setDragging] = useState(false);

  const clamp = useCallback((px: number) => Math.min(max, Math.max(min, Math.round(px))), [min, max]);
  /** Ancho real de la cabecera si se puede medir (las columnas flexibles no tienen px propio). */
  const medir = () => {
    const medido = ref.current?.parentElement?.getBoundingClientRect().width ?? 0;
    return medido > 0 ? medido : width;
  };

  const onPointerDown = (e: React.PointerEvent<HTMLSpanElement>) => {
    if (e.button !== undefined && e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    const startW = medir();
    drag.current = { startX: e.clientX, startW, last: clamp(startW) };
    setDragging(true);
    try {
      e.currentTarget.setPointerCapture?.(e.pointerId);
    } catch {
      /* sin captura el arrastre sigue funcionando mientras el puntero esté sobre el tirador */
    }
  };

  const onPointerMove = (e: React.PointerEvent<HTMLSpanElement>) => {
    const d = drag.current;
    if (!d) return;
    const next = clamp(d.startW + (e.clientX - d.startX));
    if (next === d.last) return;
    d.last = next;
    onPreview(next);
  };

  const finish = (e: React.PointerEvent<HTMLSpanElement>, commit: boolean) => {
    const d = drag.current;
    if (!d) return;
    drag.current = null;
    setDragging(false);
    try {
      e.currentTarget.releasePointerCapture?.(e.pointerId);
    } catch {
      /* ya liberado */
    }
    if (commit && d.last !== clamp(d.startW)) onCommit(d.last);
    else if (!commit) onPreview(clamp(d.startW));
  };

  const onKeyDown = (e: KeyboardEvent<HTMLSpanElement>) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    e.preventDefault();
    e.stopPropagation();
    const base = clamp(medir());
    onCommit(clamp(base + (e.key === 'ArrowRight' ? COLUMN_RESIZE_STEP_PX : -COLUMN_RESIZE_STEP_PX)));
  };

  const onDoubleClick = (e: MouseEvent<HTMLSpanElement>) => {
    e.preventDefault();
    e.stopPropagation();
    onReset();
  };

  return (
    <span
      ref={ref}
      role="separator"
      aria-orientation="vertical"
      aria-label={`Ancho de ${label}`}
      aria-valuenow={clamp(width)}
      aria-valuemin={min}
      aria-valuemax={max}
      tabIndex={0}
      data-dragging={dragging || undefined}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={(e) => finish(e, true)}
      onPointerCancel={(e) => finish(e, false)}
      onKeyDown={onKeyDown}
      onDoubleClick={onDoubleClick}
      onClick={(e) => e.stopPropagation()}
      className={`absolute right-0 top-0 z-20 flex h-full w-2 cursor-col-resize touch-none select-none items-center justify-center rounded-sm outline-none after:h-4 after:w-px after:bg-[color:var(--di-border)] hover:bg-flit-brand/20 hover:after:bg-flit-brand focus-visible:bg-flit-brand/20 focus-visible:ring-2 focus-visible:ring-flit-brand ${
        dragging ? 'bg-flit-brand/30 after:bg-flit-brand' : ''
      }`}
    />
  );
}

export interface ColumnWidthsState {
  /** Ancho por columna con el arrastre en curso encima de lo guardado. */
  widths: Record<string, number>;
  preview: (key: string, px: number) => void;
  commit: (key: string, px: number) => void;
  reset: (key: string) => void;
}

/**
 * Une los anchos guardados (hook `useUiPreferences`) con el arrastre en curso: durante el arrastre
 * solo cambia el borrador local (sin tocar el servidor); al soltar se guarda y el borrador se descarta.
 * Solo se consideran las claves de `validKeys`: el ancho guardado de una columna que ya no existe se ignora.
 */
export function useColumnWidths(
  saved: Readonly<Record<string, number>>,
  validKeys: readonly string[],
  setWidth: (key: string, px: number) => void,
  resetWidth: (key: string) => void,
): ColumnWidthsState {
  const [drafts, setDrafts] = useState<Record<string, number>>({});

  const preview = useCallback((key: string, px: number) => {
    setDrafts((d) => ({ ...d, [key]: px }));
  }, []);
  const quitarBorrador = (key: string) =>
    setDrafts((d) => {
      if (!(key in d)) return d;
      const { [key]: _omit, ...rest } = d;
      void _omit;
      return rest;
    });
  const commit = useCallback(
    (key: string, px: number) => {
      setWidth(key, px);
      quitarBorrador(key);
    },
    [setWidth],
  );
  const reset = useCallback(
    (key: string) => {
      resetWidth(key);
      quitarBorrador(key);
    },
    [resetWidth],
  );

  const widths = useMemo(() => {
    const out: Record<string, number> = {};
    for (const k of validKeys) {
      const w = drafts[k] ?? saved[k];
      if (typeof w === 'number' && Number.isFinite(w) && w > 0) out[k] = w;
    }
    return out;
  }, [drafts, saved, validKeys]);

  return { widths, preview, commit, reset };
}

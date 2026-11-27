'use client';

import { useEffect, useId, useRef, useState, type DragEvent, type KeyboardEvent as ReactKeyboardEvent } from 'react';
import { Icono } from "@flit/ui/Icono";

// Selector de columnas reutilizable: cualquier tabla de trámites (gestor u OT) lo monta pasando
// su propia lista de columnas + la selección actual + el callback de guardado (normalmente
// respaldado por useUiPreferences). Accesible: botón disparador con aria-haspopup/aria-expanded,
// panel con un checkbox nativo por columna (label asociado vía htmlFor/id — foco y lectura de
// pantalla nativos, sin reinventar roles ARIA de menú), cierre con Escape o clic fuera, y bloqueo
// duro de dejar cero columnas visibles (se deshabilita el único checkbox marcado que quedaría).

export interface ColumnOption {
  key: string;
  label: string;
  /**
   * Agrupación opcional para el desplegable. Solo afecta a cómo se LISTAN las columnas aquí; el
   * orden real en la tabla lo sigue mandando el arreglo `columns`. Sirve para separar las
   * columnas base de las adicionales cuando el catálogo crece y la lista plana se lee revuelta.
   */
  group?: string;
}

export interface ColumnSelectorProps {
  /** Columnas disponibles, en el orden en que deben aparecer en la tabla. */
  columns: readonly ColumnOption[];
  /** Claves actualmente visibles. */
  visible: readonly string[];
  /** Nueva selección (ya reordenada según `columns`); nunca se llama con un arreglo vacío. */
  onChange: (next: string[]) => void;
  /** Nombre base del control (botón + panel). Por defecto "Columnas". */
  label?: string;
  /** Deshabilita el disparador (p. ej. mientras se persiste un cambio). */
  disabled?: boolean;
  className?: string;
  /**
   * Clases del BOTÓN disparador. El átomo no impone el estilo de ninguna pantalla: antes traía un
   * borde y un texto azules cableados en un `style` inline, así que se veía "activo" siempre —
   * justo el color con el que la fila de filtros marca lo que sí tiene algo aplicado.
   */
  buttonClassName?: string;
  /**
   * Orden completo actual de las columnas (visibles y ocultas). Sin él se usa el orden de `columns`.
   */
  order?: readonly string[];
  /**
   * Si se pasa, cada columna muestra un asa para arrastrarla (o moverla con ↑/↓) y la lista se
   * muestra plana, en el orden real de la tabla (sin encabezados de grupo).
   */
  onReorder?: (next: string[]) => void;
  /** Si se pasa, aparece «Por defecto» como primera acción del panel. */
  onReset?: () => void;
}

/** Neutro y con la misma geometría que cualquier otro control: 36px de alto, 12px semibold. */
const DEFAULT_BUTTON_CLS =
  'inline-flex h-9 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-xl border ' +
  'border-border bg-card px-3 text-xs font-semibold text-foreground transition ' +
  'hover:bg-secondary focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand ' +
  'focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 ' +
  'dark:border-border dark:bg-card dark:text-foreground';

export function ColumnSelector({
  columns,
  visible,
  onChange,
  label = 'Columnas',
  disabled = false,
  className = '',
  buttonClassName = DEFAULT_BUTTON_CLS,
  order,
  onReorder,
  onReset,
}: ColumnSelectorProps) {
  const [open, setOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelId = useId();
  const hintId = useId();
  const [draggingKey, setDraggingKey] = useState<string | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const focusKeyRef = useRef<string | null>(null);
  const handleRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const reorderable = typeof onReorder === 'function';

  // Orden efectivo: el recibido (solo claves conocidas) + las columnas que no mencione, al final.
  const orderedKeys = (() => {
    const known = columns.map((c) => c.key);
    const base = (order ?? known).filter((k, i, all) => known.includes(k) && all.indexOf(k) === i);
    return [...base, ...known.filter((k) => !base.includes(k))];
  })();
  const orderedColumns = orderedKeys
    .map((k) => columns.find((c) => c.key === k))
    .filter((c): c is ColumnOption => c !== undefined);

  // Tras mover con teclado, React reubica el nodo y el navegador pierde el foco: se devuelve al asa.
  useEffect(() => {
    if (focusKeyRef.current) {
      handleRefs.current[focusKeyRef.current]?.focus();
      focusKeyRef.current = null;
    }
  });

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (panelRef.current?.contains(target) || triggerRef.current?.contains(target)) return;
      setOpen(false);
    };
    document.addEventListener('keydown', onKeyDown);
    document.addEventListener('mousedown', onPointerDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.removeEventListener('mousedown', onPointerDown);
    };
  }, [open]);

  // Grupos en orden de primera aparición, conservando dentro de cada uno el orden del catálogo
  // (que es el orden real de la tabla). Sin `group` queda un único bloque sin encabezado.
  const grupos: { nombre: string | null; columnas: readonly ColumnOption[] }[] = [];
  for (const col of orderedColumns) {
    const nombre = reorderable ? null : (col.group ?? null);
    const ultimo = grupos.find((g) => g.nombre === nombre);
    if (ultimo) (ultimo.columnas as ColumnOption[]).push(col);
    else grupos.push({ nombre, columnas: [col] });
  }

  const toggle = (key: string) => {
    const isChecked = visible.includes(key);
    if (isChecked && visible.length <= 1) return; // impide dejar cero columnas visibles
    const nextKeys = new Set(isChecked ? visible.filter((k) => k !== key) : [...visible, key]);
    // Se reordena según el orden canónico de `columns` (no el orden de clic), así el resultado
    // es determinista aunque la preferencia guardada llegue con otro orden o quede incompleta.
    const ordered = orderedKeys.filter((k) => nextKeys.has(k));
    onChange(ordered);
  };

  const labelOf = (key: string) => columns.find((c) => c.key === key)?.label ?? key;

  const moveTo = (key: string, toIndex: number) => {
    const from = orderedKeys.indexOf(key);
    const to = Math.max(0, Math.min(orderedKeys.length - 1, toIndex));
    if (from < 0 || from === to) return false;
    const next = orderedKeys.filter((k) => k !== key);
    next.splice(to, 0, key);
    onReorder?.(next);
    setAnnouncement(`${labelOf(key)}: posición ${to + 1} de ${next.length}`);
    return true;
  };

  const onHandleKeyDown = (e: ReactKeyboardEvent<HTMLButtonElement>, key: string) => {
    if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
    e.preventDefault();
    const delta = e.key === 'ArrowUp' ? -1 : 1;
    const index = orderedKeys.indexOf(key);
    if (moveTo(key, index + delta)) focusKeyRef.current = key;
  };

  const onRowDrop = (e: DragEvent<HTMLDivElement>, targetKey: string) => {
    e.preventDefault();
    const source = draggingKey ?? e.dataTransfer?.getData('text/plain');
    setDraggingKey(null);
    if (!source || source === targetKey) return;
    moveTo(source, orderedKeys.indexOf(targetKey));
  };

  const renderRow = (col: ColumnOption) => {
    const checked = visible.includes(col.key);
    const isOnlyChecked = checked && visible.length <= 1;
    const checkboxId = `${panelId}-${col.key}`;
    const position = orderedKeys.indexOf(col.key) + 1;
    return (
      <div
        key={col.key}
        data-column-key={col.key}
        onDragOver={reorderable ? (e) => e.preventDefault() : undefined}
        onDrop={reorderable ? (e) => onRowDrop(e, col.key) : undefined}
        className={`flex items-center gap-1 rounded-lg transition hover:bg-flit-brand/10 ${
          draggingKey === col.key ? 'opacity-50' : ''
        }`}
      >
        {reorderable ? (
          <button
            type="button"
            ref={(el) => {
              handleRefs.current[col.key] = el;
            }}
            draggable
            onDragStart={(e) => {
              setDraggingKey(col.key);
              e.dataTransfer?.setData('text/plain', col.key);
              if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
            }}
            onDragEnd={() => setDraggingKey(null)}
            onKeyDown={(e) => onHandleKeyDown(e, col.key)}
            aria-label={`Mover ${col.label}, posición ${position} de ${orderedKeys.length}`}
            title="Arrastra o usa ↑ ↓ para cambiar el orden"
            className="flex h-7 w-6 shrink-0 cursor-grab items-center justify-center rounded text-xs leading-none opacity-50 hover:opacity-100 focus:outline-none focus-visible:opacity-100 focus-visible:ring-2 focus-visible:ring-flit-brand active:cursor-grabbing"
          >
            <span aria-hidden="true">⋮⋮</span>
          </button>
        ) : null}
        <label
          htmlFor={checkboxId}
          className={`flex min-w-0 flex-1 items-center gap-2.5 px-2 py-1.5 text-xs ${
            isOnlyChecked ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'
          }`}
          title={isOnlyChecked ? 'Debes mantener al menos una columna visible' : undefined}
        >
          <input
            id={checkboxId}
            type="checkbox"
            checked={checked}
            disabled={isOnlyChecked}
            onChange={() => toggle(col.key)}
            className="h-3.5 w-3.5 shrink-0 accent-flit-brand"
          />
          <span className="min-w-0 flex-1 truncate">{col.label}</span>
        </label>
      </div>
    );
  };

  return (
    <div className={`relative inline-block text-left ${className}`}>
      <button
        ref={triggerRef}
        type="button"
        aria-haspopup="true"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        aria-label={`${label}: elegir columnas visibles`}
        onClick={() => setOpen((o) => !o)}
        disabled={disabled}
        className={buttonClassName}
      >
        <Icono nombre="rejilla" className="h-3.5 w-3.5" />
        {label}
      </button>

      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label={`Elegir columnas visibles de ${label.toLowerCase()}`}
          aria-describedby={hintId}
          className="absolute right-0 top-full z-20 mt-1 flex max-h-[min(70dvh,28rem)] w-72 flex-col overflow-hidden rounded-xl border bg-card shadow-[var(--kit-sombra)] dark:bg-secondary"
        >
          {/* Cabecera y aviso quedan FIJOS; solo scrollea la lista. El scroll va en un `div`, no
              en el `<fieldset>`: fieldset no funciona como contenedor flex con overflow y la
              lista se desbordaba fuera del panel, encimándose con el aviso. */}
          <div className="flex shrink-0 items-baseline justify-between gap-2 border-b px-4 py-2.5">
            <span className="text-[11px] font-semibold uppercase tracking-wide opacity-60">
              {label}
            </span>
            <span className="text-[11px] tabular-nums opacity-50">
              {visible.length} de {columns.length}
            </span>
          </div>

          {onReset ? (
            <div className="shrink-0 border-b px-2 py-1.5">
              <button
                type="button"
                onClick={onReset}
                className="w-full rounded-lg px-2 py-1.5 text-left text-xs font-semibold text-flit-brand transition hover:bg-flit-brand/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
              >
                Por defecto
              </button>
            </div>
          ) : null}

          <div className="min-h-0 flex-1 overflow-y-auto px-2 py-2">
            <fieldset className="flex flex-col gap-0.5">
              {/* El título visible vive en la cabecera fija; la leyenda queda para lector de
                  pantalla, que sí necesita el nombre del grupo de checkboxes. */}
              <legend className="sr-only">{label}</legend>
              {grupos.map((grupo, gi) => (
                <div key={grupo.nombre ?? `grupo-${gi}`} className="flex flex-col gap-0.5">
                  {grupo.nombre ? (
                    <p
                      className={`px-2 text-[10px] font-semibold uppercase tracking-wide opacity-45 ${
                        gi === 0 ? 'pb-1' : 'pb-1 pt-2'
                      }`}
                    >
                      {grupo.nombre}
                    </p>
                  ) : null}
                  {grupo.columnas.map((col) => renderRow(col))}
                </div>
              ))}
            </fieldset>
          </div>

          <p id={hintId} className="shrink-0 border-t px-4 py-2 text-[10px] opacity-60">
            Debes mantener al menos una columna visible.
          </p>
          <div role="status" aria-live="polite" aria-atomic="true" className="sr-only">
            {announcement}
          </div>
        </div>
      ) : null}
    </div>
  );
}

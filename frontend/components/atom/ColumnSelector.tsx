'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { Columns3 } from 'lucide-react';

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
}

export function ColumnSelector({
  columns,
  visible,
  onChange,
  label = 'Columnas',
  disabled = false,
  className = '',
}: ColumnSelectorProps) {
  const [open, setOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelId = useId();
  const hintId = useId();

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
  for (const col of columns) {
    const nombre = col.group ?? null;
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
    const ordered = columns.map((c) => c.key).filter((k) => nextKeys.has(k));
    onChange(ordered);
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
        className="inline-flex shrink-0 items-center gap-1.5 rounded-xl border px-4 py-2 text-[11px] font-semibold transition disabled:cursor-not-allowed disabled:opacity-50"
        style={{ borderColor: '#557EFF', color: '#557EFF' }}
      >
        <Columns3 className="h-3.5 w-3.5" aria-hidden="true" />
        {label}
      </button>

      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label={`Elegir columnas visibles de ${label.toLowerCase()}`}
          aria-describedby={hintId}
          className="absolute right-0 top-full z-20 mt-1 flex max-h-[min(70vh,28rem)] w-72 flex-col overflow-hidden rounded-xl border bg-white shadow-lg dark:bg-[#162744]"
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
                  {grupo.columnas.map((col) => {
                    const checked = visible.includes(col.key);
                    const isOnlyChecked = checked && visible.length <= 1;
                    const checkboxId = `${panelId}-${col.key}`;
                    return (
                      <label
                        key={col.key}
                        htmlFor={checkboxId}
                        className={`flex items-center gap-2.5 rounded-lg px-2 py-1.5 text-xs transition hover:bg-[#557EFF]/10 ${
                          isOnlyChecked ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'
                        }`}
                        title={
                          isOnlyChecked ? 'Debes mantener al menos una columna visible' : undefined
                        }
                      >
                        <input
                          id={checkboxId}
                          type="checkbox"
                          checked={checked}
                          disabled={isOnlyChecked}
                          onChange={() => toggle(col.key)}
                          className="h-3.5 w-3.5 shrink-0 accent-[#557EFF]"
                        />
                        <span className="min-w-0 flex-1 truncate">{col.label}</span>
                      </label>
                    );
                  })}
                </div>
              ))}
            </fieldset>
          </div>

          <p id={hintId} className="shrink-0 border-t px-4 py-2 text-[10px] opacity-60">
            Debes mantener al menos una columna visible.
          </p>
        </div>
      ) : null}
    </div>
  );
}

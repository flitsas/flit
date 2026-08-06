"use client";

// Selector de columnas del informe.
//
// El backend devuelve SIEMPRE todos los campos de cada fila, así que marcar y desmarcar aquí no
// dispara ninguna consulta: la tabla se redibuja al instante. Es lo que hace que valga la pena
// dejarlo abierto e ir probando, en vez de convertirse en un formulario que se llena una vez.

import { useEffect, useRef, useState } from "react";
import { groupsOf, type DataColumn } from "./columns";

/**
 * Recibe la definición de columnas por props en vez de importarla: el informe del periodo y el de
 * revisores usan este mismo selector con listas distintas, y un componente que conociera una de las
 * dos obligaría a duplicarlo entero para la otra.
 */
export function ColumnPicker<TRow>({
  visible,
  onChange,
  columns,
  testId = "ot-report-column-picker",
}: {
  visible: string[];
  onChange: (ids: string[]) => void;
  columns: DataColumn<TRow, string>[];
  testId?: string;
}) {
  const groups = groupsOf(columns);
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Cerrar al hacer clic fuera y con Escape: es un popover, y uno que solo se cierra con su propio
  // botón se queda tapando la tabla que el usuario acaba de configurar.
  useEffect(() => {
    if (!open) return undefined;

    function onPointerDown(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  function toggle(id: string) {
    if (visible.includes(id)) {
      // Nunca dejar la tabla sin columnas: quitar la última produce una cuadrícula vacía que parece
      // un error de carga.
      if (visible.length === 1) return;
      onChange(visible.filter((v) => v !== id));
      return;
    }

    // Se reordena según la definición para que el orden de las columnas sea estable y no dependa
    // del orden en que se fueron marcando.
    onChange(columns.filter((c) => c.id === id || visible.includes(c.id)).map((c) => c.id));
  }

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-haspopup="true"
        className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-xs font-semibold transition hover:border-[#557EFF] dark:border-white/10"
        data-testid={testId}
      >
        Columnas ({visible.length})
      </button>

      {open && (
        <div
          className="absolute right-0 z-50 mt-2 max-h-[24rem] w-64 overflow-y-auto rounded-2xl border border-[#DFE5ED] bg-white p-3 shadow-2xl dark:border-white/10 dark:bg-[#0B0F14]"
          data-testid={`${testId}-panel`}
        >
          {groups.map((group) => (
            <fieldset key={group} className="mb-2 last:mb-0">
              <legend className="mb-1 text-[10px] font-semibold uppercase tracking-wide text-[#6B7280] dark:text-white/50">
                {group}
              </legend>
              {columns.filter((c) => c.group === group).map((column) => (
                <label
                  key={column.id}
                  className="flex cursor-pointer items-center gap-2 rounded-lg px-1.5 py-1 text-xs hover:bg-[#F5F7FA] dark:hover:bg-white/5"
                >
                  <input
                    type="checkbox"
                    checked={visible.includes(column.id)}
                    onChange={() => toggle(column.id)}
                    className="accent-[#557EFF]"
                  />
                  {column.label}
                </label>
              ))}
            </fieldset>
          ))}
        </div>
      )}
    </div>
  );
}

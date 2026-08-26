"use client";

// Filtro de revisores: uno, varios o todos.
//
// Es un popover con casillas y no un <select multiple>, que en la práctica nadie sabe usar —exige
// ctrl+clic para añadir y un clic suelto borra toda la selección hecha—. Aquí cada clic hace una
// sola cosa y lo elegido se ve resumido sin abrir nada.
//
// «Ninguno seleccionado» significa TODOS, igual que en el backend. Es la lectura que hace que el
// informe sirva nada más abrirlo, y se dice en el propio botón para que nadie lo interprete al
// revés.

import { useEffect, useRef, useState } from "react";
import type { OtReviewerOption } from "@/lib/api/ot-metrics";
import { formatInt, plural } from "./report-columns";
import { FIELD_CLS } from "./shared";

export function ReviewerPicker({
  selected,
  options,
  onChange,
}: {
  selected: string[];
  options: OtReviewerOption[];
  onChange: (userIds: string[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const [filtro, setFiltro] = useState("");
  const containerRef = useRef<HTMLDivElement>(null);

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

  function toggle(userId: string) {
    onChange(
      selected.includes(userId)
        ? selected.filter((id) => id !== userId)
        : // Se reordena según el catálogo para que la selección no dependa del orden de los clics.
          options.filter((o) => o.userId === userId || selected.includes(o.userId)).map((o) => o.userId),
    );
  }

  const visibles = filtro.trim()
    ? options.filter((o) => o.displayName.toLowerCase().includes(filtro.trim().toLowerCase()))
    : options;

  const resumen =
    selected.length === 0
      ? "Todos los revisores"
      : selected.length === 1
        ? (options.find((o) => o.userId === selected[0])?.displayName ?? "1 revisor")
        : plural(selected.length, "revisor", "revisores");

  return (
    <div className="flex flex-col gap-1" ref={containerRef}>
      <label className="text-[11px] font-semibold text-[#6B7280] dark:text-white/50">
        Revisores
      </label>
      <div className="relative">
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          aria-haspopup="true"
          className={`${FIELD_CLS} min-w-[13rem] text-left`}
          data-testid="ot-reviewers-picker"
        >
          {resumen} ▾
        </button>

        {open && (
          <div
            className="absolute left-0 z-50 mt-2 max-h-[22rem] w-72 overflow-y-auto rounded-2xl border border-[#DFE5ED] bg-white p-3 shadow-2xl dark:border-white/10 dark:bg-[#0B0F14]"
            data-testid="ot-reviewers-picker-panel"
          >
            <div className="mb-2 flex items-center justify-between gap-2">
              <span className="text-[10px] font-semibold uppercase tracking-wide text-[#6B7280] dark:text-white/50">
                {plural(options.length, "revisor", "revisores")}
              </span>
              {/* Volver a «todos» es un clic, no desmarcar veinte casillas una por una. */}
              <button
                type="button"
                onClick={() => onChange([])}
                disabled={selected.length === 0}
                className="text-[11px] font-semibold text-[#557EFF] disabled:opacity-40"
              >
                Todos
              </button>
            </div>

            {options.length > 8 && (
              <input
                type="search"
                value={filtro}
                onChange={(e) => setFiltro(e.target.value)}
                placeholder="Buscar…"
                aria-label="Buscar revisor"
                className={`${FIELD_CLS} mb-2 w-full`}
              />
            )}

            {visibles.length === 0 ? (
              <p className="px-1.5 py-2 text-xs text-[#6B7280] dark:text-white/50">
                {options.length === 0
                  ? "Nadie ha decidido trámites en este organismo todavía."
                  : "Ningún revisor coincide con la búsqueda."}
              </p>
            ) : (
              visibles.map((option) => (
                <label
                  key={option.userId}
                  className="flex cursor-pointer items-center gap-2 rounded-lg px-1.5 py-1 text-xs hover:bg-[#F5F7FA] dark:hover:bg-white/5"
                >
                  <input
                    type="checkbox"
                    checked={selected.includes(option.userId)}
                    onChange={() => toggle(option.userId)}
                    className="accent-[#557EFF]"
                  />
                  <span className="min-w-0 flex-1 truncate">{option.displayName}</span>
                  {/* El histórico ordena la lista y da contexto: quien tiene 3 decisiones en dos
                      años no es comparable con quien tiene 900. */}
                  <span className="shrink-0 tabular-nums text-[10px] text-[#6B7280] dark:text-white/40">
                    {formatInt(option.decisiones)}
                  </span>
                </label>
              ))
            )}
          </div>
        )}
      </div>
    </div>
  );
}

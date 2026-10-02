"use client";

import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { CalendarDays } from "lucide-react";
import { DayButton, DayPicker, type DayButtonProps } from "react-day-picker";
import { es } from "react-day-picker/locale";
import "react-day-picker/style.css";

import { toIsoDate } from "@/components/atom/modules/_reportes/range";
import { isoToDisplay } from "@/components/atom/DateRangePicker";

/**
 * Fecha en un modal con scroll y desenfoque.
 * El `<input type="date">` nativo abre su calendario en el sistema de coordenadas del
 * `backdrop-filter` del overlay y el modal salta o el calendario queda cortado.
 * Este control pinta el mes fuera del modal, en el body.
 */
export function FechaCalendario({
  id,
  label,
  value,
  onChange,
  invalid,
  describedBy,
}: {
  id: string;
  label: string;
  /** ISO `YYYY-MM-DD`, o vacío. */
  value: string;
  onChange: (iso: string) => void;
  invalid?: boolean;
  describedBy?: string;
}) {
  const titleId = useId();
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const [box, setBox] = useState<{ top: number; left: number } | null>(null);

  useLayoutEffect(() => {
    if (!open || !triggerRef.current) return;
    const place = () => {
      const r = triggerRef.current!.getBoundingClientRect();
      const width = 320;
      const height = 340;
      let top = r.bottom + 8;
      let left = Math.max(8, r.left);
      if (top + height > window.innerHeight - 8) top = Math.max(8, r.top - height - 8);
      if (left + width > window.innerWidth - 8) left = Math.max(8, window.innerWidth - width - 8);
      setBox({ top, left });
    };
    place();
    window.addEventListener("resize", place);
    return () => window.removeEventListener("resize", place);
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      e.stopPropagation();
      setOpen(false);
      triggerRef.current?.focus();
    };
    const onPointer = (e: MouseEvent) => {
      const target = e.target as Node;
      if (panelRef.current?.contains(target) || triggerRef.current?.contains(target)) return;
      setOpen(false);
    };
    // Captura: el modal padre escucha Escape en burbuja y si no cortamos aquí se cierra entero.
    document.addEventListener("keydown", onKey, true);
    document.addEventListener("mousedown", onPointer);
    return () => {
      document.removeEventListener("keydown", onKey, true);
      document.removeEventListener("mousedown", onPointer);
    };
  }, [open]);

  const texto = value ? isoToDisplay(value) : "DD/MM/AAAA";

  return (
    <div className="min-w-0">
      <label htmlFor={id} className="mb-1.5 block text-xs font-semibold">
        {label}
      </label>
      <button
        ref={triggerRef}
        id={id}
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        onClick={() => setOpen((v) => !v)}
        className={`flex w-full min-w-0 items-center justify-between gap-2 rounded-xl border bg-white px-3 py-2 text-left text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14] ${
          invalid ? "border-[#E5484D]" : "border-[#DFE5ED] dark:border-white/15"
        } ${value ? "text-[#162744] dark:text-white" : "text-[#59677D]"}`}
      >
        <span>{texto}</span>
        <CalendarDays className="h-4 w-4 shrink-0 text-[#59677D]" aria-hidden="true" />
      </button>
      {open && box
        ? createPortal(
            <div
              ref={panelRef}
              role="dialog"
              aria-labelledby={titleId}
              className="fixed z-[200] rounded-xl border border-[#DFE5ED] bg-white p-3 shadow-lg dark:border-white/15 dark:bg-[#0B0F14] dark:text-white"
              style={{ top: box.top, left: box.left }}
            >
              <p id={titleId} className="sr-only">
                Elegir {label}
              </p>
              <DayPicker
                mode="single"
                locale={es}
                selected={isoToDate(value)}
                defaultMonth={isoToDate(value) ?? new Date()}
                onSelect={(day) => {
                  onChange(day ? toIsoDate(day) : "");
                  setOpen(false);
                }}
                components={{ DayButton: Dia }}
                classNames={{
                  root: "rdp-root text-[#162744] dark:text-white",
                  chevron: "fill-[#557EFF]",
                  day_button:
                    "rounded-lg text-xs font-medium hover:bg-[#557EFF]/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]",
                  selected: "bg-[#557EFF] text-white hover:bg-[#557EFF] hover:text-white",
                  today: "font-bold text-[#557EFF]",
                  outside: "text-[#667085] opacity-50 dark:text-white/40",
                }}
              />
            </div>,
            document.body,
          )
        : null}
    </div>
  );
}

function isoToDate(iso: string): Date | undefined {
  if (!iso) return undefined;
  const [y, m, d] = iso.split("-").map(Number);
  if (!y || !m || !d) return undefined;
  return new Date(y, m - 1, d);
}

function Dia(props: DayButtonProps) {
  const iso = toIsoDate(props.day.date);
  return (
    <DayButton
      {...props}
      data-testid={`day-${iso}`}
      {...(props.modifiers.outside ? { "data-outside": "" } : {})}
    />
  );
}

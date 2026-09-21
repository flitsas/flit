"use client";

import { useEffect, useId, useRef, useState, type RefObject } from "react";
import { CalendarDays, ChevronDown } from "lucide-react";
import {
  DayPicker,
  DayButton,
  type DateRange as DayPickerRange,
  type DayButtonProps,
} from "react-day-picker";
import { es } from "react-day-picker/locale";
import "react-day-picker/style.css";

import { toIsoDate, type DateRange } from "@/components/atom/modules/_reportes/range";

export interface DateRangePickerProps {
  value: DateRange;
  onChange: (next: DateRange) => void;
  disabled?: boolean;
  /** Etiqueta visible del campo. */
  label?: string;
  id?: string;
  className?: string;
  /** Texto cuando no hay fechas seleccionadas. */
  placeholder?: string;
}

const FIELD_CLS =
  "inline-flex h-10 w-full min-w-[220px] items-center gap-2 rounded-[10px] border border-[#DFE5ED] bg-white px-3 text-xs font-medium text-[#162744] outline-none transition hover:bg-[#F4F7FC] focus-visible:border-[#557EFF] focus-visible:ring-2 focus-visible:ring-[#557EFF]/30 disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/15 dark:bg-[#0B0F14] dark:text-white dark:hover:bg-white/5";

const POPOVER_CLS =
  "absolute left-0 top-full z-40 mt-2 rounded-xl border border-[#DFE5ED] bg-white p-3 shadow-lg dark:border-white/15 dark:bg-[#0B0F14] dark:text-white";

/** ISO `YYYY-MM-DD` → `DD/MM/AAAA` para mostrar al usuario. */
export function isoToDisplay(iso: string): string {
  if (!iso) return "";
  const [year, month, day] = iso.split("-");
  if (!year || !month || !day) return "";
  return `${day}/${month}/${year}`;
}

/** Texto del campo: `DD/MM/AAAA – DD/MM/AAAA`. */
export function formatRangeDisplay(range: DateRange, placeholder: string): string {
  if (!range.from && !range.to) return placeholder;
  const from = range.from ? isoToDisplay(range.from) : "…";
  const to = range.to ? isoToDisplay(range.to) : "…";
  return `${from} – ${to}`;
}

function isoToDate(iso: string): Date | undefined {
  if (!iso) return undefined;
  const [y, m, d] = iso.split("-").map(Number);
  if (!y || !m || !d) return undefined;
  return new Date(y, m - 1, d);
}

function toDayPickerRange(range: DateRange): DayPickerRange | undefined {
  const from = isoToDate(range.from);
  const to = isoToDate(range.to);
  if (!from && !to) return undefined;
  return { from, to };
}

function fromDayPickerRange(selected: DayPickerRange | undefined): DateRange {
  return {
    from: selected?.from ? toIsoDate(selected.from) : "",
    to: selected?.to ? toIsoDate(selected.to) : "",
  };
}

function isInvalidRange(range: DateRange): boolean {
  return Boolean(range.from && range.to && range.from > range.to);
}

function FlitDayButton(props: DayButtonProps) {
  const iso = toIsoDate(props.day.date);
  return <DayButton {...props} data-testid={`day-${iso}`} />;
}

function usePopoverDismiss(
  open: boolean,
  onClose: () => void,
  triggerRef: RefObject<HTMLElement | null>,
) {
  const panelRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
        triggerRef.current?.focus();
      }
    };
    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (panelRef.current?.contains(target) || triggerRef.current?.contains(target)) return;
      onClose();
    };
    document.addEventListener("keydown", onKeyDown);
    document.addEventListener("mousedown", onPointerDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.removeEventListener("mousedown", onPointerDown);
    };
  }, [open, onClose, triggerRef]);
  return panelRef;
}

/**
 * Selector de rango de fechas en un solo campo (HU #12724, bloque B.2).
 * Abre un popover con calendario de rango; emite el contrato {@link DateRange} de `range.ts`.
 */
export function DateRangePicker({
  value,
  onChange,
  disabled = false,
  label = "Rango de fechas",
  id,
  className = "",
  placeholder = "Seleccionar rango",
}: DateRangePickerProps) {
  const generatedId = useId();
  const fieldId = id ?? `date-range-${generatedId}`;
  const panelId = `${fieldId}-panel`;
  const errorId = `${fieldId}-error`;

  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<DateRange>(value);
  const [invalidMessage, setInvalidMessage] = useState<string | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  /** Ref espejo del draft para que «Aplicar» lea la última selección del calendario sin esperar al re-render. */
  const draftRef = useRef<DateRange>(value);
  /** Clicks de selección en la sesión abierta (rdp v9 puede fijar from=to en el primer click). */
  const selectEventsRef = useRef(0);

  useEffect(() => {
    if (!open) {
      setDraft(value);
      draftRef.current = value;
    }
  }, [value, open]);

  const close = () => setOpen(false);
  const panelRef = usePopoverDismiss(open, close, triggerRef);

  const displayText = formatRangeDisplay(value, placeholder);
  const hasValue = Boolean(value.from || value.to);

  const syncDraft = (next: DateRange) => {
    draftRef.current = next;
    setDraft(next);
  };

  const tryEmit = (next: DateRange, closeOnSuccess = false) => {
    if (isInvalidRange(next)) {
      setInvalidMessage("La fecha final no puede ser anterior a la inicial.");
      syncDraft(next);
      return false;
    }
    setInvalidMessage(null);
    syncDraft(next);
    onChange(next);
    if (closeOnSuccess) close();
    return true;
  };

  const normalizeForApply = (next: DateRange): DateRange => {
    // Un solo click en rdp v9 deja from=to; para el dashboard eso significa «solo desde», no un día cerrado.
    if (next.from && next.to && next.from === next.to && selectEventsRef.current <= 1) {
      return { from: next.from, to: "" };
    }
    return next;
  };

  const handleSelect = (selected: DayPickerRange | undefined) => {
    selectEventsRef.current += 1;
    const next = fromDayPickerRange(selected);
    if (isInvalidRange(next)) {
      setInvalidMessage("La fecha final no puede ser anterior a la inicial.");
      syncDraft(next);
      return;
    }
    setInvalidMessage(null);
    syncDraft(next);
    const distinctRange = next.from && next.to && next.from !== next.to;
    const sameDayConfirmed = next.from && next.to && next.from === next.to && selectEventsRef.current >= 2;
    if (distinctRange || sameDayConfirmed) {
      onChange(next);
      close();
    }
  };

  const handleApply = () => {
    tryEmit(normalizeForApply(draftRef.current), true);
  };

  const handleClear = () => {
    const empty = { from: "", to: "" };
    setInvalidMessage(null);
    syncDraft(empty);
    onChange(empty);
    close();
  };

  const toggleOpen = () => {
    if (disabled) return;
    setOpen((v) => !v);
    if (!open) {
      syncDraft(value);
      selectEventsRef.current = 0;
      setInvalidMessage(null);
    }
  };

  return (
    <div className={`relative flex flex-col gap-1 ${className}`} data-testid="date-range-picker">
      <label htmlFor={fieldId} className="text-[10px] font-semibold uppercase opacity-60">
        {label}
      </label>
      <button
        ref={triggerRef}
        id={fieldId}
        type="button"
        disabled={disabled}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        aria-label={`${label}: ${hasValue ? displayText : placeholder}`}
        aria-invalid={invalidMessage ? true : undefined}
        aria-describedby={invalidMessage ? errorId : undefined}
        className={`${FIELD_CLS} ${hasValue ? "border-[#557EFF] text-[#3B4FD6] dark:text-[#8FA8FF]" : ""}`}
        onClick={toggleOpen}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            toggleOpen();
          }
        }}
      >
        <CalendarDays className="h-4 w-4 shrink-0 opacity-70" aria-hidden="true" />
        <span className="min-w-0 flex-1 truncate text-left">{displayText}</span>
        <ChevronDown className="h-3.5 w-3.5 shrink-0 opacity-60" aria-hidden="true" />
      </button>

      {invalidMessage && !open && (
        <p id={errorId} role="alert" className="text-[11px] font-medium text-[#C2410C] dark:text-[#FF9B73]">
          {invalidMessage}
        </p>
      )}

      {open && (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Elegir rango de fechas"
          className={`${POPOVER_CLS} flit-date-range-picker`}
        >
          <DayPicker
            mode="range"
            locale={es}
            selected={toDayPickerRange(draft)}
            onSelect={handleSelect}
            numberOfMonths={1}
            defaultMonth={isoToDate(draft.from) ?? isoToDate(draft.to) ?? new Date()}
            components={{ DayButton: FlitDayButton }}
            classNames={{
              root: "rdp-root text-[#162744] dark:text-white",
              chevron: "fill-[#557EFF]",
              day_button:
                "rounded-lg text-xs font-medium hover:bg-[#557EFF]/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]",
              selected: "bg-[#557EFF] text-white hover:bg-[#557EFF] hover:text-white",
              range_start: "rounded-l-lg bg-[#557EFF] text-white",
              range_end: "rounded-r-lg bg-[#557EFF] text-white",
              range_middle: "bg-[#557EFF]/15 text-[#162744] dark:text-white",
              today: "font-bold text-[#557EFF]",
              outside: "text-[#667085] opacity-50 dark:text-white/40",
              disabled: "opacity-40",
            }}
          />

          {invalidMessage && (
            <p role="alert" className="mt-2 text-[11px] font-medium text-[#C2410C] dark:text-[#FF9B73]">
              {invalidMessage}
            </p>
          )}

          <div className="mt-3 flex flex-wrap items-center justify-end gap-2 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
            <button
              type="button"
              data-testid="date-range-clear"
              className="rounded-lg border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold text-[#59677D] hover:bg-[#F4F7FC] dark:border-white/15 dark:text-white/70 dark:hover:bg-white/5"
              onClick={handleClear}
            >
              Limpiar
            </button>
            <button
              type="button"
              data-testid="date-range-apply"
              className="rounded-lg bg-[#557EFF] px-3 py-1.5 text-xs font-semibold text-white hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:focus-visible:ring-offset-[#0B0F14]"
              onClick={handleApply}
            >
              Aplicar
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

"use client";

import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { MoreVertical, type LucideIcon } from "lucide-react";

// Menú de acciones accesible "⋯ Acciones" (HU #10194 — consolidación de config OT en
// tabla). Genérico para columnas de acciones de cualquier tabla admin: botón disparador
// (aria-haspopup="menu", aria-expanded, aria-controls) + panel `role="menu"` con
// `role="menuitem"` navegable por teclado (flechas, Home/End, Escape, cierre al perder
// foco/clic fuera). Cada ítem puede deshabilitarse con un motivo (tooltip + aria-disabled).
export interface ActionsMenuItem {
  key: string;
  label: string;
  icon?: LucideIcon;
  onSelect: () => void;
  disabled?: boolean;
  /** Motivo del deshabilitado; se usa como tooltip cuando `disabled` es true. */
  disabledReason?: string;
}

export interface ActionsMenuProps {
  items: ActionsMenuItem[];
  /** Nombre accesible del botón disparador (p. ej. "Acciones para Secretaría de Movilidad Bogotá"). */
  ariaLabel: string;
  /** Texto visible del botón. Por defecto "Acciones". */
  triggerLabel?: string;
  className?: string;
}

export function ActionsMenu({ items, ariaLabel, triggerLabel = "Acciones", className = "" }: ActionsMenuProps) {
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const itemRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const menuId = useId();

  useEffect(() => {
    if (!open) {
      return;
    }
    const firstEnabled = items.findIndex((item) => !item.disabled);
    itemRefs.current[firstEnabled]?.focus();

    const onDocMouseDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) {
        return;
      }
      setOpen(false);
    };
    document.addEventListener("mousedown", onDocMouseDown);
    return () => document.removeEventListener("mousedown", onDocMouseDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const close = (refocusTrigger = true) => {
    setOpen(false);
    if (refocusTrigger) {
      buttonRef.current?.focus();
    }
  };

  const onMenuKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const enabledIndexes = items
      .map((item, i) => (!item.disabled ? i : -1))
      .filter((i) => i >= 0);
    if (enabledIndexes.length === 0) {
      return;
    }
    const currentIndex = itemRefs.current.findIndex((el) => el === document.activeElement);
    const moveTo = (idx: number) => itemRefs.current[idx]?.focus();

    switch (e.key) {
      case "ArrowDown": {
        e.preventDefault();
        const pos = enabledIndexes.indexOf(currentIndex);
        moveTo(enabledIndexes[(pos + 1) % enabledIndexes.length]);
        break;
      }
      case "ArrowUp": {
        e.preventDefault();
        const pos = enabledIndexes.indexOf(currentIndex);
        moveTo(enabledIndexes[(pos - 1 + enabledIndexes.length) % enabledIndexes.length]);
        break;
      }
      case "Home":
        e.preventDefault();
        moveTo(enabledIndexes[0]);
        break;
      case "End":
        e.preventDefault();
        moveTo(enabledIndexes[enabledIndexes.length - 1]);
        break;
      case "Escape":
        e.preventDefault();
        close();
        break;
      case "Tab":
        setOpen(false);
        break;
      default:
        break;
    }
  };

  return (
    <div className={`relative inline-block text-left ${className}`}>
      <button
        ref={buttonRef}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={ariaLabel}
        title={ariaLabel}
        onClick={() => setOpen((o) => !o)}
        className="inline-flex items-center gap-1 rounded-lg border px-2.5 py-1.5 text-[11px] font-semibold transition hover:bg-[#557EFF]/10"
      >
        <MoreVertical className="h-3.5 w-3.5" aria-hidden="true" />
        {triggerLabel}
      </button>

      {open && (
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          aria-label={ariaLabel}
          onKeyDown={onMenuKeyDown}
          className="absolute right-0 z-20 mt-1 min-w-[240px] rounded-xl border bg-white p-1 shadow-lg dark:bg-[#0B0F14]"
        >
          {items.map((item, i) => {
            const Icon = item.icon;
            return (
              <button
                key={item.key}
                ref={(el) => {
                  itemRefs.current[i] = el;
                }}
                type="button"
                role="menuitem"
                disabled={item.disabled}
                aria-disabled={item.disabled || undefined}
                title={item.disabled ? (item.disabledReason ?? item.label) : item.label}
                onClick={() => {
                  if (item.disabled) {
                    return;
                  }
                  close();
                  item.onSelect();
                }}
                className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-xs font-medium transition hover:bg-[#557EFF]/10 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent"
              >
                {Icon && <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />}
                <span>{item.label}</span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

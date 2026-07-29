"use client";

import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from "react";
import { createPortal } from "react-dom";
import { MoreVertical, type LucideIcon } from "lucide-react";

// Menú de acciones accesible "⋯ Acciones" (HU #10194 — consolidación de config OT en
// tabla). Genérico para columnas de acciones de cualquier tabla admin: botón disparador
// (aria-haspopup="menu", aria-expanded, aria-controls) + panel `role="menu"` con
// `role="menuitem"` navegable por teclado (flechas, Home/End, Escape, cierre al perder
// foco/clic fuera). Cada ítem puede deshabilitarse con un motivo (tooltip + aria-disabled).
//
// El panel se portaliza a `document.body` con posición fixed: así no lo tapa la fila
// siguiente (stacking) ni lo recorta un `overflow-x-auto` del contenedor de la tabla.
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

type MenuCoords = { top: number; left: number; minWidth: number };

export function ActionsMenu({ items, ariaLabel, triggerLabel = "Acciones", className = "" }: ActionsMenuProps) {
  const [open, setOpen] = useState(false);
  const [coords, setCoords] = useState<MenuCoords | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const itemRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const menuId = useId();

  const updateCoords = () => {
    const btn = buttonRef.current;
    if (!btn) return;
    const rect = btn.getBoundingClientRect();
    const menuWidth = Math.max(240, rect.width);
    // Alinea el borde derecho del menú con el del botón (como absolute right-0).
    const left = Math.min(
      Math.max(8, rect.right - menuWidth),
      window.innerWidth - menuWidth - 8,
    );
    setCoords({
      top: rect.bottom + 4,
      left,
      minWidth: menuWidth,
    });
  };

  useLayoutEffect(() => {
    if (!open) {
      // Coordenadas solo importan con el menú abierto; al cerrar se limpian fuera del effect
      // (ver setOpen(false) handlers) para no disparar set-state-in-effect.
      return;
    }
    updateCoords();
    const onReposition = () => updateCoords();
    window.addEventListener("resize", onReposition);
    window.addEventListener("scroll", onReposition, true);
    return () => {
      window.removeEventListener("resize", onReposition);
      window.removeEventListener("scroll", onReposition, true);
    };
  }, [open]);

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
      setCoords(null);
    };
    document.addEventListener("mousedown", onDocMouseDown);
    return () => document.removeEventListener("mousedown", onDocMouseDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const close = (refocusTrigger = true) => {
    setOpen(false);
    setCoords(null);
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
        close(false);
        break;
      default:
        break;
    }
  };

  const menu =
    open &&
    coords &&
    typeof document !== "undefined" &&
    createPortal(
      <div
        ref={menuRef}
        id={menuId}
        role="menu"
        aria-label={ariaLabel}
        onKeyDown={onMenuKeyDown}
        style={{ top: coords.top, left: coords.left, minWidth: coords.minWidth }}
        className="fixed z-[200] rounded-xl border bg-white p-1 shadow-lg dark:bg-[#0B0F14]"
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
      </div>,
      document.body,
    );

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
        onClick={() => {
          setOpen((o) => {
            if (o) setCoords(null);
            return !o;
          });
        }}
        className="inline-flex items-center gap-1 rounded-lg border px-2.5 py-1.5 text-[11px] font-semibold transition hover:bg-[#557EFF]/10"
      >
        <MoreVertical className="h-3.5 w-3.5" aria-hidden="true" />
        {triggerLabel}
      </button>
      {menu}
    </div>
  );
}

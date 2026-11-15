"use client";

import { useEffect, useId, useLayoutEffect, useRef, useState, type ComponentType, type KeyboardEvent, type MouseEvent as ReactMouseEvent } from "react";
import { createPortal } from "react-dom";
import { Icono } from "@flit/ui/Icono";

// Menú «Acciones» propio de Diagnóstico (ola Diagnóstico UX 2026-11-15, FE-2 y U10-1). Nació como la variante `kit` de
// `@flit/ui/ActionsMenu`; se trajo aquí para no cambiar el componente compartido que usa Comparendos, que vuelve a su
// versión previa. Mismo patrón accesible: disparador (aria-haspopup="menu", aria-expanded, aria-controls) + panel
// `role="menu"` portalizado a `document.body` con posición fixed (no lo tapa la fila siguiente ni lo recorta el
// `overflow-x-auto` de la grilla) e ítems `role="menuitem"` navegables con flechas, Home/End, Escape y Tab.
//
// Lo que agregó FE-2 (spec-ui §2.2):
// - Aspecto del Manual UI Kit 2026 (`.acciones-btn` y `.menu-acciones`).
// - `onSelect(ctx)` recibe el disparador (para devolverle el foco al cerrar un modal abierto desde el menú) y el evento.
// - Ítem enlace (`href`, `target`, `download`): se pinta como `<a role="menuitem">`; con `_blank` lleva rel noopener.
// - Deshabilitado accesible: `aria-disabled` (sigue en la navegación con flechas y no hace nada) y el motivo
//   (`disabledReason`) o la ayuda (`hint`) como segunda línea visible enlazada con `aria-describedby`.
// - `ocupado`: `aria-busy` en el disparador mientras una acción está en vuelo.
// - Foco inicial en el primer ítem habilitado cuando el menú ya está pintado.

/** Contexto de `onSelect`: el disparador del menú (recibe el foco al cerrar) y el evento del clic. */
export interface MenuAccionesSelectContext {
  disparador: HTMLButtonElement | null;
  evento: ReactMouseEvent<HTMLElement>;
}

export interface MenuAccionesItem {
  key: string;
  label: string;
  icon?: ComponentType<{ className?: string; "aria-hidden"?: boolean | "true" | "false" }>;
  /** Acción al elegir. Opcional en los ítems enlace (`href`). */
  onSelect?: (ctx: MenuAccionesSelectContext) => void;
  /** Ítem enlace: navega a `href` (pestaña nueva con `target="_blank"`; descarga con `download`). */
  href?: string;
  target?: "_blank";
  download?: string | true;
  disabled?: boolean;
  /** Motivo del deshabilitado: segunda línea visible y anunciada. */
  disabledReason?: string;
  /** Ayuda de un ítem habilitado: segunda línea visible y anunciada. */
  hint?: string;
  /** Nombre accesible completo si el texto visible es corto (p. ej. «Ver deuda» → «Ver deuda de la placa ABC123»). */
  ariaLabel?: string;
  /** `danger` pinta el ítem con el tono de peligro (acciones destructivas). */
  tone?: "default" | "danger";
  /** Destaca el ítem (punto de alerta) cuando el disparador tiene `attention` por esta acción. */
  attention?: boolean;
}

export interface MenuAccionesProps {
  items: MenuAccionesItem[];
  /** Nombre accesible del botón disparador (p. ej. «Acciones de la placa ABC123»). */
  ariaLabel: string;
  /** Texto visible del botón. Por defecto «Acciones». */
  triggerLabel?: string;
  className?: string;
  /** Punto de alerta en el disparador (hay una acción pendiente). No cambia el menú. */
  attention?: boolean;
  /** Texto extra del nombre accesible y del title cuando `attention` es true. */
  attentionHint?: string;
  /** Acción de un ítem en vuelo: `aria-busy` en el disparador. */
  ocupado?: boolean;
}

type Coordenadas = { top: number; left: number; minWidth: number };

export function MenuAcciones({
  items,
  ariaLabel,
  triggerLabel = "Acciones",
  className = "",
  attention = false,
  attentionHint,
  ocupado = false,
}: MenuAccionesProps) {
  const [open, setOpen] = useState(false);
  const [coords, setCoords] = useState<Coordenadas | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const itemRefs = useRef<(HTMLElement | null)[]>([]);
  const menuId = useId();

  const updateCoords = () => {
    const btn = buttonRef.current;
    if (!btn) return;
    const rect = btn.getBoundingClientRect();
    const menuWidth = Math.max(240, rect.width);
    // Alinea el borde derecho del menú con el del botón (como absolute right-0).
    const left = Math.min(Math.max(8, rect.right - menuWidth), window.innerWidth - menuWidth - 8);
    setCoords({ top: rect.bottom + 4, left, minWidth: menuWidth });
  };

  useLayoutEffect(() => {
    if (!open) return;
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
    if (!open) return;
    const onDocMouseDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) return;
      setOpen(false);
      setCoords(null);
    };
    document.addEventListener("mousedown", onDocMouseDown);
    return () => document.removeEventListener("mousedown", onDocMouseDown);
  }, [open]);

  // Al pintarse el menú (coordenadas listas), el foco va al primer ítem habilitado (o al primero, si todos lo están).
  const menuPintado = open && coords !== null;
  useEffect(() => {
    if (!menuPintado) return;
    const primero = items.findIndex((item) => !item.disabled);
    itemRefs.current[primero >= 0 ? primero : 0]?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [menuPintado]);

  const close = (refocusTrigger = true) => {
    setOpen(false);
    setCoords(null);
    if (refocusTrigger) buttonRef.current?.focus();
  };

  // Los deshabilitados (aria-disabled) siguen en la navegación con teclado.
  const onMenuKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const total = items.length;
    if (total === 0) return;
    const actual = itemRefs.current.findIndex((el) => el === document.activeElement);
    const moveTo = (idx: number) => itemRefs.current[idx]?.focus();

    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        moveTo((actual + 1) % total);
        break;
      case "ArrowUp":
        e.preventDefault();
        moveTo((actual - 1 + total) % total);
        break;
      case "Home":
        e.preventDefault();
        moveTo(0);
        break;
      case "End":
        e.preventDefault();
        moveTo(total - 1);
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
        className="fixed z-[200] rounded-[9px] border border-[color:var(--di-border)] bg-card px-4 py-1 shadow-[var(--kit-sombra)]"
      >
        {items.map((item, i) => {
          const Icon = item.icon;
          const tono = item.attention
            ? "bg-[var(--badge-warning-bg)] text-[var(--badge-warning-fg)] hover:brightness-95"
            : item.tone === "danger"
              ? "text-[var(--badge-danger-fg)]"
              : "";
          const alElegir = (evento: ReactMouseEvent<HTMLElement>) => {
            if (item.disabled) {
              evento.preventDefault();
              return;
            }
            close();
            item.onSelect?.({ disparador: buttonRef.current, evento });
          };
          const ref = (el: HTMLElement | null) => {
            itemRefs.current[i] = el;
          };
          const marca = item.attention ? (
            <span className="inline-flex h-2 w-2 shrink-0 rounded-full bg-flit-alert" aria-hidden="true" title="Acción pendiente" />
          ) : null;

          // `.menu-acciones` del Kit 2026: ítems de 40 px separados por una línea; al pasar, texto azul.
          const motivo = item.disabled ? item.disabledReason : item.hint;
          const motivoId = motivo ? `${menuId}-${item.key}-motivo` : undefined;
          const clases = `flex min-h-10 w-full items-center gap-2 border-t border-[color:var(--di-border)] px-0 py-2 text-left text-sm font-medium transition first:border-t-0 focus-visible:outline-2 focus-visible:outline-[var(--di-action)] ${
            item.disabled ? "cursor-not-allowed" : "hover:text-[color:var(--kit-azul-texto)]"
          } ${tono}`;
          const contenido = (
            <>
              {Icon && <Icon className={`h-3.5 w-3.5 shrink-0 ${item.disabled ? "opacity-60" : ""}`} aria-hidden="true" />}
              <span className="flex min-w-0 flex-1 flex-col">
                <span className={item.disabled ? "opacity-60" : undefined}>{item.label}</span>
                {/* El motivo no lleva opacidad: conserva el contraste de 4,5:1 (WCAG 1.4.3). */}
                {motivo ? (
                  <span id={motivoId} className="block text-xs font-normal text-[color:var(--di-text-tertiary)]">
                    {motivo}
                  </span>
                ) : null}
              </span>
              {marca}
            </>
          );
          return item.href && !item.disabled ? (
            <a
              key={item.key}
              ref={ref}
              role="menuitem"
              href={item.href}
              target={item.target}
              rel={item.target === "_blank" ? "noopener noreferrer" : undefined}
              download={item.download === true ? "" : item.download}
              aria-label={item.ariaLabel ?? (motivo ? item.label : undefined)}
              aria-describedby={motivoId}
              onClick={alElegir}
              className={clases}
            >
              {contenido}
            </a>
          ) : (
            <button
              key={item.key}
              ref={ref}
              type="button"
              role="menuitem"
              aria-disabled={item.disabled || undefined}
              aria-label={item.ariaLabel ?? (motivo ? item.label : undefined)}
              aria-describedby={motivoId}
              onClick={alElegir}
              className={clases}
            >
              {contenido}
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
        aria-label={attention && attentionHint ? `${ariaLabel}. ${attentionHint}` : ariaLabel}
        aria-busy={ocupado || undefined}
        title={attention && attentionHint ? attentionHint : ariaLabel}
        onClick={() => {
          setOpen((o) => {
            if (o) setCoords(null);
            return !o;
          });
        }}
        // UI Kit 2026 (`.acciones-btn`): píldora de 32 px de alto y ≥ 44 px de ancho, borde de línea, texto de acción y
        // chevron de 16 px (se despliega una lista; no es un kebab de «más opciones»).
        className={`relative flex min-h-8 min-w-11 w-full items-center justify-between gap-1 rounded-full border py-0 pl-4 pr-2 text-sm font-semibold transition hover:bg-[var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] ${
          attention
            ? "border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] text-[var(--badge-warning-fg)] hover:brightness-95"
            : "border-[color:var(--di-border-control)] bg-[var(--di-surface-elevated)] text-[color:var(--di-action)]"
        }`}
      >
        {triggerLabel}
        <Icono nombre="abajo" className={`h-4 w-4 shrink-0 transition ${open ? "rotate-180" : ""}`} />
        {attention ? (
          <span className="pointer-events-none absolute -right-0.5 -top-0.5 flex h-2.5 w-2.5" aria-hidden="true">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-flit-alert opacity-60" />
            <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-flit-alert ring-2 ring-card" />
          </span>
        ) : null}
      </button>
      {menu}
    </div>
  );
}

"use client";

import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent, type MouseEvent as ReactMouseEvent } from "react";
import { createPortal } from "react-dom";
import { Icono } from "@flit/ui/Icono";
import type { ComponentType } from "react";

// Menú de acciones accesible "⋯ Acciones" (HU #10194 — consolidación de config OT en
// tabla). Genérico para columnas de acciones de cualquier tabla admin: botón disparador
// (aria-haspopup="menu", aria-expanded, aria-controls) + panel `role="menu"` con
// `role="menuitem"` navegable por teclado (flechas, Home/End, Escape, cierre al perder
// foco/clic fuera). Cada ítem puede deshabilitarse con un motivo (tooltip + aria-disabled).
//
// El panel se portaliza a `document.body` con posición fixed: así no lo tapa la fila
// siguiente (stacking) ni lo recorta un `overflow-x-auto` del contenedor de la tabla.
//
// Ola Diagnóstico UX 2026-11-15 (FE-2, decisión V3): extensiones aditivas, opcionales y sin efecto en el default:
// - `onSelect(ctx)` recibe el disparador (para devolverle el foco al cerrar un modal abierto desde el menú) y el evento.
// - Ítem enlace (`href`, `target`, `download`): se pinta como `<a role="menuitem">`; con `_blank` lleva rel noopener.
// - `variante="kit"`: aspecto del Manual UI Kit 2026 (el de la copia de Trámites) y deshabilitado accesible: el ítem usa
//   `aria-disabled` (sigue en la navegación con flechas, no hace nada), y el motivo (`disabledReason`) o la ayuda (`hint`)
//   se ven como segunda línea enlazada con `aria-describedby`. El default (Comparendos) conserva `disabled` + `title`.

/** Contexto de `onSelect`: el disparador del menú (recibe el foco al cerrar) y el evento del clic. */
export interface ActionsMenuSelectContext {
  disparador: HTMLButtonElement | null;
  evento: ReactMouseEvent<HTMLElement>;
}

export interface ActionsMenuItem {
  key: string;
  label: string;
  icon?: ComponentType<{ className?: string; "aria-hidden"?: boolean | "true" | "false" }>;
  /** Acción al elegir. Opcional en los ítems enlace (`href`). */
  onSelect?: (ctx: ActionsMenuSelectContext) => void;
  /** Ítem enlace: navega a `href` (pestaña nueva con `target="_blank"`; descarga con `download`). */
  href?: string;
  target?: "_blank";
  download?: string | true;
  disabled?: boolean;
  /** Motivo del deshabilitado; tooltip en el default, segunda línea visible y anunciada en `variante="kit"`. */
  disabledReason?: string;
  /** Ayuda de un ítem habilitado (solo `variante="kit"`): segunda línea visible y anunciada. */
  hint?: string;
  /** Nombre accesible completo si el texto visible es corto (p. ej. «Editar» → «Editar mandatario Ana»). */
  ariaLabel?: string;
  /** `danger` pinta el ítem de naranja de marca (acciones destructivas: desactivar, eliminar). */
  tone?: "default" | "danger";
  /**
   * Destaca el ítem (punto ámbar) cuando el disparador tiene `attention` por esta acción
   * (p. ej. "Procesar" pendiente tras asignar placa).
   */
  attention?: boolean;
}

export interface ActionsMenuProps {
  items: ActionsMenuItem[];
  /** Nombre accesible del botón disparador (p. ej. "Acciones para Secretaría de Movilidad Bogotá"). */
  ariaLabel: string;
  /** Texto visible del botón. Por defecto "Acciones". */
  triggerLabel?: string;
  className?: string;
  /**
   * Muestra un punto de alerta en el disparador (p. ej. hay una acción pendiente).
   * No cambia el menú; solo llama la atención visualmente.
   */
  attention?: boolean;
  /** Tooltip / título extra cuando `attention` es true. */
  attentionHint?: string;
  /** `kit`: aspecto del Manual UI Kit 2026 y deshabilitado accesible (ver arriba). Por defecto, el aspecto anterior. */
  variante?: "clasica" | "kit";
  /** Acción de un ítem en vuelo: `aria-busy` en el disparador. */
  ocupado?: boolean;
}

type MenuCoords = { top: number; left: number; minWidth: number };

export function ActionsMenu({
  items,
  ariaLabel,
  triggerLabel = "Acciones",
  className = "",
  attention = false,
  attentionHint,
  variante = "clasica",
  ocupado = false,
}: ActionsMenuProps) {
  const kit = variante === "kit";
  // En el kit los deshabilitados siguen en la navegación (aria-disabled); en el default, no.
  const navegable = (item: ActionsMenuItem) => kit || !item.disabled;
  const [open, setOpen] = useState(false);
  const [coords, setCoords] = useState<MenuCoords | null>(null);
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
    // Default: comportamiento previo (sin cambios). En el kit el foco inicial va en el efecto de abajo, cuando el menú ya
    // está pintado: aquí, las coordenadas aún no existen y el menú no se ha montado.
    if (!kit) itemRefs.current[items.findIndex((item) => !item.disabled)]?.focus();

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

  // Kit: al pintarse el menú (coordenadas listas), el foco va al primer ítem habilitado (o al primero navegable).
  const menuPintado = open && coords !== null;
  useEffect(() => {
    if (!kit || !menuPintado) return;
    const primero = items.findIndex((item) => !item.disabled);
    itemRefs.current[primero >= 0 ? primero : items.findIndex(navegable)]?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [menuPintado]);

  const close = (refocusTrigger = true) => {
    setOpen(false);
    setCoords(null);
    if (refocusTrigger) {
      buttonRef.current?.focus();
    }
  };

  const onMenuKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const enabledIndexes = items
      .map((item, i) => (navegable(item) ? i : -1))
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
        className={
          kit
            ? "fixed z-[200] rounded-[9px] border border-[color:var(--di-border)] bg-card px-4 py-1 shadow-[var(--kit-sombra)]"
            : "fixed z-[200] rounded-xl border bg-card p-1 shadow-[var(--shadow-modal)]"
        }
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

          if (!kit) {
            const clases = `flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-xs font-medium transition hover:bg-flit-brand/10 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent ${tono}`;
            const titulo = item.disabled ? (item.disabledReason ?? item.ariaLabel ?? item.label) : (item.ariaLabel ?? item.label);
            const contenido = (
              <>
                {Icon && <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />}
                <span className="flex-1">{item.label}</span>
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
                aria-label={item.ariaLabel}
                title={titulo}
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
                disabled={item.disabled}
                aria-disabled={item.disabled || undefined}
                aria-label={item.ariaLabel}
                title={titulo}
                onClick={alElegir}
                className={clases}
              >
                {contenido}
              </button>
            );
          }

          // Variante kit: `.menu-acciones` del Kit 2026 (ítems de 40 px separados por una línea; al pasar, texto azul).
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
        // Forma del diseño: rótulo a la izquierda y chevron a la derecha, sobre un botón con
        // borde neutro y texto de marca. El kebab de tres puntos que había antes se lee como "más
        // opciones" —algo accesorio— cuando aquí vive la acción principal de la fila: aprobar,
        // rechazar, asignar placa. El chevron dice lo que de verdad pasa: se despliega una lista.
        className={
          kit
            ? // UI Kit 2026 (`.acciones-btn`): píldora de 32 px de alto y ≥ 44 px de ancho, borde de línea, texto de acción y
              // chevron de 16 px.
              `relative flex min-h-8 min-w-11 w-full items-center justify-between gap-1 rounded-full border py-0 pl-4 pr-2 text-sm font-semibold transition hover:bg-[var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] ${
                attention
                  ? "border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] text-[var(--badge-warning-fg)] hover:brightness-95"
                  : "border-[color:var(--di-border-control)] bg-[var(--di-surface-elevated)] text-[color:var(--di-action)]"
              }`
            : `relative flex w-full items-center justify-between gap-1 rounded-xl border px-2 py-1.5 text-[11px] font-semibold transition hover:bg-flit-brand/10 ${
                attention
                  ? "border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] text-[var(--badge-warning-fg)] hover:brightness-95"
                  : "border-border bg-card text-flit-brand"
              }`
        }
      >
        {triggerLabel}
        <Icono nombre="abajo" className={`${kit ? "h-4 w-4" : "h-3.5 w-3.5"} shrink-0 transition ${open ? "rotate-180" : ""}`} />
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

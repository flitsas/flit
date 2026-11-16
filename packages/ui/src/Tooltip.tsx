"use client";

import {
  cloneElement,
  isValidElement,
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type FocusEvent,
  type KeyboardEvent,
  type MouseEvent,
  type PointerEvent,
  type ReactElement,
  type ReactNode,
  type Ref,
} from "react";
import { createPortal } from "react-dom";
import { cn } from "./cn";

/**
 * Tooltip compartido del UI Kit 2026 (HU #14584, spec-ui §5; kit `[data-tip]`). Un único hijo enfocable hace de
 * disparador; la burbuja (`role="tooltip"`) se portaliza a `document.body` con `position: fixed`.
 *
 * - Hover: abre a los 300 ms. Foco por teclado: al instante (no al enfocar con un clic del puntero).
 * - Cierre: al salir el puntero, con 100 ms de gracia y puente hasta la burbuja (WCAG 1.4.13); al perder el foco; con
 *   Esc, sin mover el foco y sin que el evento suba (no cierra el modal o menú contenedor) solo si estaba abierta.
 * - Posición: preferida abajo (o `lado`), flip si no cabe, a ≥ 8 px de los bordes; se recalcula en scroll y resize.
 * - Táctil: un toque alterna (toggletip) y un toque fuera la cierra. Si el disparador es un enlace, el toque navega.
 * - SP-19 (security-previa): por defecto el contenido se monta al abrir; cerrada no queda nada oculto en el DOM ni el
 *   disparador apunta a un id inexistente. `persistente` deja la burbuja montada (oculta con opacidad, sin aria-hidden)
 *   para que `aria-describedby` la lea siempre: úsalo solo con literales fijos sin datos personales.
 * - Disparador: un elemento host enfocable (button, a, input, select, textarea o con tabIndex) o un componente (Button,
 *   Link) recibe los manejadores por clonación, sin envoltorio: una sola parada de Tab. Solo un elemento host no
 *   enfocable (span, img) se envuelve en un span enfocable; ese span no reenvía los manejadores del hijo (ya los recibe
 *   el hijo por el burbujeo del evento), así que nunca corren dos veces.
 * - `describirHijo={false}` + `id`: el consumidor enlaza la burbuja desde un control interno (p. ej. la casilla de una
 *   etiqueta), y el hijo no se envuelve aunque no sea enfocable (el foco del control interno sube por React).
 */
export type LadoTooltip = "abajo" | "arriba";

export interface TooltipProps {
  contenido: ReactNode;
  children: ReactElement;
  lado?: LadoTooltip;
  desactivado?: boolean;
  /**
   * Deja la burbuja montada aunque esté cerrada (oculta por opacidad). PROHIBIDO con contenido de datos o PII (SP-19):
   * documentos (también enmascarados), nombres, correos o cualquier valor de una fila. Solo literales fijos de la UI.
   */
  persistente?: boolean;
  /** Id de la burbuja; por defecto uno de `useId`. */
  id?: string;
  /** Añade el id de la burbuja al `aria-describedby` del hijo (por defecto sí). */
  describirHijo?: boolean;
  className?: string;
}

/** Línea secundaria del contenido (fecha del dato, motivo): texto terciario, peso normal. */
export const TOOLTIP_SECUNDARIO_CLS = "block font-normal text-[color:var(--di-text-tertiary)]";

const RETARDO_HOVER_MS = 300;
const GRACIA_MS = 100;
const MARGEN = 8;
const SEPARACION = 8;

type Posicion = { top: number; left: number; lado: LadoTooltip; flecha: number };

const ENFOCABLES = new Set(["button", "a", "input", "select", "textarea"]);

type PropsHijo = {
  ref?: Ref<HTMLElement>;
  tabIndex?: number;
  href?: string;
  "aria-describedby"?: string;
  onPointerEnter?: (e: PointerEvent<HTMLElement>) => void;
  onPointerLeave?: (e: PointerEvent<HTMLElement>) => void;
  onPointerDown?: (e: PointerEvent<HTMLElement>) => void;
  onFocus?: (e: FocusEvent<HTMLElement>) => void;
  onBlur?: (e: FocusEvent<HTMLElement>) => void;
  onKeyDown?: (e: KeyboardEvent<HTMLElement>) => void;
  onClick?: (e: MouseEvent<HTMLElement>) => void;
};

function asignarRef<T>(ref: Ref<T> | undefined, valor: T | null) {
  if (typeof ref === "function") ref(valor);
  else if (ref && typeof ref === "object") (ref as { current: T | null }).current = valor;
}

export function Tooltip({
  contenido,
  children,
  lado = "abajo",
  desactivado = false,
  persistente = false,
  id,
  describirHijo = true,
  className,
}: TooltipProps) {
  const idPropio = useId();
  const burbujaId = id ?? `${idPropio}-tooltip`;
  const [abierto, setAbierto] = useState(false);
  const [pos, setPos] = useState<Posicion | null>(null);
  const disparadorRef = useRef<HTMLElement | null>(null);
  const burbujaRef = useRef<HTMLDivElement | null>(null);
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);
  const punteroAbajo = useRef(false);
  const ultimoPuntero = useRef<string>("mouse");

  const limpiar = () => {
    if (temporizador.current) clearTimeout(temporizador.current);
    temporizador.current = null;
  };
  const abrir = useCallback(() => {
    if (desactivado) return;
    limpiar();
    setAbierto(true);
  }, [desactivado]);
  const cerrar = useCallback(() => {
    limpiar();
    setAbierto(false);
  }, []);
  const programar = (fn: () => void, ms: number) => {
    limpiar();
    temporizador.current = setTimeout(fn, ms);
  };

  useEffect(() => limpiar, []);
  useEffect(() => {
    if (desactivado) cerrar();
  }, [desactivado, cerrar]);

  const calcular = useCallback(() => {
    const disparador = disparadorRef.current;
    const burbuja = burbujaRef.current;
    if (!disparador || !burbuja) return;
    const r = disparador.getBoundingClientRect();
    const b = burbuja.getBoundingClientRect();
    const alto = b.height;
    const ancho = b.width;
    const cabeAbajo = r.bottom + SEPARACION + alto <= window.innerHeight - MARGEN;
    const cabeArriba = r.top - SEPARACION - alto >= MARGEN;
    let final: LadoTooltip = lado;
    if (lado === "abajo" && !cabeAbajo && cabeArriba) final = "arriba";
    if (lado === "arriba" && !cabeArriba && cabeAbajo) final = "abajo";
    const top = final === "abajo" ? r.bottom + SEPARACION : r.top - SEPARACION - alto;
    const centro = r.left + r.width / 2;
    const left = Math.max(MARGEN, Math.min(centro - ancho / 2, window.innerWidth - ancho - MARGEN));
    setPos({ top, left, lado: final, flecha: Math.max(10, Math.min(centro - left, ancho - 10)) });
  }, [lado]);

  useLayoutEffect(() => {
    if (!abierto) {
      setPos(null);
      return;
    }
    calcular();
    window.addEventListener("resize", calcular);
    window.addEventListener("scroll", calcular, true);
    return () => {
      window.removeEventListener("resize", calcular);
      window.removeEventListener("scroll", calcular, true);
    };
  }, [abierto, calcular]);

  // Toque (o clic) fuera de disparador y burbuja: cierra. Esc con el puntero encima sin foco: también cierra.
  useEffect(() => {
    if (!abierto) return;
    const fuera = (e: Event) => {
      const t = e.target as Node;
      if (disparadorRef.current?.contains(t) || burbujaRef.current?.contains(t)) return;
      cerrar();
    };
    // Fase de captura: corre antes que el Escape de un Modal o menú contenedor (que escucha en burbujeo) y lo corta
    // solo cuando la burbuja estaba abierta (este efecto solo existe con `abierto`). El segundo Esc ya cierra la capa.
    const esc = (e: globalThis.KeyboardEvent) => {
      if (e.key !== "Escape") return;
      e.stopPropagation();
      cerrar();
    };
    document.addEventListener("pointerdown", fuera);
    document.addEventListener("keydown", esc, true);
    return () => {
      document.removeEventListener("pointerdown", fuera);
      document.removeEventListener("keydown", esc, true);
    };
  }, [abierto, cerrar]);

  if (!isValidElement(children)) return children;
  const hijo = children as ReactElement<PropsHijo>;
  const props = hijo.props;
  const tipo = typeof hijo.type === "string" ? hijo.type : "";
  const esEnlace = tipo === "a" && props.href !== undefined;
  // Un componente (tipo no string) se clona: se asume que reenvía props y ref a su elemento enfocable (Button, Link).
  const enfocable = tipo === "" || ENFOCABLES.has(tipo) || props.tabIndex !== undefined;
  const envolver = describirHijo && !enfocable;
  const montada = abierto || persistente;
  // En el envoltorio, los manejadores del hijo ya corren por el burbujeo del evento: no se vuelven a llamar.
  const delHijo: PropsHijo = envolver ? {} : props;

  const handlers: PropsHijo = {
    onPointerEnter: (e) => {
      delHijo.onPointerEnter?.(e);
      ultimoPuntero.current = e.pointerType;
      if (e.pointerType === "touch") return;
      if (abierto) limpiar();
      else programar(abrir, RETARDO_HOVER_MS);
    },
    onPointerLeave: (e) => {
      delHijo.onPointerLeave?.(e);
      if (e.pointerType === "touch") return;
      programar(cerrar, GRACIA_MS);
    },
    onPointerDown: (e) => {
      delHijo.onPointerDown?.(e);
      ultimoPuntero.current = e.pointerType;
      punteroAbajo.current = true;
      setTimeout(() => {
        punteroAbajo.current = false;
      }, 0);
    },
    onFocus: (e) => {
      delHijo.onFocus?.(e);
      // Foco por teclado: al instante. El foco que trae un clic o un toque no la abre (el toque usa onClick).
      if (!punteroAbajo.current) abrir();
    },
    onBlur: (e) => {
      delHijo.onBlur?.(e);
      const siguiente = e.relatedTarget as Node | null;
      if (siguiente && burbujaRef.current?.contains(siguiente)) return;
      cerrar();
    },
    onKeyDown: (e) => {
      if (e.key === "Escape" && abierto) {
        e.stopPropagation();
        cerrar();
        return;
      }
      delHijo.onKeyDown?.(e);
    },
    onClick: (e) => {
      delHijo.onClick?.(e);
      if (ultimoPuntero.current !== "touch" || esEnlace) return;
      if (abierto) cerrar();
      else abrir();
    },
  };

  const describedBy = [props["aria-describedby"], describirHijo && montada ? burbujaId : null].filter(Boolean).join(" ") || undefined;

  const disparador = envolver ? (
    <span
      ref={(el) => {
        disparadorRef.current = el;
      }}
      tabIndex={0}
      aria-describedby={montada ? burbujaId : undefined}
      className="inline-flex rounded-[inherit] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]"
      {...handlers}
    >
      {hijo}
    </span>
  ) : (
    cloneElement(hijo, {
      ...handlers,
      ref: (el: HTMLElement | null) => {
        disparadorRef.current = el;
        asignarRef(props.ref, el);
      },
      ...(describirHijo ? { "aria-describedby": describedBy } : {}),
    })
  );

  const visible = abierto && pos !== null;
  const burbuja =
    montada && typeof document !== "undefined"
      ? createPortal(
          <div
            ref={burbujaRef}
            id={burbujaId}
            role="tooltip"
            data-lado={pos?.lado ?? lado}
            data-abierto={visible ? "true" : "false"}
            onPointerEnter={(e) => {
              if (e.pointerType !== "touch") limpiar();
            }}
            onPointerLeave={(e) => {
              if (e.pointerType !== "touch") programar(cerrar, GRACIA_MS);
            }}
            style={{ top: pos?.top ?? 0, left: pos?.left ?? 0 }}
            className={cn(
              "fixed z-[60] w-max max-w-[18rem] rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] px-2.5 py-1.5 text-[0.8125rem] font-medium leading-snug text-[color:var(--di-action)] motion-safe:transition-opacity motion-safe:duration-[var(--di-motion-fast)]",
              visible ? "opacity-100" : "pointer-events-none opacity-0",
              className,
            )}
          >
            {contenido}
            {/* Flecha de 6 px con el mismo borde y fondo, apuntando al disparador; se mueve con el flip. */}
            <span
              aria-hidden="true"
              style={{ left: pos?.flecha ?? 12 }}
              className={cn(
                "absolute h-1.5 w-1.5 -translate-x-1/2 rotate-45 border-[color:var(--di-border)] bg-[var(--di-surface-elevated)]",
                (pos?.lado ?? lado) === "abajo" ? "-top-[4px] border-l border-t" : "-bottom-[4px] border-b border-r",
              )}
            />
          </div>,
          document.body,
        )
      : null;

  return (
    <>
      {disparador}
      {burbuja}
    </>
  );
}

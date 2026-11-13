"use client";

import { createContext, useContext, useEffect, useRef, type RefObject } from "react";

/**
 * Trampa de foco de las capas modales del kit (HU #14562 · M2 y m-6 de la auditoría de la oleada): `Modal` y
 * `PanelLateral` la comparten.
 *
 * - Al activarse, el foco entra a la capa. Si un hijo ya lo puso dentro durante su montaje (autoFocus o un efecto),
 *   se respeta. Si no, va a `focoInicial()`, después al primer control enfocable y, por último, al contenedor. Si el
 *   destino elegido no acepta el foco, va al contenedor: nunca queda en body (HU #14562 · N1).
 * - Tab y Mayús+Tab circulan dentro de la capa. Si el foco quedó fuera, Tab lo regresa.
 * - Un control oculto por estilo no está en el orden de Tab (HU #14562 · N1): `display: none` propio o de un ancestro
 *   dentro de la capa, o `visibility: hidden` calculado. Por ejemplo, el `<input type="file" class="hidden">` que dispara
 *   un botón «Elegir archivo».
 * - Escape llama a `onEscape`. Quien llama decide si cierra, por ejemplo cuando está ocupado.
 * - Las capas se apilan: solo la de arriba atiende Tab y Escape. Un Modal abierto sobre un PanelLateral no cierra
 *   los dos con un Escape, y el panel no le quita el Tab al modal.
 * - Orden de la pila (observación 7 del code review): manda la capa que se abrió después. Si dos capas se montan en el
 *   mismo render, por ejemplo un PanelLateral que nace abierto con su Modal ya abierto, React corre primero el efecto
 *   del hijo y el orden de montaje las dejaría invertidas. Por eso, las capas que se montan en el mismo tick (antes de
 *   la siguiente microtarea) se ordenan por su profundidad en el árbol de React: la anidada queda arriba. Entre capas
 *   hermanas del mismo tick manda la última montada, que es también la última del DOM y la que se pinta encima.
 *   Modal y PanelLateral publican su profundidad con `ProfundidadCapa`; una capa propia sin ese proveedor cuenta como 0.
 * - Al desactivarse, el foco vuelve al elemento que lo tenía antes de abrir, pero solo si se perdió (quedó en `body`
 *   o en un nodo desmontado). Si quien cerró ya lo movió a otro lado, no se pisa.
 */

/** Controles que pueden recibir el foco con Tab. Después se descartan los ocultos y los que tienen tabindex negativo. */
export const SELECTOR_ENFOCABLES = [
  "a[href]",
  "area[href]",
  "button:not([disabled])",
  'input:not([disabled]):not([type="hidden"])',
  "select:not([disabled])",
  "textarea:not([disabled])",
  "iframe",
  "summary",
  '[contenteditable]:not([contenteditable="false"])',
  "[tabindex]",
].join(", ");

/**
 * HU #14562 · N1: oculto por estilo. `display: none` en el control o en un ancestro hasta la capa lo saca del render.
 * `visibility` se hereda, así que basta su valor calculado en el propio control: un hijo con `visibility: visible`
 * dentro de un padre oculto sí se ve y sí recibe el foco. Sin `getComputedStyle` (SSR) no se descarta nada.
 */
function ocultoPorEstilo(el: HTMLElement, capa: HTMLElement): boolean {
  if (typeof window === "undefined" || typeof window.getComputedStyle !== "function") return false;
  const propio = window.getComputedStyle(el);
  if (propio.display === "none" || propio.visibility === "hidden" || propio.visibility === "collapse") return true;
  for (let nodo = el.parentElement; nodo && nodo !== capa.parentElement; nodo = nodo.parentElement) {
    const estilo = window.getComputedStyle(nodo);
    if (estilo.display === "none") return true;
    // jsdom no hereda `visibility`; en el navegador el valor del control ya lo trae. Un `visible` propio lo anula.
    if (estilo.visibility === "hidden" && propio.visibility !== "visible") return true;
    if (nodo === capa) break;
  }
  return false;
}

function visibleParaTab(el: HTMLElement, capa: HTMLElement): boolean {
  // Con tabindex explícito manda su valor; sin él, el control es enfocable por su tipo (el selector ya lo filtró).
  const tabindex = el.getAttribute("tabindex");
  if (tabindex !== null && Number(tabindex) < 0) return false;
  if (el.closest("[hidden], [inert]")) return false;
  // Dentro de un <details> cerrado solo se alcanza su <summary>.
  const details = el.parentElement?.closest("details");
  if (details && !details.open && !(el.tagName === "SUMMARY" && el.parentElement === details)) return false;
  return !ocultoPorEstilo(el, capa);
}

/** Controles de la capa en el orden de Tab (orden del DOM). */
export function enfocablesDe(contenedor: HTMLElement | null): HTMLElement[] {
  if (!contenedor) return [];
  return Array.from(contenedor.querySelectorAll<HTMLElement>(SELECTOR_ENFOCABLES)).filter((el) => visibleParaTab(el, contenedor));
}

/**
 * Profundidad de la capa en el árbol de React. Modal y PanelLateral envuelven su contenido con `profundidad + 1`; así una
 * capa anidada sabe que va encima de la que la contiene aunque las dos se monten en el mismo render.
 */
export const ProfundidadCapa = createContext(0);

interface EntradaPila {
  id: symbol;
  /** Tick en que se montó la capa: las de un mismo tick se ordenan por profundidad. */
  tick: number;
  profundidad: number;
  /** Orden de montaje, para desempatar. */
  orden: number;
}

const pila: EntradaPila[] = [];
let tickActual = 0;
let tickAbierto = false;
let montajes = 0;

/** Número del tick en curso: todas las capas que se montan antes de la siguiente microtarea comparten el mismo. */
function tickDeMontaje(): number {
  if (!tickAbierto) {
    tickAbierto = true;
    tickActual += 1;
    queueMicrotask(() => {
      tickAbierto = false;
    });
  }
  return tickActual;
}

/** La capa de arriba: la del tick más reciente; en el mismo tick, la más profunda y, a igual profundidad, la última. */
function capaSuperior(): symbol | undefined {
  let arriba: EntradaPila | undefined;
  for (const e of pila) {
    const despues =
      !arriba ||
      e.tick > arriba.tick ||
      (e.tick === arriba.tick && (e.profundidad > arriba.profundidad || (e.profundidad === arriba.profundidad && e.orden > arriba.orden)));
    if (despues) arriba = e;
  }
  return arriba?.id;
}

/** Enfoca `destino`; si no acepta el foco (no es enfocable o no se pinta), enfoca el contenedor de la capa. */
function enfocarConRespaldo(destino: HTMLElement, capa: HTMLElement): void {
  destino.focus();
  if (document.activeElement !== destino && destino !== capa) capa.focus();
}

export interface OpcionesTrampaFoco {
  /** Capa abierta. */
  activa: boolean;
  /** Elemento que delimita la capa (el panel del diálogo). */
  contenedor: RefObject<HTMLElement | null>;
  /** Escape con la capa arriba. Se lee al momento, así que puede cambiar entre renders sin reiniciar la trampa. */
  onEscape?: () => void;
  /** Destino del foco inicial. Si devuelve null o undefined, va al primer control y luego al contenedor. */
  focoInicial?: () => HTMLElement | null | undefined;
}

export function useTrampaFoco({ activa, contenedor, onEscape, focoInicial }: OpcionesTrampaFoco): void {
  const profundidad = useContext(ProfundidadCapa);
  const profundidadRef = useRef(profundidad);
  const escapeRef = useRef(onEscape);
  const inicialRef = useRef(focoInicial);
  useEffect(() => {
    profundidadRef.current = profundidad;
    escapeRef.current = onEscape;
    inicialRef.current = focoInicial;
  });

  useEffect(() => {
    if (!activa) return;
    const capa = Symbol("capa");
    montajes += 1;
    pila.push({ id: capa, tick: tickDeMontaje(), profundidad: profundidadRef.current, orden: montajes });
    const previo = typeof document !== "undefined" ? (document.activeElement as HTMLElement | null) : null;
    const nodo = () => contenedor.current;

    const raiz = nodo();
    if (raiz && !raiz.contains(document.activeElement)) {
      const destino = inicialRef.current?.() ?? enfocablesDe(raiz)[0] ?? raiz;
      enfocarConRespaldo(destino, raiz);
    }

    const alTeclear = (e: KeyboardEvent) => {
      if (capaSuperior() !== capa) return;
      if (e.key === "Escape") {
        escapeRef.current?.();
        return;
      }
      if (e.key !== "Tab") return;
      const panel = nodo();
      if (!panel) return;
      const lista = enfocablesDe(panel);
      if (lista.length === 0) {
        e.preventDefault();
        panel.focus();
        return;
      }
      const primero = lista[0];
      const ultimo = lista[lista.length - 1];
      const actual = document.activeElement as HTMLElement | null;
      const dentro = !!actual && panel.contains(actual);
      const indice = actual ? lista.indexOf(actual) : -1;
      // Un elemento de la capa que no está en el orden de Tab (el título con tabindex=-1 o el propio panel).
      const antesDelPrimero =
        dentro && indice === -1 && !!actual && (actual === panel || !!(actual.compareDocumentPosition(primero) & Node.DOCUMENT_POSITION_FOLLOWING));
      const despuesDelUltimo =
        dentro && indice === -1 && !!actual && actual !== panel && !!(actual.compareDocumentPosition(ultimo) & Node.DOCUMENT_POSITION_PRECEDING);
      if (e.shiftKey) {
        if (!dentro || indice === 0 || antesDelPrimero) {
          e.preventDefault();
          ultimo.focus();
        }
      } else if (!dentro || indice === lista.length - 1 || despuesDelUltimo) {
        e.preventDefault();
        primero.focus();
      }
    };
    document.addEventListener("keydown", alTeclear);

    return () => {
      document.removeEventListener("keydown", alTeclear);
      const i = pila.findIndex((e) => e.id === capa);
      if (i >= 0) pila.splice(i, 1);
      const ahora = document.activeElement;
      const perdido = !ahora || ahora === document.body || !ahora.isConnected;
      if (perdido && previo && previo.isConnected && typeof previo.focus === "function") previo.focus();
    };
    // `contenedor` es un ref estable; la trampa solo se rearma al abrir o cerrar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activa]);
}

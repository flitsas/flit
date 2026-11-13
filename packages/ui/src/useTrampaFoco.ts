"use client";

import { useEffect, useRef, type RefObject } from "react";

/**
 * Trampa de foco de las capas modales del kit (HU #14562 · M2 y m-6 de la auditoría de la oleada): `Modal` y
 * `PanelLateral` la comparten.
 *
 * - Al activarse, el foco entra a la capa. Si un hijo ya lo puso dentro durante su montaje (autoFocus o un efecto),
 *   se respeta. Si no, va a `focoInicial()`, después al primer control enfocable y, por último, al contenedor.
 * - Tab y Mayús+Tab circulan dentro de la capa. Si el foco quedó fuera, Tab lo regresa.
 * - Escape llama a `onEscape`. Quien llama decide si cierra, por ejemplo cuando está ocupado.
 * - Las capas se apilan: solo la de arriba atiende Tab y Escape. Un Modal abierto sobre un PanelLateral no cierra
 *   los dos con un Escape, y el panel no le quita el Tab al modal.
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

function visibleParaTab(el: HTMLElement): boolean {
  // Con tabindex explícito manda su valor; sin él, el control es enfocable por su tipo (el selector ya lo filtró).
  const tabindex = el.getAttribute("tabindex");
  if (tabindex !== null && Number(tabindex) < 0) return false;
  if (el.closest("[hidden], [inert]")) return false;
  // Dentro de un <details> cerrado solo se alcanza su <summary>.
  const details = el.parentElement?.closest("details");
  if (details && !details.open && !(el.tagName === "SUMMARY" && el.parentElement === details)) return false;
  return true;
}

/** Controles de la capa en el orden de Tab (orden del DOM). */
export function enfocablesDe(contenedor: HTMLElement | null): HTMLElement[] {
  if (!contenedor) return [];
  return Array.from(contenedor.querySelectorAll<HTMLElement>(SELECTOR_ENFOCABLES)).filter(visibleParaTab);
}

const pila: symbol[] = [];

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
  const escapeRef = useRef(onEscape);
  const inicialRef = useRef(focoInicial);
  useEffect(() => {
    escapeRef.current = onEscape;
    inicialRef.current = focoInicial;
  });

  useEffect(() => {
    if (!activa) return;
    const capa = Symbol("capa");
    pila.push(capa);
    const previo = typeof document !== "undefined" ? (document.activeElement as HTMLElement | null) : null;
    const nodo = () => contenedor.current;

    const raiz = nodo();
    if (raiz && !raiz.contains(document.activeElement)) {
      const destino = inicialRef.current?.() ?? enfocablesDe(raiz)[0] ?? raiz;
      destino.focus();
    }

    const alTeclear = (e: KeyboardEvent) => {
      if (pila[pila.length - 1] !== capa) return;
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
      const i = pila.lastIndexOf(capa);
      if (i >= 0) pila.splice(i, 1);
      const ahora = document.activeElement;
      const perdido = !ahora || ahora === document.body || !ahora.isConnected;
      if (perdido && previo && previo.isConnected && typeof previo.focus === "function") previo.focus();
    };
    // `contenedor` es un ref estable; la trampa solo se rearma al abrir o cerrar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activa]);
}

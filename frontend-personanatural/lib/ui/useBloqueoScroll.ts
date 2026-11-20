// Copia de @flit/ui/useBloqueoScroll (packages/ui/src/useBloqueoScroll.ts): si cambia allá, cambia aquí.
// Persona natural no depende de @flit/ui (contrato responsive S4), por eso vive copiado.
"use client";

import { useEffect } from "react";

/**
 * Bloquea el scroll de la página mientras una capa (Modal, PanelLateral, hoja del dock) está abierta.
 *
 * En iOS (Safari y Chrome, ambos WebKit) `overflow: hidden` en `body` no basta: la página de atrás sigue moviéndose
 * al arrastrar dentro de la capa. Lo que sí funciona es fijar `body` con `position: fixed` y `top: -scrollY`, y al
 * soltar devolver la posición con `scrollTo`.
 *
 * - Cuenta las capas abiertas: el bloqueo se pone con la primera y se quita con la última, así un Modal sobre un
 *   PanelLateral no libera el scroll al cerrarse.
 * - Ancla el ancho (`left: 0; right: 0`) para que la página no se mueva de lado al fijarla.
 * - Compensa el ancho de la barra de scroll en escritorio para que el contenido no salte (CLS), tanto la de la ventana
 *   como la del contenedor del shell (Windows/Linux, donde la barra ocupa espacio).
 * - Devuelve la posición sin animar (`behavior: "instant"`), aunque la app tenga `scroll-behavior: smooth`.
 * - Si el scroll de la app vive en un contenedor (el `[data-shell-scroll]` de SuiteShell) y no en la ventana, el
 *   contenedor también recibe `overflow: hidden` mientras dura el bloqueo.
 */

let capasAbiertas = 0;
let restaurar: (() => void) | null = null;

function bloquear(): () => void {
  const body = document.body;
  const html = document.documentElement;
  const scrollY = window.scrollY;
  const anchoBarra = window.innerWidth - html.clientWidth;
  const contenedor = document.querySelector<HTMLElement>("[data-shell-scroll]");

  const previo = {
    position: body.style.position,
    top: body.style.top,
    left: body.style.left,
    right: body.style.right,
    width: body.style.width,
    overflow: body.style.overflow,
    paddingRight: body.style.paddingRight,
    contenedorOverflow: contenedor?.style.overflow ?? "",
    contenedorPaddingRight: contenedor?.style.paddingRight ?? "",
  };

  body.style.position = "fixed";
  body.style.top = `-${scrollY}px`;
  body.style.left = "0";
  body.style.right = "0";
  body.style.width = "100%";
  body.style.overflow = "hidden";
  if (anchoBarra > 0) body.style.paddingRight = `${anchoBarra}px`;
  if (contenedor) {
    const barraContenedor = contenedor.offsetWidth - contenedor.clientWidth;
    contenedor.style.overflow = "hidden";
    if (barraContenedor > 0) {
      const base = parseFloat(getComputedStyle(contenedor).paddingRight) || 0;
      contenedor.style.paddingRight = `${base + barraContenedor}px`;
    }
  }

  return () => {
    body.style.position = previo.position;
    body.style.top = previo.top;
    body.style.left = previo.left;
    body.style.right = previo.right;
    body.style.width = previo.width;
    body.style.overflow = previo.overflow;
    body.style.paddingRight = previo.paddingRight;
    if (contenedor) {
      contenedor.style.overflow = previo.contenedorOverflow;
      contenedor.style.paddingRight = previo.contenedorPaddingRight;
    }
    window.scrollTo({ top: scrollY, left: 0, behavior: "instant" });
  };
}

/**
 * Pide un bloqueo y devuelve la función que lo suelta (idempotente). Se exporta para capas que no son componentes
 * React y para las pruebas.
 */
export function adquirirBloqueoScroll(): () => void {
  capasAbiertas += 1;
  if (capasAbiertas === 1) restaurar = bloquear();
  let suelto = false;
  return () => {
    if (suelto) return;
    suelto = true;
    capasAbiertas -= 1;
    if (capasAbiertas === 0 && restaurar) {
      restaurar();
      restaurar = null;
    }
  };
}

export function useBloqueoScroll(activo: boolean): void {
  useEffect(() => {
    if (!activo || typeof document === "undefined") return;
    return adquirirBloqueoScroll();
  }, [activo]);
}

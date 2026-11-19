import { fireEvent, screen, within } from "@testing-library/react";

// Ola Diagnóstico UX 2026-11-15 · FE-2: las acciones de cada vehículo del tablero viven en el menú «Acciones de la placa X»
// (portalizado a body). Estos atajos abren el menú de una fila y devuelven el ítem por su nombre accesible.

type Nombre = string | RegExp;

/** Disparador «Acciones» de la placa (o null si la fila no tiene acciones). */
export const disparadorDe = (placa: string, raiz?: HTMLElement) =>
  (raiz ? within(raiz) : screen).queryByRole("button", { name: `Acciones de la placa ${placa}` });

export function cerrarMenu() {
  const menu = screen.queryByRole("menu");
  if (menu) fireEvent.keyDown(menu, { key: "Escape" });
}

/** Abre el menú de la placa (cierra antes el de otra fila) y devuelve el disparador. */
export function abrirMenu(placa: string): HTMLElement {
  const disparador = disparadorDe(placa);
  if (!disparador) throw new Error(`La placa ${placa} no tiene menú de acciones`);
  if (disparador.getAttribute("aria-expanded") !== "true") {
    cerrarMenu();
    fireEvent.click(disparador);
  }
  return disparador;
}

/** Ítem del menú de la placa por nombre accesible; falla si no existe. */
export function accionFila(placa: string, nombre: Nombre): HTMLElement {
  abrirMenu(placa);
  return screen.getByRole("menuitem", { name: nombre });
}

/** Ítem del menú de la placa o null (sin menú o sin el ítem). Deja el menú cerrado. */
export function quizaAccionFila(placa: string, nombre: Nombre): HTMLElement | null {
  if (!disparadorDe(placa)) return null;
  abrirMenu(placa);
  const item = screen.queryByRole("menuitem", { name: nombre });
  cerrarMenu();
  return item;
}

/** Nombres accesibles de los ítems del menú de la placa, en orden. Deja el menú cerrado. */
export function accionesDeFila(placa: string): string[] {
  if (!disparadorDe(placa)) return [];
  abrirMenu(placa);
  const nombres = screen.getAllByRole("menuitem").map((i) => i.getAttribute("aria-label") ?? i.textContent?.trim() ?? "");
  cerrarMenu();
  return nombres;
}

/** Todas las placas con menú en pantalla. */
export const placasConMenu = () =>
  screen.queryAllByRole("button", { name: /^Acciones de la placa / }).map((b) => b.getAttribute("aria-label")!.replace("Acciones de la placa ", ""));

/** Ítems de todas las filas cuyo nombre coincide. */
export const accionesEnTodas = (nombre: RegExp) => placasConMenu().flatMap((p) => accionesDeFila(p).filter((n) => nombre.test(n)));

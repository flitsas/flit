// Ayudantes de pruebas para las tablas que agrupan sus acciones en un único botón «Acciones» con menú
// (RowActionsMenu / ActionsMenu). Abren los menús de la pantalla, buscan la acción por su nombre accesible y la
// pulsan, o dicen si existe. Solo se usan en tests.
import { screen, waitFor, within } from "@testing-library/react";
import { expect } from "vitest";
import type { UserEvent } from "@testing-library/user-event";

const disparadores = (en?: HTMLElement): HTMLElement[] =>
  (en ? within(en) : screen)
    .queryAllByRole("button")
    .filter((b) => b.getAttribute("aria-haspopup") === "menu");

async function abrir(user: UserEvent, d: HTMLElement) {
  await user.click(d);
  // Espera al menú de ESTE disparador (el anterior se cierra solo al hacer clic fuera).
  const nombre = d.getAttribute("aria-label") ?? undefined;
  await waitFor(() => expect(screen.getByRole("menu", nombre ? { name: nombre } : undefined)).toBeInTheDocument());
}

async function cerrar(user: UserEvent) {
  if (!screen.queryByRole("menu")) return;
  await user.keyboard("{Escape}");
  // Si el entorno no lo cierra con Escape, un clic fuera lo hace (comportamiento real del menú).
  if (screen.queryByRole("menu")) await user.click(document.body);
}

async function recorrer(user: UserEvent, nombre: string | RegExp, en?: HTMLElement): Promise<HTMLElement | null> {
  for (const d of disparadores(en)) {
    await abrir(user, d);
    const item = screen.queryByRole("menuitem", { name: nombre });
    if (item) return item;
    await cerrar(user);
  }
  // Una acción que no está en un menú (p. ej. el botón de la tarjeta del mandatario general).
  return (en ? within(en) : screen).queryByRole("button", { name: nombre });
}

/**
 * Abre los menús uno a uno hasta encontrar la acción; la deja abierta y devuelve el ítem (o null). Reintenta unos
 * instantes: la pantalla suele cargar sus datos de forma asíncrona.
 */
export async function buscarAccion(
  user: UserEvent,
  nombre: string | RegExp,
  en?: HTMLElement,
  intentos = 12,
): Promise<HTMLElement | null> {
  for (let i = 0; i < intentos; i++) {
    const item = await recorrer(user, nombre, en);
    if (item) return item;
    await new Promise((r) => setTimeout(r, 100));
  }
  return null;
}

/** Abre el menú «Acciones» de la fila (por su nombre accesible) y pulsa la acción. */
export async function pulsarAccion(user: UserEvent, nombre: string | RegExp, en?: HTMLElement): Promise<void> {
  const item = await buscarAccion(user, nombre, en);
  if (!item) throw new Error(`No se encontró la acción «${String(nombre)}» en ningún menú «Acciones».`);
  await user.click(item);
}

/** ¿Alguna fila ofrece esa acción? Deja los menús cerrados. */
export async function hayAccion(
  user: UserEvent,
  nombre: string | RegExp,
  en?: HTMLElement,
  /** Con `false` no espera: para afirmar que una acción NO está (evita 1 s de espera por comprobación). */
  esperar = true,
): Promise<boolean> {
  const item = await buscarAccion(user, nombre, en, esperar ? 12 : 1);
  await cerrar(user);
  return item !== null;
}

/** ¿La acción existe pero está deshabilitada? (null si no existe). */
export async function accionDeshabilitada(
  user: UserEvent,
  nombre: string | RegExp,
  en?: HTMLElement,
): Promise<boolean | null> {
  const item = await buscarAccion(user, nombre, en);
  if (!item) return null;
  const off = item.hasAttribute("disabled") || item.getAttribute("aria-disabled") === "true";
  await cerrar(user);
  return off;
}

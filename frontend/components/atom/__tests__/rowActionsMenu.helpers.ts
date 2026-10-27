// Ayudas de prueba para las tablas cuya columna de acciones es UN botón «Acciones» con menú (RowActionsMenu /
// ActionsMenu). El botón se llama «Acciones de {fila}» y cada opción es un `menuitem` con el nombre accesible completo
// («Editar usuario Ana»). El menú se portaliza a `document.body`: las opciones se buscan siempre en `screen`.
import { screen, within } from "@testing-library/react";

interface UserLike {
  click: (element: Element) => Promise<unknown>;
  keyboard: (text: string) => Promise<unknown>;
}

type Scope = Pick<typeof screen, "findByRole" | "queryByRole"> | ReturnType<typeof within>;

function triggerName(rowName: string | RegExp): string | RegExp {
  return typeof rowName === "string" ? `Acciones de ${rowName}` : new RegExp(`^Acciones de .*${rowName.source}`, rowName.flags);
}

/** Abre el menú «Acciones» de la fila. */
export async function openRowActions(user: UserLike, rowName: string | RegExp, scope: Scope = screen): Promise<void> {
  await user.click(await scope.findByRole("button", { name: triggerName(rowName) }));
}

/** Abre el menú «Acciones» de la fila y elige la opción (por su nombre accesible). */
export async function chooseRowAction(
  user: UserLike,
  rowName: string | RegExp,
  action: string | RegExp,
  scope: Scope = screen,
): Promise<void> {
  await openRowActions(user, rowName, scope);
  await user.click(await screen.findByRole("menuitem", { name: action }));
}

/** Abre el menú, devuelve la opción (sin elegirla) para revisar su estado; el menú queda abierto. */
export async function findRowAction(
  user: UserLike,
  rowName: string | RegExp,
  action: string | RegExp,
  scope: Scope = screen,
): Promise<HTMLElement> {
  await openRowActions(user, rowName, scope);
  return screen.findByRole("menuitem", { name: action });
}

/**
 * Nombres accesibles de las opciones del menú de la fila (lo abre y lo cierra). Si la fila no tiene botón
 * «Acciones» devuelve una lista vacía. La fila ya debe estar en pantalla (espérala antes con `findByText`).
 */
export async function rowActionNames(user: UserLike, rowName: string | RegExp, scope: Scope = screen): Promise<string[]> {
  const trigger = scope.queryByRole("button", { name: triggerName(rowName) });
  if (!trigger) return [];
  await user.click(trigger);
  const items = await screen.findAllByRole("menuitem");
  const names = items.map((m) => m.getAttribute("aria-label") ?? m.textContent ?? "");
  await user.keyboard("{Escape}");
  // Si el foco no estaba en el menú (p. ej. todas las opciones deshabilitadas), se cierra con el mismo botón.
  if (screen.queryByRole("menu")) await user.click(trigger);
  return names;
}

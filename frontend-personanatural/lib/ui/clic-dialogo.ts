/**
 * El clic en el fondo del `<dialog>` (el `::backdrop`) llega al propio diálogo, igual que el clic en su relleno.
 * Solo cuenta como «fuera» cuando el clic cae sobre el propio diálogo (no sobre un hijo) y el puntero está fuera de la caja
 * del modal: un clic en el espacio en blanco de adentro no cierra.
 * Un botón accionado con el teclado (Enter en un campo envía el formulario con un clic sintético en «Enviar») burbujea con
 * coordenadas 0,0 desde un hijo; sin mirar el `target` eso contaba como «fuera» y cerraba el modal al enviar con Enter.
 */
export function clicFueraDelDialogo(e: { target: EventTarget; currentTarget: HTMLElement; clientX: number; clientY: number }): boolean {
  if (e.target !== e.currentTarget) return false;
  const cuadro = e.currentTarget.getBoundingClientRect();
  return e.clientX < cuadro.left || e.clientX > cuadro.right || e.clientY < cuadro.top || e.clientY > cuadro.bottom;
}

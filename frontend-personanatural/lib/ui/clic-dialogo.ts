/**
 * El clic en el fondo del `<dialog>` (el `::backdrop`) llega al propio diálogo, igual que el clic en su relleno.
 * Solo cuenta como «fuera» cuando el puntero está fuera de la caja del modal: un clic en el espacio en blanco de adentro no cierra.
 */
export function clicFueraDelDialogo(e: { currentTarget: HTMLElement; clientX: number; clientY: number }): boolean {
  const cuadro = e.currentTarget.getBoundingClientRect();
  return e.clientX < cuadro.left || e.clientX > cuadro.right || e.clientY < cuadro.top || e.clientY > cuadro.bottom;
}

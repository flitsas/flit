/** Lleva a la persona al login del hub y vuelve a la pantalla en que estaba. Separado para poder sustituirlo en las pruebas. */
export function irAlLogin(destino: Window["location"] = window.location): void {
  const retorno = `${destino.pathname}${destino.search}`;
  destino.assign(`/auth/login?returnTo=${encodeURIComponent(retorno)}`);
}

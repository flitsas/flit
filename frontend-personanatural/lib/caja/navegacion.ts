/** Lleva a la persona al login del hub y vuelve a la pantalla en que estaba. Separado para poder sustituirlo en las pruebas. */
export function irAlLogin(destino: Window["location"] = window.location): void {
  const retorno = `${destino.pathname}${destino.search}`;
  destino.assign(`/auth/login?returnTo=${encodeURIComponent(retorno)}`);
}

/** Cierra la sesión: POST same-origin que borra la cookie local y devuelve la URL de cierre del hub, a la que se navega. */
export async function cerrarSesion(destino: Window["location"] = window.location): Promise<void> {
  try {
    const respuesta = await fetch("/auth/logout", { method: "POST", credentials: "same-origin", cache: "no-store" });
    const { redirectTo } = (await respuesta.json()) as { redirectTo?: unknown };
    if (respuesta.ok && typeof redirectTo === "string") return destino.assign(redirectTo);
  } catch {
    // Sin red o respuesta ilegible: se vuelve al inicio; la caja sigue exigiendo login.
  }
  destino.assign("/");
}

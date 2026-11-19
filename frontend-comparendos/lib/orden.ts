// Texto del orden de la bandeja (HU #13785). El control muestra cómo está ordenada ahora, con palabras propias de cada
// campo: antes decía «Menor a mayor» / «Mayor a menor» y se leía como la acción del botón, al revés de lo que pasaba.

const FECHAS = new Set(["imposicion", "notificacion", "actividad"]);

/** Cómo está ordenada la bandeja con este campo y esta dirección. */
export function textoOrden(orden: string, descendente: boolean): string {
  if (orden === "valor") return descendente ? "De mayor a menor valor" : "De menor a mayor valor";
  if (FECHAS.has(orden)) return descendente ? "Más recientes primero" : "Más antiguas primero";
  return descendente ? "Descendente" : "Ascendente";
}

/** Lo que pasa al hacer clic: el orden contrario. */
export function textoCambioOrden(orden: string, descendente: boolean): string {
  return `Cambiar a: ${textoOrden(orden, !descendente).toLowerCase()}`;
}

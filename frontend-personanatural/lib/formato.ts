// Formatos de presentación escritos a mano: la imagen de producción corre con globalización invariante, así que no se
// usa Intl ni toLocaleString con «es-CO» (saldría con otro separador o sin formato).

const UTC_COLOMBIA_HORAS = -5; // Colombia no tiene horario de verano.

/** «$ 185.000 COP», igual que el diseño de referencia. Redondea a pesos enteros; un valor no numérico queda en «$ 0 COP». */
export function formatearPesos(valor: number): string {
  const entero = Number.isFinite(valor) ? Math.round(Math.abs(valor)) : 0;
  const conPuntos = String(entero).replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  return `${valor < 0 && entero !== 0 ? "-" : ""}$ ${conPuntos} COP`;
}

const dos = (n: number) => String(n).padStart(2, "0");

/** «dd/mm/aaaa hh:mm» en hora de Colombia, o null si no hay fecha o no se puede leer. */
export function formatearFechaHora(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const instante = new Date(iso);
  if (Number.isNaN(instante.getTime())) return null;
  const local = new Date(instante.getTime() + UTC_COLOMBIA_HORAS * 3_600_000);
  return `${dos(local.getUTCDate())}/${dos(local.getUTCMonth() + 1)}/${local.getUTCFullYear()} ${dos(local.getUTCHours())}:${dos(local.getUTCMinutes())}`;
}

/** «dd/mm/aaaa» de una fecha ISO con o sin hora (se lee del texto: una fecha sin hora no se corre por la zona horaria), o null. */
export function formatearFecha(iso: string | null | undefined): string | null {
  const m = iso ? /^(\d{4})-(\d{2})-(\d{2})/.exec(iso) : null;
  return m ? `${m[3]}/${m[2]}/${m[1]}` : null;
}

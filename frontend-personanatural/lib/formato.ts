// Formatos de presentación escritos a mano: la imagen de producción corre con globalización invariante, así que no se
// usa Intl ni toLocaleString con «es-CO» (saldría con otro separador o sin formato).

const UTC_COLOMBIA_HORAS = -5; // Colombia no tiene horario de verano.

/**
 * «$ 185.000,00 COP». Punto de miles y coma decimal (dos cifras), escritos a mano.
 * Un valor no numérico queda en «$ 0,00 COP».
 */
export function formatearPesos(valor: number): string {
  const centavos = Number.isFinite(valor) ? Math.round(Math.abs(valor) * 100) : 0;
  const entero = Math.floor(centavos / 100);
  const decimales = String(centavos % 100).padStart(2, "0");
  const conPuntos = String(entero).replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  return `${valor < 0 && centavos !== 0 ? "-" : ""}$ ${conPuntos},${decimales} COP`;
}

/** Parte entera con puntos de miles. `1250000` → `1.250.000`. */
function milesDe(digitos: string): string {
  const limpios = digitos.replace(/\D/g, "").replace(/^0+(?=\d)/, "");
  if (limpios === "") return "";
  return limpios.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
}

/**
 * Lo que va en un campo de valor mientras se escribe. El punto de miles se rearma en cada tecla,
 * también al borrar (`1.250.00` vuelve a `125.000`). La coma abre los decimales (máximo dos cifras).
 */
export function textoMoneda(texto: string): string {
  const limpio = texto.replace(/[^\d,]/g, "");
  if (limpio === "") return "";
  const coma = limpio.indexOf(",");
  const entera = milesDe(coma >= 0 ? limpio.slice(0, coma) : limpio);
  if (coma < 0) return entera;
  const decimales = limpio.slice(coma + 1).replace(/\D/g, "").slice(0, 2);
  return `${entera === "" ? "0" : entera},${decimales}`;
}

/** `45.000.000,50` al salir del campo, para que el valor escrito y el calculado se vean igual. */
export function textoMonedaCompleta(valor: number): string {
  return formatearPesos(valor).replace(/^\$\s*/, "").replace(/\s*COP$/, "");
}

/** Interpreta el texto del campo (puntos de miles y coma decimal) como número. Vacío o ilegible → NaN. */
export function numeroMoneda(texto: string): number {
  const limpio = texto.trim().replace(/\./g, "").replace(",", ".");
  if (limpio === "" || limpio === ".") return Number.NaN;
  const n = Number(limpio);
  return Number.isFinite(n) ? n : Number.NaN;
}

/** Cursor justo después de `n` caracteres que no son punto de miles (dígitos y la coma). */
export function caretTrasMoneda(texto: string, significativos: number): number {
  if (significativos <= 0) return 0;
  let vistos = 0;
  for (let i = 0; i < texto.length; i++) {
    if (texto[i] !== ".") {
      vistos++;
      if (vistos === significativos) return i + 1;
    }
  }
  return texto.length;
}

/** Aplica la máscara y deja el cursor en el mismo dígito, aunque el punto de miles se mueva. */
export function alEscribirMoneda(valor: string, cursor: number): { texto: string; cursor: number } {
  const significativos = valor.slice(0, cursor).replace(/\./g, "").length;
  const texto = textoMoneda(valor);
  return { texto, cursor: caretTrasMoneda(texto, significativos) };
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

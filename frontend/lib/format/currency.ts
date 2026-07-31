/**
 * Utilidades de formato de moneda colombiana (COP).
 *
 * El valor se maneja siempre como número entero de pesos en el estado/API; estas
 * funciones son solo de presentación (separadores de miles, símbolo $) y de
 * parseo de la entrada del usuario de vuelta a dígitos.
 */

const COP_FORMATTER = new Intl.NumberFormat('es-CO', {
  style: 'currency',
  currency: 'COP',
  maximumFractionDigits: 0,
});

/** Formatea un valor en pesos como moneda COP: `50000000 → "$ 50.000.000"`. */
export function formatCOP(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '';
  return COP_FORMATTER.format(value);
}

/** Conserva solo los dígitos de una entrada (descarta `$`, `.`, espacios, letras, etc.). */
export function digitsOnly(input: string): string {
  return input.replace(/\D/g, '');
}

/**
 * Entrada numérica con decimales: solo dígitos y un separador (`,` o `.` → `.`).
 * Sin letras ni otros símbolos. Feature #11066 / HU #11072.
 */
export function sanitizeDecimalInput(input: string): string {
  const normalized = input.replace(/,/g, '.');
  let out = '';
  let seenDot = false;
  for (const ch of normalized) {
    if (ch >= '0' && ch <= '9') {
      out += ch;
    } else if (ch === '.' && !seenDot) {
      out += '.';
      seenDot = true;
    }
  }
  return out;
}

/**
 * Agrupa una cadena de dígitos con separador de miles colombiano:
 * `"50000000" → "50.000.000"`. Descarta ceros a la izquierda (salvo el "0" solo).
 */
export function groupThousands(input: string): string {
  const clean = digitsOnly(input).replace(/^0+(?=\d)/, '');
  if (clean === '') return '';
  return clean.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
}

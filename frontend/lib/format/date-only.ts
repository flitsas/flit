/**
 * Normaliza una fecha de SOAT/RTM (u otras) a solo día calendario, sin hora.
 * Acepta ISO (`2027-01-23T00:00:00.000-05:00`), `YYYY-MM-DD` y `DD/MM/YYYY`.
 * Salida: `DD/MM/YYYY` (lectura Colombia).
 */
export function formatDateOnly(raw: string | null | undefined): string {
  if (!raw?.trim()) return '';
  const s = raw.trim();

  if (/^\d{1,2}\/\d{1,2}\/\d{4}$/.test(s)) {
    const [d, m, y] = s.split('/');
    return `${d.padStart(2, '0')}/${m.padStart(2, '0')}/${y}`;
  }

  const isoDay = s.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (isoDay) {
    return `${isoDay[3]}/${isoDay[2]}/${isoDay[1]}`;
  }

  const tIdx = s.indexOf('T');
  if (tIdx > 0) return formatDateOnly(s.slice(0, tIdx));

  return s;
}

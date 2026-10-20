// Utilidades puras para las listas de correos de la configuración de la empresa.

const CORREO = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function esCorreo(texto: string): boolean {
  return CORREO.test(texto.trim());
}

/** Separa por coma, punto y coma, espacios o saltos de línea; minúsculas y sin repetidos. */
export function partirCorreos(texto: string): { validos: string[]; invalidos: string[] } {
  const validos: string[] = [];
  const invalidos: string[] = [];
  for (const parte of texto.split(/[\s,;]+/)) {
    const c = parte.trim().toLowerCase();
    if (!c) continue;
    if (esCorreo(c)) {
      if (!validos.includes(c)) validos.push(c);
    } else invalidos.push(parte.trim());
  }
  return { validos, invalidos };
}

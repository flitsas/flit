// IP del ciudadano para el límite por IP del BFF. El cliente controla el contenido de X-Forwarded-For (nginx con
// `$proxy_add_x_forwarded_for` conserva lo que el cliente envíe y solo AGREGA su salto al final), así que jamás se usa
// el primer valor. Orden de confianza:
//  1. `X-Real-IP`, que nginx sobrescribe con `$remote_addr` (el cliente no puede falsearlo).
//  2. El último salto de confianza de `X-Forwarded-For`: con N proxies propios delante, la IP real es la N-ésima
//     contando desde la derecha (cada proxy de confianza agrega el salto que vio).

/** Saltos de proxy propios delante del portal (variable de servidor TRUSTED_PROXY_HOPS). Por defecto 1. */
export function parsearSaltosConfianza(valor: string | undefined): number {
  const n = Number.parseInt(valor ?? "", 10);
  return Number.isInteger(n) && n >= 1 && n <= 10 ? n : 1;
}

export function ipDelCliente(cabeceras: Pick<Headers, "get">, saltosConfianza = 1): string | undefined {
  const real = limpiar(cabeceras.get("x-real-ip"));
  if (real) return real;

  const saltos = (cabeceras.get("x-forwarded-for") ?? "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
  const indice = saltos.length - saltosConfianza;
  return indice >= 0 ? limpiar(saltos[indice]) : undefined;
}

// Una IP válida es hex, dígitos, `.` y `:` (v4/v6); cualquier otra cosa se descarta en vez de reenviarla.
function limpiar(valor: string | null | undefined): string | undefined {
  const v = valor?.trim();
  return v && v.length <= 45 && /^[0-9a-fA-F:.]+$/.test(v) ? v : undefined;
}

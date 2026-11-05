import { safeReturnTo } from "@flit/auth/return-to";

/**
 * Solo rutas relativas de esta misma app (evita un redireccionamiento abierto). Corre en el navegador (/auth/listo):
 * usa la misma regla que /auth/login (`safeReturnTo` de @flit/auth, módulo sin dependencias de servidor), que rechaza
 * caracteres de control y `\` —«/\t/evil.com» el navegador lo deja en «//evil.com»— y lo que resuelva fuera del origen.
 */
export function safeRelative(value: string | null | undefined): string {
  return safeReturnTo(value);
}

/** Solo rutas relativas de esta misma app (evita un redireccionamiento abierto). */
export function safeRelative(value: string | null | undefined): string {
  return value && value.startsWith("/") && !value.startsWith("//") && !value.startsWith("/\\") ? value : "/";
}

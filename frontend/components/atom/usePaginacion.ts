// Estado de paginación estándar de las tablas (Bug #13055). Vive en @flit/ui.
// El recuerdo de «Filas por página» se ata al usuario en sesión (Bug #14593): cada usuario ve su
// propio tamaño, y el cierre de sesión lo borra.
import { configurarAmbitoDePaginacion } from "@flit/ui/usePaginacion";
import { decodeJwtPayload, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

configurarAmbitoDePaginacion(() => {
  if (typeof window === "undefined") return null;
  return decodeJwtPayload(window.localStorage.getItem(TOKEN_STORAGE_KEY))?.sub ?? null;
});

export * from "@flit/ui/usePaginacion";

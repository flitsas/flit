import Link from "next/link";
import { PaginaExcepcion } from "@flit/ui/PaginaExcepcion";

// 404 con la página de excepción del UI Kit 2026 (HU #14562, F-11). También la ve quien abre un lote ajeno con visibilidad
// PROPIOS (AC57): la API responde 404 y la pantalla no revela si el lote existe.
export default function NotFound() {
  return (
    <main className="flex min-h-full flex-col items-center justify-center">
      <PaginaExcepcion
        codigo={404}
        titulo="Esta página no existe"
        mensaje="Revisa la dirección o vuelve al inicio para buscar el diagnóstico."
        accion={{ etiqueta: "Volver al inicio", href: "/" }}
        enlace={Link}
      />
    </main>
  );
}

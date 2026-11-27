import Link from "next/link";
import { PaginaExcepcion } from "@flit/ui/PaginaExcepcion";

// 404 con la página de excepción del UI Kit. También la ve quien abre una liquidación ajena: la API responde 404 y la
// pantalla no revela si existe.
export default function NotFound() {
  return (
    <main className="flex min-h-full flex-col items-center justify-center">
      <PaginaExcepcion
        codigo={404}
        titulo="Esta página no existe"
        mensaje="Revisa la dirección o vuelve al inicio para buscar la liquidación."
        accion={{ etiqueta: "Volver al inicio", href: "/" }}
        enlace={Link}
      />
    </main>
  );
}

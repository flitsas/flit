import Link from "next/link";
import { FileQuestion } from "lucide-react";

/**
 * 404 global FLIT — patrón AlertCard / Acceso restringido (`/403`).
 * Fondo app azul claro, tipografía Poppins vía shell, CTA pastilla degradada.
 * Semántica: alerta/error (naranja marca) + código gris (inactivo/borrador).
 */
export default function NotFoundPage() {
  return (
    <main className="app-bg flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <div
        className="flex flex-col items-center gap-3 rounded-2xl border border-[#DFE5ED] bg-white px-8 py-12 shadow-sm dark:border-white/10 dark:bg-[#0B0F14]"
        role="status"
        aria-live="polite"
      >
        <FileQuestion
          className="h-14 w-14"
          style={{ color: "#FF4E00" }}
          strokeWidth={1.8}
          aria-hidden="true"
        />
        <p
          className="text-3xl font-bold tracking-tight text-[#59677D] dark:text-white/55"
          aria-hidden="true"
        >
          404
        </p>
        <h1 className="text-2xl font-bold" style={{ color: "#162744" }}>
          Página no encontrada
        </h1>
        <p className="max-w-md text-sm text-[#59677D] dark:text-white/65">
          La sección que buscas aún no está disponible o la ruta no existe. Vuelve al inicio para
          continuar.
        </p>
        <span className="sr-only">
          404. Página no encontrada. La sección que buscas aún no está disponible o la ruta no
          existe.
        </span>
        <Link
          href="/"
          className="mt-2 rounded-xl px-5 py-2.5 text-sm font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          Volver al inicio
        </Link>
      </div>
    </main>
  );
}

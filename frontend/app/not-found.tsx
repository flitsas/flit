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
        className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-card px-8 py-12 shadow-[var(--shadow-card)]"
        role="status"
        aria-live="polite"
      >
        <FileQuestion
          className="h-14 w-14"
          style={{ color: "var(--badge-warning-fg)" }}
          strokeWidth={1.8}
          aria-hidden="true"
        />
        <p
          className="text-3xl font-bold tracking-tight text-muted-foreground"
          aria-hidden="true"
        >
          404
        </p>
        <h1 className="text-2xl font-bold text-flit-primary">
          Página no encontrada
        </h1>
        <p className="max-w-md text-sm text-muted-foreground">
          La sección que buscas aún no está disponible o la ruta no existe. Vuelve al inicio para
          continuar.
        </p>
        <span className="sr-only">
          404. Página no encontrada. La sección que buscas aún no está disponible o la ruta no
          existe.
        </span>
        <Link
          href="/"
          className="mt-2 rounded-xl px-5 py-2.5 text-sm font-semibold text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
          style={{ background: "var(--gradient-action)", boxShadow: "var(--shadow-button)" }}
        >
          Volver al inicio
        </Link>
      </div>
    </main>
  );
}

import Link from "next/link";
import { FileQuestion } from "lucide-react";

/**
 * 404 del segmento `/admin/plataforma/mandatos` (Plataforma → Mandatos).
 * Patrón AlertCard / OtUnderConstructionState: tarjeta blanca dentro del Shell.
 */
export default function AdminMandatosNotFound() {
  return (
    <div className="mx-auto flex max-w-2xl flex-col px-4 py-10 md:px-6">
      <div
        role="status"
        aria-live="polite"
        data-testid="admin-mandatos-not-found"
        className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-border bg-card px-6 py-14 text-center"
      >
        <div
          className="flex h-12 w-12 items-center justify-center rounded-xl bg-[var(--badge-danger-bg)]"
          style={{ color: "var(--destructive)" }}
          aria-hidden="true"
        >
          <FileQuestion className="h-6 w-6" strokeWidth={1.8} />
        </div>
        <p
          className="text-3xl font-bold tracking-tight text-muted-foreground"
          aria-hidden="true"
        >
          404
        </p>
        <h1 className="text-base font-semibold text-flit-primary">
          Mandatos no disponible
        </h1>
        <p className="max-w-md text-sm text-muted-foreground">
          Esta sección de mandatos aún no está construida. Vuelve al inicio o elige otra opción del
          menú Plataforma.
        </p>
        <span className="sr-only">
          404. Mandatos no disponible. Esta sección de mandatos aún no está construida.
        </span>
        <Link
          href="/"
          className="mt-2 rounded-xl px-5 py-2.5 text-sm font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
          style={{ background: "var(--gradient-action)" }}
        >
          Volver al inicio
        </Link>
      </div>
    </div>
  );
}

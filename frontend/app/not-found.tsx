import Link from "next/link";
import { buttonClass } from "@flit/ui/Button";
import { rutaIlustracion } from "@flit/ui/ilustraciones";

/**
 * 404 global FLIT — patrón AlertCard / Acceso restringido (`/403`).
 * Fondo app, tarjeta con ilustración (estado vacío `.vacio` de la propuesta, HU #14511) y botón del kit.
 */
export default function NotFoundPage() {
  return (
    <main className="app-bg flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <div
        className="flex w-full max-w-lg flex-col items-center gap-3 rounded-[20px] border border-border bg-card px-6 py-10 shadow-[var(--shadow-card)] sm:px-10"
        role="status"
        aria-live="polite"
      >
        {/* eslint-disable-next-line @next/next/no-img-element -- ilustración decorativa estática */}
        <img src={rutaIlustracion("consulta-placa")} alt="" aria-hidden="true" width={96} height={96} className="h-24 w-24" />
        <p
          className="text-3xl font-bold tracking-tight text-muted-foreground"
          aria-hidden="true"
        >
          404
        </p>
        <h1 className="text-2xl font-bold text-[color:var(--di-heading)]">
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
        <Link href="/" className={buttonClass({ className: "mt-2" })}>
          Volver al inicio
        </Link>
      </div>
    </main>
  );
}

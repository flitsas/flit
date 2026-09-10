import Link from "next/link";
import { FileQuestion } from "lucide-react";

/**
 * 404 del segmento `/admin/plataforma/confirmacion-runt` (Plataforma → Confirmación RUNT).
 * Patrón AlertCard / OtUnderConstructionState: tarjeta blanca dentro del Shell.
 * Lo reciben tanto una URL inexistente como un usuario sin el permiso de la pestaña
 * (HU #12313, AC4/AC5): a quien no tiene el módulo no se le confirma que existe.
 */
export default function AdminConfirmacionRuntNotFound() {
  return (
    <div className="mx-auto flex max-w-2xl flex-col px-4 py-10 md:px-6">
      <div
        role="status"
        aria-live="polite"
        data-testid="admin-confirmacion-runt-not-found"
        className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-[#DFE5ED] bg-white px-6 py-14 text-center dark:border-white/10 dark:bg-[#0B0F14]"
      >
        <div
          className="flex h-12 w-12 items-center justify-center rounded-xl bg-[#FF4E00]/10"
          style={{ color: "#FF4E00" }}
          aria-hidden="true"
        >
          <FileQuestion className="h-6 w-6" strokeWidth={1.8} />
        </div>
        <p
          className="text-3xl font-bold tracking-tight text-[#59677D] dark:text-white/55"
          aria-hidden="true"
        >
          404
        </p>
        <h1 className="text-base font-semibold text-[#162744] dark:text-white">
          Confirmación RUNT no disponible
        </h1>
        <p className="max-w-md text-sm text-[#59677D] dark:text-white/65">
          No tienes acceso a esta sección o la ruta no existe. Vuelve al inicio o elige otra
          opción del menú Plataforma.
        </p>
        <span className="sr-only">
          404. Confirmación RUNT no disponible. No tienes acceso a esta sección o la ruta no
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
    </div>
  );
}

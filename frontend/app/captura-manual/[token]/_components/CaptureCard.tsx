import type { ReactNode } from "react";

/**
 * Marco de la página pública: fondo crema, tarjeta blanca estrecha, cabecera
 * solo el logo de FLIT (con el nombre del producto) a la izquierda, y pie de ayuda.
 * Excepción documentada a la línea base FLIT: replica a Kyverum por decisión del PO (Épica #13202).
 */
export function CaptureCard({ productName, children }: { productName?: string; children: ReactNode }) {
  return (
    <main className="flex min-h-dvh justify-center bg-[#FBF7EF] px-4 py-6">
      <div className="w-full max-w-[480px]">
        <div className="rounded-[18px] border border-flit-gray bg-white p-4 shadow-sm sm:p-6">
          <header className="mb-4 flex items-center gap-3">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src="/assets/logo-flit-dark.svg" alt="FLIT" className="h-7 w-auto" />
            <span className="text-sm text-muted-foreground">{productName ?? "FLIT 2.0"}</span>
          </header>
          {children}
        </div>
        <p className="mt-4 text-center text-sm text-muted-foreground">
          ¿Problemas? Escríbele a FLIT 2.0
        </p>
      </div>
    </main>
  );
}

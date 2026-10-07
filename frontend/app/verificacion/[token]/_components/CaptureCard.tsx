import type { ReactNode } from "react";

/**
 * Marco de la página pública: fondo crema, tarjeta blanca estrecha, cabecera
 * solo el logo de FLIT (sin texto) a la izquierda, y pie de ayuda.
 * Excepción documentada a la línea base FLIT: replica a Kyverum por decisión del PO (Épica #13202).
 */
export function CaptureCard({ children }: { productName?: string | null; children: ReactNode }) {
  return (
    <main className="flex min-h-dvh justify-center bg-[#FBF7EF] px-4 py-6">
      <div className="w-full max-w-[480px]">
        <div className="rounded-[18px] border border-flit-gray bg-white p-4 shadow-sm sm:p-6">
          <header className="mb-4 flex items-center">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src="/assets/logo-flit-dark.svg" alt="FLIT" className="h-10 w-auto" />
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

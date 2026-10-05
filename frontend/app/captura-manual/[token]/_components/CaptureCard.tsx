import type { ReactNode } from "react";
import { ShieldCheck } from "lucide-react";

/**
 * Marco de la página pública: fondo crema, tarjeta blanca estrecha, cabecera
 * «Verify · FLIT 2.0» a la izquierda y logo flit a la derecha, y pie de ayuda.
 * Excepción documentada a la línea base FLIT: replica a Kyverum por decisión del PO (Épica #13202).
 */
export function CaptureCard({ productName, children }: { productName?: string; children: ReactNode }) {
  return (
    <main className="flex min-h-dvh justify-center bg-[#FBF7EF] px-4 py-6">
      <div className="w-full max-w-[480px]">
        <div className="rounded-[18px] border border-flit-gray bg-white p-4 shadow-sm sm:p-6">
          <header className="mb-4 flex items-center justify-between gap-3">
            <div className="flex items-center gap-2 text-flit-primary">
              <ShieldCheck aria-hidden="true" className="size-6 text-flit-brand-ink" />
              <span className="text-base font-bold">Verify</span>
              <span className="text-sm text-muted-foreground">{productName ?? "FLIT 2.0"}</span>
            </div>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src="/assets/logo-flit-dark.svg" alt="flit" className="h-6 w-auto" />
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

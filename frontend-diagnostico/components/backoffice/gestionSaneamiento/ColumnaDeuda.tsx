"use client";

import { useSyncExternalStore } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { SeccionesDeuda } from "@/components/saneamiento/SeccionesDeuda";
import type { CargaDeuda } from "./useGestionSaneamiento";

// PS-5 (CR-5): columna izquierda, la deuda del vehículo en lectura (SeccionesDeuda de PS-2) con sus estados: cargando,
// consultas en curso, error con «Reintentar» y lista.

/** Por encima de 1024 px la deuda va abierta en su columna; por debajo, plegada (spec-ui §6.1, D-14). Sin matchMedia: ancha. */
function useAncha(): boolean {
  return useSyncExternalStore(
    (avisar) => {
      if (typeof window === "undefined" || typeof window.matchMedia !== "function") return () => {};
      const m = window.matchMedia("(min-width: 1024px)");
      m.addEventListener("change", avisar);
      return () => m.removeEventListener("change", avisar);
    },
    () => (typeof window.matchMedia === "function" ? window.matchMedia("(min-width: 1024px)").matches : true),
    () => true,
  );
}

function CuerpoDeuda({ deuda, onReintentar }: { deuda: CargaDeuda; onReintentar: () => void }) {
  if (deuda.tipo === "cargando") {
    return (
      <p role="status" className={AYUDA_CLS}>
        Cargando la deuda…
      </p>
    );
  }
  if (deuda.tipo === "listo") return <SeccionesDeuda deuda={deuda.deuda} />;
  if (deuda.noTerminado) {
    return (
      <InlineAlert tone="info" title="Consultas en curso">
        Este vehículo aún tiene consultas por terminar.
      </InlineAlert>
    );
  }
  return (
    <InlineAlert
      tone="error"
      title="No pudimos cargar la deuda"
      action={
        <button type="button" onClick={onReintentar} className={BOTON_REINTENTAR_CLS}>
          Reintentar
        </button>
      }
    >
      Intenta de nuevo en un momento.
    </InlineAlert>
  );
}

export function ColumnaDeuda({ deuda, onReintentar }: { deuda: CargaDeuda; onReintentar: () => void }) {
  const ancha = useAncha();
  const cuerpo = <CuerpoDeuda deuda={deuda} onReintentar={onReintentar} />;
  return (
    <section aria-label="Deuda del vehículo" className={`${TARJETA_CLS} flex min-w-0 flex-col gap-3`}>
      {ancha ? (
        <>
          <h2 className="text-lg font-semibold text-flit-primary">Deuda del vehículo</h2>
          {cuerpo}
        </>
      ) : (
        <TarjetaPlegable titulo="Deuda del vehículo" nivelTitulo={2}>
          {cuerpo}
        </TarjetaPlegable>
      )}
    </section>
  );
}

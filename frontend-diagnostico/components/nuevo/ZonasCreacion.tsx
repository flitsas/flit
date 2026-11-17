import { useId, type ReactNode, type Ref } from "react";
import { Icono } from "@flit/ui/Icono";
import { AYUDA_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";

export interface ZonasCreacionProps {
  /** Contenido de la tarjeta Vehículos (placa o archivo). */
  vehiculos: ReactNode;
  /** Ayuda bajo el título de Vehículos. */
  ayudaVehiculos?: string;
  /** Tarjetas que siguen a Vehículos (resumen y filas de la carga), a ancho completo. */
  despuesVehiculos?: ReactNode;
  /** Contenido de la tarjeta Parámetros (el formulario con los campos). */
  parametros: ReactNode;
  /** Barra final con el estimado, los avisos y la acción (S5: el botón queda al final del formulario de carga). */
  pie?: ReactNode;
  /** El contenedor, para llevar el foco al primer campo inválido (en orden visual: Parámetros primero). */
  rejillaRef?: Ref<HTMLDivElement>;
}

const TITULO_CLS = "text-lg font-semibold text-flit-primary";

/**
 * Disposición de la pantalla de creación (HU #14085; ola Diagnóstico UX 2026-11-15 · FE-3, spec-ui §3): una sola columna
 * a cualquier ancho, de arriba abajo: Parámetros (a ancho completo y sin sticky: arriba ya no hace falta y taparía
 * Vehículos), Vehículos, lo que sigue a la carga y al final la barra de acción. El orden del DOM es el visual, así que el
 * foco recorre la pantalla en ese orden. `min-w-0` deja el scroll horizontal dentro de la tabla de filas (reflow 320 px).
 */
export function ZonasCreacion({ vehiculos, ayudaVehiculos, despuesVehiculos, parametros, pie, rejillaRef }: ZonasCreacionProps) {
  const id = useId();
  return (
    <div ref={rejillaRef} className="flex min-w-0 flex-col gap-4">
      <section aria-labelledby={`${id}-parametros`} className={`${TARJETA_CLS} flex min-w-0 flex-col gap-4`}>
        <div className="flex items-center gap-2">
          <Icono nombre="filtro" className="h-5 w-5 text-flit-brand" />
          <h2 id={`${id}-parametros`} className={TITULO_CLS}>
            Parámetros
          </h2>
        </div>
        {parametros}
      </section>

      <section aria-labelledby={`${id}-vehiculos`} className={`${TARJETA_CLS} flex min-w-0 flex-col gap-4`}>
        <div className="flex flex-col gap-1">
          <div className="flex items-center gap-2">
            <Icono nombre="carro" className="h-5 w-5 text-flit-brand" />
            <h2 id={`${id}-vehiculos`} className={TITULO_CLS}>
              Vehículos
            </h2>
          </div>
          {ayudaVehiculos && <p className={AYUDA_CLS}>{ayudaVehiculos}</p>}
        </div>
        {vehiculos}
      </section>

      {despuesVehiculos}

      {pie ? (
        <div data-parte="pie-creacion" className={`${TARJETA_CLS} flex min-w-0 flex-col gap-3 sm:flex-row sm:items-center sm:justify-between`}>
          {pie}
        </div>
      ) : null}
    </div>
  );
}

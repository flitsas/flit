import { useId, type ReactNode, type Ref } from "react";
import { CarFront, SlidersHorizontal } from "lucide-react";
import { AYUDA_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";

export interface DosZonasProps {
  /** Contenido de la tarjeta Vehículos (placa o archivo). */
  vehiculos: ReactNode;
  /** Ayuda bajo el título de Vehículos. */
  ayudaVehiculos?: string;
  /** Tarjetas que siguen a Vehículos en la misma columna (resumen y filas de la carga). */
  despuesVehiculos?: ReactNode;
  /** Contenido de la tarjeta Parámetros (campos, estimado y acción). */
  parametros: ReactNode;
  /** La rejilla, para llevar el foco al primer campo inválido de cualquiera de las dos zonas. */
  rejillaRef?: Ref<HTMLDivElement>;
}

const TITULO_CLS = "text-lg font-semibold text-flit-primary dark:text-white";

/**
 * Disposición de la pantalla única de creación (HU #14085): desde 1024 px dos columnas, a la izquierda Vehículos (con lo
 * que siga del lote) y a la derecha Parámetros, fija (sticky) y con su propio scroll si no cabe en la ventana; por debajo
 * de 1024 px se apilan en una sola columna. `min-w-0` en las columnas evita que la tabla de filas desborde la pantalla:
 * el scroll horizontal queda dentro de la tabla. Tarjetas blancas del prototipo (TARJETA_CLS).
 */
export function DosZonas({ vehiculos, ayudaVehiculos, despuesVehiculos, parametros, rejillaRef }: DosZonasProps) {
  const id = useId();
  return (
    <div ref={rejillaRef} className="grid grid-cols-1 items-start gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
      <section aria-labelledby={`${id}-vehiculos`} className="flex min-w-0 flex-col gap-4">
        <div className={`${TARJETA_CLS} flex flex-col gap-4`}>
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <CarFront className="h-5 w-5 text-flit-brand" aria-hidden="true" />
              <h2 id={`${id}-vehiculos`} className={TITULO_CLS}>
                Vehículos
              </h2>
            </div>
            {ayudaVehiculos && <p className={AYUDA_CLS}>{ayudaVehiculos}</p>}
          </div>
          {vehiculos}
        </div>
        {despuesVehiculos}
      </section>

      <section
        aria-labelledby={`${id}-parametros`}
        className={`${TARJETA_CLS} flex min-w-0 flex-col gap-4 lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)] lg:overflow-y-auto`}
      >
        <div className="flex items-center gap-2">
          <SlidersHorizontal className="h-5 w-5 text-flit-brand" aria-hidden="true" />
          <h2 id={`${id}-parametros`} className={TITULO_CLS}>
            Parámetros
          </h2>
        </div>
        {parametros}
      </section>
    </div>
  );
}

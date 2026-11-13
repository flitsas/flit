"use client";

import { CheckSquare, FilePlus2 } from "lucide-react";
import { BarraSeleccion } from "@flit/ui/BarraSeleccion";
import { Button } from "@flit/ui/Button";
import { BOTON_SECUNDARIO_CLS, MARCA_CLS } from "@/components/configuracion/estilos";
import type { FilaVehiculo } from "@/lib/tablero/api";
import { rotuloMotivo, type DerivacionFila } from "@/lib/tablero/derivacion";

/** HU #14562 AC32: «1 vehículo seleccionado» / «N vehículos seleccionados» (maqueta del tablero). */
export const textoSeleccionadas = (n: number) => (n === 1 ? "1 vehículo seleccionado" : `${n} vehículos seleccionados`);

export interface CasillaDerivacionProps {
  vehiculo: FilaVehiculo;
  derivacion: DerivacionFila;
  marcada: boolean;
  onCambiar: (vehiculoId: string, marcada: boolean) => void;
}

/**
 * Casilla de selección de una fila para «Crear trámites (N)» (Punto 4, decisión 4.2). Activa solo en la placa elegible
 * (terminada, VIABLE y sin trámite); en las demás, deshabilitada con el motivo en lenguaje de negocio como descripción
 * accesible y tooltip. Sin datos de derivación en la fila (derivación apagada) no se pinta.
 */
export function CasillaDerivacion({ vehiculo, derivacion, marcada, onCambiar }: CasillaDerivacionProps) {
  if (!derivacion.conocida) return null;
  const identificador = vehiculo.placa || vehiculo.vin || vehiculo.identificador;
  const motivo = derivacion.elegible ? undefined : rotuloMotivo(derivacion.motivo ?? (derivacion.tramite ? "YA_TIENE_TRAMITE" : null));
  return (
    <input
      type="checkbox"
      aria-label={`Seleccionar la placa ${identificador}`}
      checked={derivacion.elegible && marcada}
      disabled={!derivacion.elegible}
      title={motivo}
      onChange={(e) => onCambiar(vehiculo.id, e.target.checked)}
      className={`${MARCA_CLS} disabled:cursor-not-allowed disabled:opacity-50`}
    />
  );
}

export interface BarraSeleccionDerivacionProps {
  /** Placas elegibles del lote (las que «Seleccionar todos los elegibles» marca: Viables, terminadas y sin trámite). */
  elegibles: number;
  seleccionadas: number;
  enviando: boolean;
  onSeleccionarTodas: () => void;
  onQuitar: () => void;
  onCrear: () => void;
  /** Error al encolar, en lenguaje claro. */
  error?: string | null;
}

/**
 * Selección para crear trámites (Punto 4; HU #14562 AC32): sin preselección, «Seleccionar todos los elegibles» y, con algo
 * marcado, la barra de acciones en bloque (`BarraSeleccion`): conteo anunciado por aria-live, «Crear trámites (N)» —que
 * mientras encola dice «Creando trámites…» con el girador del botón— y «Quitar selección».
 */
export function BarraSeleccionDerivacion({ elegibles, seleccionadas, enviando, onSeleccionarTodas, onQuitar, onCrear, error }: BarraSeleccionDerivacionProps) {
  return (
    <div role="group" aria-label="Selección para crear trámites" className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={onSeleccionarTodas} disabled={elegibles === 0 || enviando} className={BOTON_SECUNDARIO_CLS}>
          <CheckSquare className="h-4 w-4" aria-hidden="true" />
          Seleccionar todos los elegibles
        </button>
      </div>
      <BarraSeleccion
        cantidad={seleccionadas}
        textoSeleccion={textoSeleccionadas}
        ayuda="Solo se seleccionan vehículos Viables y sin trámite"
        onQuitar={enviando ? undefined : onQuitar}
        acciones={
          <Button cargando={enviando} onClick={onCrear} disabled={seleccionadas === 0}>
            {enviando ? (
              "Creando trámites…"
            ) : (
              <>
                <FilePlus2 className="h-4 w-4" aria-hidden="true" />
                {`Crear trámites (${seleccionadas})`}
              </>
            )}
          </Button>
        }
      />
      {error && (
        <p role="alert" aria-label="Resultado de crear trámites" className="text-xs font-semibold" style={{ color: "var(--badge-danger-fg)" }}>
          {error}
        </p>
      )}
    </div>
  );
}

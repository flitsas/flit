"use client";

import { CheckSquare, FilePlus2, XSquare } from "lucide-react";
import { BOTON_PRIMARIO_CLS, BOTON_SECUNDARIO_CLS, MARCA_CLS } from "@/components/configuracion/estilos";
import type { FilaVehiculo } from "@/lib/tablero/api";
import { rotuloMotivo, type DerivacionFila } from "@/lib/tablero/derivacion";

/** «1 seleccionada» / «N seleccionadas». */
export const textoSeleccionadas = (n: number) => (n === 1 ? "1 seleccionada" : `${n} seleccionadas`);

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
  /** Placas elegibles del lote (las que «Seleccionar todas» marca). */
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
 * Barra de selección del masivo (Punto 4, decisión 4.2, diseño §6): sin preselección, «Seleccionar todas las elegibles»,
 * «Quitar selección», contador «N seleccionadas» (anunciado con aria-live) y «Crear trámites (N)», deshabilitado sin
 * selección o mientras se encola.
 */
export function BarraSeleccionDerivacion({ elegibles, seleccionadas, enviando, onSeleccionarTodas, onQuitar, onCrear, error }: BarraSeleccionDerivacionProps) {
  return (
    <div role="group" aria-label="Selección para crear trámites" className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={onSeleccionarTodas} disabled={elegibles === 0 || enviando} className={BOTON_SECUNDARIO_CLS}>
          <CheckSquare className="h-4 w-4" aria-hidden="true" />
          Seleccionar todas las elegibles
        </button>
        <button type="button" onClick={onQuitar} disabled={seleccionadas === 0 || enviando} className={BOTON_SECUNDARIO_CLS}>
          <XSquare className="h-4 w-4" aria-hidden="true" />
          Quitar selección
        </button>
        <p aria-live="polite" className="text-sm font-medium text-flit-primary">
          {textoSeleccionadas(seleccionadas)}
        </p>
        <button
          type="button"
          onClick={onCrear}
          disabled={seleccionadas === 0 || enviando}
          aria-busy={enviando}
          className={`${BOTON_PRIMARIO_CLS} sm:ml-auto`}
        >
          <FilePlus2 className="h-4 w-4" aria-hidden="true" />
          {`Crear trámites (${seleccionadas})`}
        </button>
      </div>
      {error && (
        <p role="alert" aria-label="Resultado de crear trámites" className="text-xs font-semibold" style={{ color: "var(--badge-danger-fg)" }}>
          {error}
        </p>
      )}
    </div>
  );
}

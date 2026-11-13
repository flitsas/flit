"use client";

import { useState, type ReactNode } from "react";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { buscarVehiculos, filtrarVehiculos, veredictoDeFila, FILTROS_TABLERO, type FiltroTablero } from "@/lib/tablero/filtrosTablero";
import { AYUDA_CLS } from "@/components/configuracion/estilos";
import { BuscadorGrilla } from "./BuscadorGrilla";
import { GrillaSemaforos } from "./GrillaSemaforos";
import { BotonDictamenFila } from "./PanelDictamen";

export interface GrillaLoteProps {
  grilla: Grilla;
  filtro: FiltroTablero | null;
  puedeReprocesar: boolean;
  onReprocesada: (celda: CeldaGrilla) => void;
  onErrorReproceso: (mensaje: string, volverALeer: boolean) => void;
  accionVehiculo?: (vehiculo: FilaVehiculo) => ReactNode;
  tituloAccionVehiculo: string;
  casillaVehiculo?: (vehiculo: FilaVehiculo) => ReactNode;
  onAbrirDictamen: (vehiculo: FilaVehiculo, disparador: HTMLElement) => void;
}

const placaDe = (v: FilaVehiculo) => v.placa || v.vin || v.identificador;

/**
 * Grilla del tablero (HU #14562 AC28): búsqueda por placa o VIN, el filtro de la tarjeta activa (en el cliente) y la columna
 * Dictamen con el veredicto de la fila; el panel del dictamen se abre desde ahí.
 */
export function GrillaLote({
  grilla,
  filtro,
  puedeReprocesar,
  onReprocesada,
  onErrorReproceso,
  accionVehiculo,
  tituloAccionVehiculo,
  casillaVehiculo,
  onAbrirDictamen,
}: GrillaLoteProps) {
  const [busqueda, setBusqueda] = useState("");
  const vehiculos = buscarVehiculos(filtrarVehiculos(grilla.vehiculos, filtro), busqueda);
  const etiquetaFiltro = FILTROS_TABLERO.find((f) => f.clave === filtro)?.etiqueta;
  const total = grilla.vehiculos.length;

  return (
    <>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <BuscadorGrilla valor={busqueda} onCambiar={setBusqueda} />
        <p aria-live="polite" className={AYUDA_CLS}>
          {etiquetaFiltro || busqueda.trim()
            ? `${etiquetaFiltro ? `Filtro: ${etiquetaFiltro} · ` : ""}${vehiculos.length} de ${total}`
            : `Mostrando los ${total} vehículos`}
        </p>
      </div>
      <GrillaSemaforos
        grilla={grilla}
        vehiculos={vehiculos}
        puedeReprocesar={puedeReprocesar}
        onReprocesada={onReprocesada}
        onErrorReproceso={onErrorReproceso}
        accionVehiculo={accionVehiculo}
        tituloAccionVehiculo={tituloAccionVehiculo}
        casillaVehiculo={casillaVehiculo}
        dictamenVehiculo={(v) => (
          <BotonDictamenFila
            identificador={placaDe(v)}
            veredicto={veredictoDeFila(v)}
            ajustado={v.dictamen_ajustado === true}
            onAbrir={(disparador) => onAbrirDictamen(v, disparador)}
          />
        )}
      />
      <p className={AYUDA_CLS}>Clic en una celda: abre el detalle de esa consulta · Clic en el dictamen: abre el panel del vehículo</p>
    </>
  );
}

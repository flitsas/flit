"use client";

import { useState, type ReactNode } from "react";
import { ChipFiltro } from "@flit/ui/ChipsFiltro";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import {
  FILTROS_GRILLA_VACIOS,
  FILTROS_SANEAMIENTO,
  aplicarFiltrosGrilla,
  filtrarPorSaneamiento,
  filtrarVehiculos,
  hayFiltrosGrilla,
  veredictoDeFila,
  type FiltroSaneamiento,
  type FiltroTablero,
  type FiltrosGrilla as ValorFiltros,
} from "@/lib/tablero/filtrosTablero";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";
import { AYUDA_CLS } from "@/components/configuracion/estilos";
import { Etiqueta, TEXTO_SECUNDARIO_CLS } from "./vistasDetalle/comun";
import { FiltrosGrilla, VacioFiltros } from "./FiltrosGrilla";
import { GrillaSemaforos } from "./GrillaSemaforos";

export interface GrillaLoteProps {
  grilla: Grilla;
  filtro: FiltroTablero | null;
  /** HU #14577 AC9: chip «Con / Sin saneamiento» activo (lo guarda quien pinta la grilla, en la URL). */
  filtroSaneamiento?: FiltroSaneamiento | null;
  /** Sin él no hay chips de saneamiento (filas sin la clave `saneamiento`, spec-ui §2.6). */
  onFiltroSaneamiento?: (filtro: FiltroSaneamiento | null) => void;
  puedeReprocesar: boolean;
  onReprocesada: (celda: CeldaGrilla) => void;
  onErrorReproceso: (mensaje: string, volverALeer: boolean) => void;
  accionVehiculo?: (vehiculo: FilaVehiculo) => ReactNode;
  tituloAccionVehiculo: string;
  casillaVehiculo?: (vehiculo: FilaVehiculo) => ReactNode;
  /** «Limpiar filtros» también apaga la tarjeta-filtro y el chip de saneamiento (los controla quien pinta la grilla). */
  onQuitarFiltro?: () => void;
}

/** Texto de ayuda al pie (spec-ui §6.4, A6.9). */
export const AYUDA_GRILLA =
  "Clic en un estado: abre el detalle de esa consulta en otra pestaña · Pasa el puntero o enfoca un estado para ver la fecha del dato · Dictamen y auditoría: en «Acciones».";

/** Veredicto de la fila como dato (HU #14584 §6.4): chip con texto, la marca del ajuste FLIT y la etiqueta del saneamiento. */
function DictamenFila({ vehiculo: v }: { vehiculo: FilaVehiculo }) {
  const veredicto = veredictoDeFila(v);
  return (
    <div className="flex flex-col items-start gap-1">
      <Etiqueta tono={veredicto.tono}>{veredicto.texto}</Etiqueta>
      {v.dictamen_ajustado === true && <span className={TEXTO_SECUNDARIO_CLS}>Ajustado por FLIT</span>}
      {/* HU #14577 AC9 (spec-ui §2.2): la etiqueta del saneamiento, debajo del veredicto. */}
      <EtiquetaSaneamiento estado={v.saneamiento?.estado ?? null} />
    </div>
  );
}

/**
 * Grilla del tablero (HU #14562 AC28; barra de filtros HU #14584 §6.6): la tarjeta-filtro activa, el chip de saneamiento y la
 * barra (búsqueda, fuente + estado, dictamen, cambio de propietario) se combinan (Y) en el cliente. La columna Dictamen es el
 * veredicto de la fila como dato; el panel se abre desde «Acciones». Si los filtros no dejan filas, «Ningún vehículo coincide»
 * con «Limpiar filtros», que limpia la barra, la tarjeta y el chip.
 */
export function GrillaLote({
  grilla,
  filtro,
  filtroSaneamiento = null,
  onFiltroSaneamiento,
  puedeReprocesar,
  onReprocesada,
  onErrorReproceso,
  accionVehiculo,
  tituloAccionVehiculo,
  casillaVehiculo,
  onQuitarFiltro,
}: GrillaLoteProps) {
  const [filtros, setFiltros] = useState<ValorFiltros>(FILTROS_GRILLA_VACIOS);
  const vehiculos = aplicarFiltrosGrilla(filtrarPorSaneamiento(filtrarVehiculos(grilla.vehiculos, filtro), filtroSaneamiento), filtros);
  const total = grilla.vehiculos.length;
  const filtrando = filtro !== null || filtroSaneamiento !== null || hayFiltrosGrilla(filtros);
  const limpiar = () => {
    setFiltros(FILTROS_GRILLA_VACIOS);
    onQuitarFiltro?.();
  };

  return (
    <>
      <FiltrosGrilla
        grilla={grilla}
        vehiculos={grilla.vehiculos}
        valor={filtros}
        onCambiar={setFiltros}
        onLimpiar={limpiar}
        hayActivos={filtrando}
        extra={
          onFiltroSaneamiento && (
            // HU #14577 AC9 (D-09): chips sin cifra, excluyentes; un clic en el activo lo apaga.
            <div role="group" aria-label="Filtrar por saneamiento" className="flex flex-wrap gap-2">
              {FILTROS_SANEAMIENTO.map((f) => (
                <ChipFiltro
                  key={f.clave}
                  etiqueta={f.etiqueta}
                  activo={filtroSaneamiento === f.clave}
                  onClick={() => onFiltroSaneamiento(filtroSaneamiento === f.clave ? null : f.clave)}
                />
              ))}
            </div>
          )
        }
        // spec-ui §6.6: «Mostrando N de M vehículos» siempre (aria-live); el filtro activo ya lo dicen sus controles.
        resumen={`Mostrando ${vehiculos.length} de ${total} vehículos`}
      />
      {filtrando && vehiculos.length === 0 ? (
        <VacioFiltros onLimpiar={limpiar} mensaje={filtroSaneamiento ? "Quita el filtro de saneamiento o prueba con otra búsqueda." : undefined} />
      ) : (
        <GrillaSemaforos
          grilla={grilla}
          vehiculos={vehiculos}
          puedeReprocesar={puedeReprocesar}
          onReprocesada={onReprocesada}
          onErrorReproceso={onErrorReproceso}
          accionVehiculo={accionVehiculo}
          tituloAccionVehiculo={tituloAccionVehiculo}
          casillaVehiculo={casillaVehiculo}
          dictamenVehiculo={(v) => <DictamenFila vehiculo={v} />}
        />
      )}
      <p className={AYUDA_CLS}>{AYUDA_GRILLA}</p>
    </>
  );
}

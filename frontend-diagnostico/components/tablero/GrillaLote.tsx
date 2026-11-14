"use client";

import { useState, type ReactNode } from "react";
import { CHIPS_FILTRO_FILA_CLS, ChipFiltro } from "@flit/ui/ChipsFiltro";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { iconoComo } from "@flit/ui/iconoComo";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import {
  buscarVehiculos,
  filtrarPorSaneamiento,
  filtrarVehiculos,
  veredictoDeFila,
  FILTROS_SANEAMIENTO,
  FILTROS_TABLERO,
  type FiltroSaneamiento,
  type FiltroTablero,
} from "@/lib/tablero/filtrosTablero";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";
import { AYUDA_CLS, BOTON_SECUNDARIO_CLS } from "@/components/configuracion/estilos";
import { BuscadorGrilla } from "./BuscadorGrilla";
import { GrillaSemaforos } from "./GrillaSemaforos";
import { BotonDictamenFila } from "./PanelDictamen";

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
  onAbrirDictamen: (vehiculo: FilaVehiculo, disparador: HTMLElement) => void;
  /** HU #14562 · m-12: «Quitar filtro» del vacío también apaga la tarjeta-filtro (la controla quien pinta la grilla). */
  onQuitarFiltro?: () => void;
}

const IconoBuscar = iconoComo("buscar");

const placaDe = (v: FilaVehiculo) => v.placa || v.vin || v.identificador;

/**
 * Grilla del tablero (HU #14562 AC28): búsqueda por placa o VIN, el filtro de la tarjeta activa (en el cliente) y la columna
 * Dictamen con el veredicto de la fila; el panel del dictamen se abre desde ahí.
 * HU #14562 · m-12: si el filtro o la búsqueda no dejan filas, en vez de una tabla vacía se anuncia «Ningún vehículo
 * coincide» con «Quitar filtro», que limpia la búsqueda y la tarjeta-filtro.
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
  onAbrirDictamen,
  onQuitarFiltro,
}: GrillaLoteProps) {
  const [busqueda, setBusqueda] = useState("");
  const vehiculos = buscarVehiculos(filtrarPorSaneamiento(filtrarVehiculos(grilla.vehiculos, filtro), filtroSaneamiento), busqueda);
  const etiquetaFiltro = [FILTROS_TABLERO.find((f) => f.clave === filtro)?.etiqueta, FILTROS_SANEAMIENTO.find((f) => f.clave === filtroSaneamiento)?.etiqueta]
    .filter(Boolean)
    .join(" · ");
  const total = grilla.vehiculos.length;
  const filtrando = filtro !== null || filtroSaneamiento !== null || busqueda.trim() !== "";
  const quitarFiltro = () => {
    setBusqueda("");
    onQuitarFiltro?.();
  };

  return (
    <>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap items-end gap-3">
          <BuscadorGrilla valor={busqueda} onCambiar={setBusqueda} />
          {onFiltroSaneamiento && (
            // HU #14577 AC9 (D-09): chips sin cifra, excluyentes; un clic en el activo lo apaga.
            <div role="group" aria-label="Filtrar por saneamiento" className={CHIPS_FILTRO_FILA_CLS}>
              {FILTROS_SANEAMIENTO.map((f) => (
                <ChipFiltro
                  key={f.clave}
                  etiqueta={f.etiqueta}
                  activo={filtroSaneamiento === f.clave}
                  onClick={() => onFiltroSaneamiento(filtroSaneamiento === f.clave ? null : f.clave)}
                />
              ))}
            </div>
          )}
        </div>
        <p aria-live="polite" className={AYUDA_CLS}>
          {etiquetaFiltro || busqueda.trim()
            ? `${etiquetaFiltro ? `Filtro: ${etiquetaFiltro} · ` : ""}${vehiculos.length} de ${total}`
            : `Mostrando los ${total} vehículos`}
        </p>
      </div>
      {filtrando && vehiculos.length === 0 ? (
        <EstadoVacio
          anunciar
          nivelTitulo={3}
          icono={<IconoBuscar className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
          titulo="Ningún vehículo coincide"
          mensaje={
            filtroSaneamiento
              ? "Quita el filtro de saneamiento o prueba con otra búsqueda."
              : "Prueba con otra placa o VIN, o quita el filtro de la tarjeta."
          }
          acciones={[
            <button key="quitar" type="button" onClick={quitarFiltro} className={BOTON_SECUNDARIO_CLS}>
              {filtroSaneamiento ? "Limpiar filtros" : "Quitar filtro"}
            </button>,
          ]}
        />
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
        dictamenVehiculo={(v) => (
          // HU #14577 AC9 (spec-ui §2.2): la etiqueta del saneamiento, debajo del veredicto.
          <div className="flex flex-col items-start gap-1">
            <BotonDictamenFila
              identificador={placaDe(v)}
              veredicto={veredictoDeFila(v)}
              ajustado={v.dictamen_ajustado === true}
              onAbrir={(disparador) => onAbrirDictamen(v, disparador)}
            />
            <EtiquetaSaneamiento estado={v.saneamiento?.estado ?? null} />
          </div>
        )}
      />
      )}
      <p className={AYUDA_CLS}>Clic en una celda: abre el detalle de esa consulta · Clic en el dictamen: abre el panel del vehículo</p>
    </>
  );
}

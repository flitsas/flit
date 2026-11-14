"use client";

import type { RefObject } from "react";
import { BarraSeleccion } from "@flit/ui/BarraSeleccion";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { BOTON_SECUNDARIO_CLS, FOCO_CLS } from "@/components/configuracion/estilos";
import { IconoDxBandera } from "@/components/dx/iconos/IconoDxBandera";
import { textoSeleccionadas } from "@/components/tablero/BarraSeleccionTablero";
import { AccionBarra } from "./ControlesSeleccion";
import { bloqueoDescargar, bloqueoSanear } from "./seleccion";
import type { AccionesSeleccion, EstadoSeleccion } from "./useSeleccionVehiculos";

// PS-3 (CR-5): sobre la tabla, «Seleccionar todos los elegibles», la barra con «Sanear (N)» y «Descargar deuda (N)», sus
// errores y el resultado de la solicitud (enfocable cuando la selección queda vacía, UA-4).

const ERROR_CLS = "text-xs font-semibold text-[color:var(--di-danger)]";

export interface BarraVehiculosProps {
  sel: EstadoSeleccion;
  acciones: AccionesSeleccion;
  puedeSanear: boolean;
  puedeDescargar: boolean;
  resultadoRef: RefObject<HTMLDivElement | null>;
}

export function BarraVehiculos({ sel, acciones, puedeSanear, puedeDescargar, resultadoRef }: BarraVehiculosProps) {
  const { sanearEnviando, descargando, errorSanear, errorDescarga, resultadoSanear } = sel;
  const ocupada = sanearEnviando || descargando;
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={acciones.seleccionarElegibles} disabled={acciones.elegibles.length === 0 || ocupada} className={BOTON_SECUNDARIO_CLS}>
          <Icono nombre="check" className="h-4 w-4" />
          Seleccionar todos los elegibles
        </button>
      </div>
      <BarraSeleccion
        cantidad={acciones.seleccionadas.length}
        textoSeleccion={textoSeleccionadas}
        ayuda="Cada acción cuenta solo los vehículos que cumplen su regla."
        onQuitar={ocupada ? undefined : acciones.quitarSeleccion}
        acciones={
          <>
            {puedeSanear && (
              <AccionBarra
                variante="primary"
                cantidad={acciones.paraSanear.length}
                enviando={sanearEnviando}
                textoEnvio="Solicitando…"
                texto="Sanear"
                icono={<IconoDxBandera tamano={16} />}
                bloqueo={bloqueoSanear(acciones.paraSanear.length)}
                onClick={() => void acciones.sanear()}
              />
            )}
            {puedeDescargar && (
              <AccionBarra
                variante={puedeSanear ? "secondary" : "primary"}
                cantidad={acciones.paraDescargar.length}
                enviando={descargando}
                textoEnvio="Descargando…"
                texto="Descargar deuda"
                icono={<Icono nombre="nube-bajar" className="h-4 w-4" />}
                bloqueo={bloqueoDescargar(acciones.paraDescargar.length)}
                onClick={() => void acciones.descargar()}
              />
            )}
          </>
        }
      />
      {errorSanear && (
        <p role="alert" aria-label="Resultado de sanear" className={ERROR_CLS}>
          {errorSanear}
        </p>
      )}
      {errorDescarga && (
        <p role="alert" aria-label="Resultado de descargar deuda" className={ERROR_CLS}>
          {errorDescarga}
        </p>
      )}
      {resultadoSanear && (
        <div ref={resultadoRef} tabIndex={-1} className={`rounded-[9px] ${FOCO_CLS}`}>
          <InlineAlert tone={resultadoSanear.tono} title={resultadoSanear.titulo} vivo={false}>
            {resultadoSanear.texto}
          </InlineAlert>
        </div>
      )}
    </div>
  );
}

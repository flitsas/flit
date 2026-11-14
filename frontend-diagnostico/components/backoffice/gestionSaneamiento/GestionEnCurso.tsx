"use client";

import { Button } from "@flit/ui/Button";
import { Icono } from "@flit/ui/Icono";
import { RenglonesEditables } from "@/components/backoffice/RenglonesGestion";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, CAMPO_CLS, ETIQUETA_CLS, FOCO_CLS } from "@/components/configuracion/estilos";
import type { SaneamientoBackoffice } from "@/lib/backoffice/saneamientos";
import { LIMITES_TEXTO } from "@/lib/saneamiento/textoLibre";
import { ENLACE_PELIGRO_CLS } from "./comun";
import type { GestionSaneamientoEstado } from "./useGestionSaneamiento";
import type { EstadoObservacion } from "./useObservacionSaneamiento";

// PS-5 (CR-5): la gestión EN_GESTION (spec-ui §6.3): conceptos editables, «Agregar otro concepto», la observación para la
// compañía y el bloque de cierre (saneado, no procede, anular).

const ROTULO_OBSERVACION: Record<EstadoObservacion, string> = {
  sin_cambios: "Sin cambios",
  editado: "Sin guardar",
  guardando: "Guardando…",
  guardado: "Guardado",
  error: "No se guardó",
};
const TONO_OBSERVACION: Record<EstadoObservacion, string> = {
  sin_cambios: "text-[color:var(--di-text-tertiary)]",
  editado: "text-[color:var(--di-info)]",
  guardando: "text-[color:var(--di-info)]",
  guardado: "text-[color:var(--di-success)]",
  error: "text-[color:var(--di-danger)]",
};

function CampoObservacion({ id, observacion }: { id: string; observacion: GestionSaneamientoEstado["observacion"] }) {
  const { obs, editar, guardar } = observacion;
  return (
    <div className="flex flex-col gap-1">
      <div className="flex flex-wrap items-center gap-2">
        <label htmlFor={`${id}-observacion`} className={ETIQUETA_CLS}>
          Observación para la compañía
        </label>
        <span data-testid="indicador-observacion" className={`text-xs font-semibold ${TONO_OBSERVACION[obs.estado]}`}>
          {ROTULO_OBSERVACION[obs.estado]}
        </span>
      </div>
      <textarea
        id={`${id}-observacion`}
        rows={3}
        maxLength={LIMITES_TEXTO.observacion.max}
        value={obs.texto}
        readOnly={obs.estado === "guardando"}
        aria-describedby={`${id}-observacion-ayuda`}
        onChange={(e) => editar(e.target.value)}
        onBlur={() => void guardar()}
        className={`${CAMPO_CLS.replace("h-11 ", "")} min-h-20 py-2 text-sm`}
      />
      <p id={`${id}-observacion-ayuda`} className={AYUDA_CLS}>
        La compañía la ve en el panel del vehículo.
      </p>
      {obs.error && (
        <div className="flex flex-wrap items-center gap-2">
          <p role="alert" className="text-xs font-semibold text-[color:var(--di-danger)]">
            {obs.error}
          </p>
          <button type="button" aria-label="Reintentar la observación" onClick={() => void guardar()} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        </div>
      )}
    </div>
  );
}

function CierreSaneamiento({ id, motivoNoSaneado, abrir }: { id: string; motivoNoSaneado: string | null; abrir: GestionSaneamientoEstado["abrir"] }) {
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-base font-semibold text-flit-primary">Cerrar el saneamiento</h3>
      <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
        <Button
          type="button"
          size="sm"
          aria-disabled={motivoNoSaneado ? "true" : undefined}
          aria-describedby={motivoNoSaneado ? `${id}-regla` : undefined}
          className={motivoNoSaneado ? "cursor-not-allowed opacity-60" : undefined}
          onClick={() => {
            if (!motivoNoSaneado) abrir({ tipo: "saneado" });
          }}
        >
          Cerrar como saneado
        </Button>
        <Button type="button" variant="secondary" size="sm" onClick={() => abrir({ tipo: "no_procede" })}>
          Cerrar como no procede
        </Button>
        <button type="button" onClick={() => abrir({ tipo: "anular" })} className={ENLACE_PELIGRO_CLS}>
          Anular
        </button>
      </div>
      {motivoNoSaneado && (
        <p id={`${id}-regla`} className={AYUDA_CLS}>
          {motivoNoSaneado}
        </p>
      )}
    </div>
  );
}

export function GestionEnCurso({ g, san }: { g: GestionSaneamientoEstado; san: SaneamientoBackoffice }) {
  return (
    <div className="flex flex-col gap-5">
      <h3 id={`${g.id}-conceptos`} tabIndex={-1} className={`self-start rounded-[9px] text-base font-semibold text-flit-primary ${FOCO_CLS}`}>
        Conceptos a sanear
      </h3>
      <RenglonesEditables key={g.generacion} renglones={san.renglones} acciones={g.acciones} errores={g.errores} descargas={g.descargas} />
      <Button type="button" variant="secondary" size="sm" className="self-start" onClick={() => g.abrir({ tipo: "agregar" })}>
        <Icono nombre="mas" className="h-4 w-4" />
        Agregar otro concepto
      </Button>
      <CampoObservacion id={g.id} observacion={g.observacion} />
      <CierreSaneamiento id={g.id} motivoNoSaneado={g.motivoNoSaneado} abrir={g.abrir} />
    </div>
  );
}

"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { Info } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Modal } from "@flit/ui/Modal";
import { BOTON_REINTENTAR_CLS } from "@/components/configuracion/estilos";
import type { Fuente } from "@/lib/configuracion";
import {
  FUENTES_PERSONA,
  obtenerDetalleCelda,
  textoMotivoNa,
  textoTipoError,
  traducirErrorDetalle,
  type CargadorDetalle,
  type DetalleCelda as Detalle,
  type ErrorDetalle,
  type ResultadoAvaluoDetalle,
  type ResultadoImpuestoDetalle,
  type ResultadoRuntDetalle,
  type ResultadoSimitDetalle,
} from "@/lib/tablero/detalle";
import { ChipEstadoCelda, fechaDelDato } from "./CeldaSemaforo";
import { Vacio } from "./vistasDetalle/comun";
import { DetalleAvaluo } from "./vistasDetalle/DetalleAvaluo";
import { DetalleImpuesto } from "./vistasDetalle/DetalleImpuesto";
import { SeccionesPersonas } from "./vistasDetalle/DetallePersonaRunt";
import { DetalleRunt } from "./vistasDetalle/DetalleRunt";
import { DetalleSimit } from "./vistasDetalle/DetalleSimit";

export interface DetalleCeldaProps {
  loteId: string;
  vehiculoId: string;
  fuente: Fuente;
  nombreFuente: string;
  identificador: string;
  /** Cierre pedido por la X, Escape o el clic fuera del panel. */
  onClose: () => void;
  /** Cargador del detalle; por defecto GET de la compañía. El backoffice (F06-H5) pasa el suyo con `?empresa=`. */
  cargar?: CargadorDetalle;
  /** Acciones al pie (p. ej. «Llenar a mano» en el backoffice). */
  acciones?: ReactNode;
  /** Elemento que recibe el foco al cerrar (el semáforo); si falta, el que tenía el foco al abrir. */
  devolverFocoA?: HTMLElement | null;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: ErrorDetalle } | { tipo: "listo"; detalle: Detalle };

const ENFOCABLES = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';

/**
 * Atrapa el foco dentro del diálogo que contiene `ancla`, lo pone en su primer control y lo devuelve al cerrar. También lo usa
 * el formulario del llenado manual del backoffice (HU #13802).
 */
export function useFocoAtrapado(ancla: React.RefObject<HTMLElement | null>, devolverFocoA?: HTMLElement | null) {
  useEffect(() => {
    const previo = devolverFocoA ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
    const dialogo = ancla.current?.closest<HTMLElement>('[role="dialog"]');
    if (!dialogo) return;
    const enfocables = () => Array.from(dialogo.querySelectorAll<HTMLElement>(ENFOCABLES));
    enfocables()[0]?.focus();

    const alTeclear = (e: KeyboardEvent) => {
      if (e.key !== "Tab") return;
      const lista = enfocables();
      if (lista.length === 0) return;
      const primero = lista[0];
      const ultimo = lista[lista.length - 1];
      const activo = document.activeElement;
      if (!dialogo.contains(activo)) {
        e.preventDefault();
        primero.focus();
      } else if (e.shiftKey && activo === primero) {
        e.preventDefault();
        ultimo.focus();
      } else if (!e.shiftKey && activo === ultimo) {
        e.preventDefault();
        primero.focus();
      }
    };
    document.addEventListener("keydown", alTeclear);
    return () => {
      document.removeEventListener("keydown", alTeclear);
      if (previo?.isConnected) previo.focus();
    };
  }, [ancla, devolverFocoA]);
}

function Resultado({ detalle }: { detalle: Detalle }) {
  if (FUENTES_PERSONA.has(detalle.fuente) || detalle.personas)
    return (
      <SeccionesPersonas personas={detalle.personas ?? []} rol={detalle.fuente.endsWith("LOCATARIO") ? "LOCATARIO" : "PROPIETARIO"} />
    );
  const r = detalle.resultado;
  if (!r) return <Vacio>Esta consulta no trae detalle para mostrar.</Vacio>;
  if (detalle.fuente === "SIMIT_VEHICULO") return <DetalleSimit resultado={r as ResultadoSimitDetalle} />;
  if (detalle.fuente === "AVALUO") return <DetalleAvaluo resultado={r as ResultadoAvaluoDetalle} />;
  if (detalle.fuente === "RUNT") return <DetalleRunt resultado={r as ResultadoRuntDetalle} />;
  if (detalle.fuente === "IMPUESTO_DEPARTAMENTAL") return <DetalleImpuesto resultado={r as ResultadoImpuestoDetalle} />;
  return <Vacio>Esta consulta no trae detalle para mostrar.</Vacio>;
}

/** Cuerpo del detalle ya cargado, según el estado de la celda. Solo lee las claves del contrato (DTO cerrado, R-20). */
export function VistaDetalleCelda({ detalle }: { detalle: Detalle }) {
  let cuerpo: ReactNode;
  if (detalle.estado === "NA") {
    cuerpo = <Vacio>{textoMotivoNa(detalle.motivo_na)}</Vacio>;
  } else if (detalle.estado === "PENDIENTE" || detalle.estado === "EN_CURSO") {
    cuerpo = <Vacio>La consulta todavía no termina. Vuelve a abrir el detalle en un momento.</Vacio>;
  } else if (detalle.estado === "ERROR") {
    cuerpo = (
      <>
        <InlineAlert tone="error" title="La consulta terminó en error">
          {textoTipoError(detalle.tipo_error)}
        </InlineAlert>
        {detalle.personas && detalle.personas.length > 0 && <Resultado detalle={detalle} />}
      </>
    );
  } else {
    cuerpo = <Resultado detalle={detalle} />;
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <ChipEstadoCelda estado={detalle.estado} />
        {detalle.dato_obtenido_en && (
          <span className="text-xs text-[var(--nav-texto)] dark:text-slate-300">{`Dato del ${fechaDelDato(detalle.dato_obtenido_en)}`}</span>
        )}
        {detalle.reutilizada && (
          <span className="inline-flex items-center gap-1 text-xs text-[var(--nav-texto)] dark:text-slate-300">
            <Info className="h-3.5 w-3.5" aria-hidden="true" />
            Reutilizado de una consulta del mismo día
          </span>
        )}
      </div>
      {cuerpo}
    </div>
  );
}

/**
 * Detalle de una celda del tablero (HU #13801, F06-H4; F06-C1..C4/C12/C13). Diálogo de @flit/ui (overlay con blur, título
 * por aria-labelledby, X y Escape cierran) con el foco atrapado y devuelto al semáforo al cerrar. Cuatro estados: cargando
 * (loader de @flit/ui), error (404 «no disponible» sin datos, 403 sin permiso, otro con Reintentar), vacío (sin personas o
 * sin resultado) y lleno (vista por fuente). La vista de la compañía no tiene ningún indicador de origen manual (Q5).
 */
export function DetalleCelda({ loteId, vehiculoId, fuente, nombreFuente, identificador, onClose, cargar = obtenerDetalleCelda, acciones, devolverFocoA }: DetalleCeldaProps) {
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const ancla = useRef<HTMLDivElement>(null);
  const cargarRef = useRef(cargar);
  useEffect(() => {
    cargarRef.current = cargar;
  }, [cargar]);
  useFocoAtrapado(ancla, devolverFocoA);

  useEffect(() => {
    const control = new AbortController();
    cargarRef.current(loteId, vehiculoId, fuente, control.signal).then(
      (detalle) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", detalle });
      },
      (e: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error: traducirErrorDetalle(e) });
      },
    );
    return () => control.abort();
  }, [loteId, vehiculoId, fuente, intento]);

  function reintentar() {
    setCarga({ tipo: "cargando" });
    setIntento((n) => n + 1);
  }

  let contenido: ReactNode;
  if (carga.tipo === "cargando") {
    contenido = <VehicleLoader label="Cargando el detalle…" />;
  } else if (carga.tipo === "error") {
    const { error } = carga;
    contenido = (
      <InlineAlert
        tone={error.tipo === "NO_DISPONIBLE" ? "warning" : "error"}
        title={error.titulo}
        action={
          error.tipo === "FALLO" ? (
            <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          ) : undefined
        }
      >
        {error.mensaje}
      </InlineAlert>
    );
  } else {
    contenido = <VistaDetalleCelda detalle={carga.detalle} />;
  }

  return (
    <Modal open onClose={onClose} title={`${nombreFuente} · ${identificador}`} size="lg" footer={acciones ? <div className="flex justify-end gap-2">{acciones}</div> : undefined}>
      <div ref={ancla}>{contenido}</div>
    </Modal>
  );
}

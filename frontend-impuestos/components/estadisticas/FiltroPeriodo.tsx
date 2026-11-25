"use client";

import { useId, useState } from "react";
import { CAMPO_FECHA_CLS, CAMPO_INVALIDO_CLS, ERROR_CLS, ETIQUETA_CLS } from "@/components/comun/estilos";
import { validarRango, type ErrorRango } from "@/lib/estadisticas/periodo";
import type { Rango } from "@/lib/estadisticas/tipos";

/**
 * «Desde» y «Hasta» del dashboard y de los reportes (spec-visual §3.1; forma de FiltrosDashboard de Diagnóstico): label
 * visible, `min`/`max` cruzados y `max` = hoy. Un rango válido se aplica al cambiar; uno inválido (más de 366 días, hasta
 * en el futuro o anterior a desde) no se envía: el error va junto al campo con `aria-invalid` y `aria-describedby`. El 400
 * RANGO_INVALIDO del servidor llega por `errorServidor` y se pinta en el mismo sitio.
 */
export function FiltroPeriodo({
  valor,
  hoy,
  onAplicar,
  errorServidor = null,
}: {
  valor: Rango;
  hoy: string;
  onAplicar: (rango: Rango) => void;
  errorServidor?: string | null;
}) {
  const id = useId();
  const [borrador, setBorrador] = useState<Rango>(valor);
  const [aplicado, setAplicado] = useState<Rango>(valor);
  const [error, setError] = useState<ErrorRango | null>(null);
  // El rango cambió desde fuera («Ver los últimos 90 días», «Limpiar filtros»): el borrador lo sigue.
  if (valor.desde !== aplicado.desde || valor.hasta !== aplicado.hasta) {
    setAplicado(valor);
    setBorrador(valor);
    setError(null);
  }

  function cambiar(parcial: Partial<Rango>) {
    const siguiente = { ...borrador, ...parcial };
    setBorrador(siguiente);
    const problema = validarRango(siguiente, hoy);
    setError(problema);
    if (!problema) onAplicar(siguiente);
  }

  const visible: ErrorRango | null = error ?? (errorServidor ? { campo: "hasta", mensaje: errorServidor } : null);
  const errorId = (campo: ErrorRango["campo"]) => `${id}-${campo}-error`;
  const aria = (campo: ErrorRango["campo"]) =>
    visible?.campo === campo ? { "aria-invalid": true as const, "aria-describedby": errorId(campo) } : {};
  const clase = `${CAMPO_FECHA_CLS} ${CAMPO_INVALIDO_CLS} w-full sm:w-44`;
  const mensaje = (campo: ErrorRango["campo"]) =>
    visible?.campo === campo ? (
      <p id={errorId(campo)} role="alert" className={ERROR_CLS}>
        {visible.mensaje}
      </p>
    ) : null;

  return (
    <div role="group" aria-label="Periodo" className="flex w-full flex-wrap items-start gap-3 sm:w-auto">
      <div className="flex w-full flex-col gap-1 sm:w-auto">
        <label htmlFor={`${id}-desde`} className={ETIQUETA_CLS}>
          Desde
        </label>
        <input
          id={`${id}-desde`}
          type="date"
          value={borrador.desde}
          max={borrador.hasta && borrador.hasta < hoy ? borrador.hasta : hoy}
          onChange={(e) => cambiar({ desde: e.target.value })}
          className={clase}
          {...aria("desde")}
        />
        {mensaje("desde")}
      </div>
      <div className="flex w-full flex-col gap-1 sm:w-auto">
        <label htmlFor={`${id}-hasta`} className={ETIQUETA_CLS}>
          Hasta
        </label>
        <input
          id={`${id}-hasta`}
          type="date"
          value={borrador.hasta}
          min={borrador.desde || undefined}
          max={hoy}
          onChange={(e) => cambiar({ hasta: e.target.value })}
          className={clase}
          {...aria("hasta")}
        />
        {mensaje("hasta")}
      </div>
    </div>
  );
}

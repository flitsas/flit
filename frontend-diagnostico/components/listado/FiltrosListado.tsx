"use client";

import { useEffect, useEffectEvent, useId, useState } from "react";
import { CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS } from "@/components/parametrizacion/estilos";
import type { ModoDiagnostico } from "@/lib/diagnosticos/api";
import {
  ESTADOS_FILTRABLES,
  etiquetaEstado,
  MODOS,
  NOMBRE_FILTRO_MAXIMO,
  type CampoFiltro,
  type EstadoFiltrable,
  type FiltrosListado as Filtros,
} from "@/lib/diagnosticos/listado";

/** Espera al escribir el nombre antes de consultar (F08-H2). */
export const ESPERA_NOMBRE_MS = 300;

export interface FiltrosListadoProps {
  valor: Filtros;
  /** 400 con `campo`: el control queda inválido con el mensaje junto a él. */
  error: { campo: CampoFiltro; mensaje: string } | null;
  onCambiar: (cambios: Partial<Filtros>) => void;
}

/**
 * Filtros del listado del inicio (HU #13852): rango de fechas (días de Colombia, inclusivos), tipo, estado y nombre. El
 * nombre espera 300 ms sin teclear antes de consultar. «Sin lanzar» no es un estado filtrable: esos lotes salen sin filtro
 * de estado. Cada control tiene su etiqueta; el error del 400 va en aria-describedby del control que lo causó.
 */
export function FiltrosListado({ valor, error, onCambiar }: FiltrosListadoProps) {
  const id = useId();
  const [nombre, setNombre] = useState(valor.nombre);
  const [nombreExterno, setNombreExterno] = useState(valor.nombre);

  // El nombre de los filtros cambió desde fuera (Limpiar filtros, volver atrás): el campo lo sigue.
  if (valor.nombre !== nombreExterno) {
    setNombreExterno(valor.nombre);
    setNombre(valor.nombre);
  }

  const emitirNombre = useEffectEvent((texto: string) => {
    if (texto !== valor.nombre) onCambiar({ nombre: texto });
  });
  useEffect(() => {
    const espera = setTimeout(() => emitirNombre(nombre), ESPERA_NOMBRE_MS);
    return () => clearTimeout(espera);
  }, [nombre]);

  const errorId = (campo: CampoFiltro) => `${id}-${campo}-error`;
  const invalido = (campo: CampoFiltro) =>
    error?.campo === campo ? { "aria-invalid": true as const, "aria-describedby": errorId(campo) } : {};
  const clase = (campo: CampoFiltro, extra: string) => `${CAMPO_CLS} ${extra} ${error?.campo === campo ? CAMPO_INVALIDO_CLS : ""}`.trim();
  const mensaje = (campo: CampoFiltro) =>
    error?.campo === campo ? (
      <p id={errorId(campo)} className={ERROR_CLS}>
        {error.mensaje}
      </p>
    ) : null;

  return (
    <div role="group" aria-label="Filtros del listado" className="flex flex-wrap items-start gap-4">
      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-desde`} className={ETIQUETA_CLS}>
          Desde
        </label>
        <input
          id={`${id}-desde`}
          type="date"
          value={valor.desde}
          max={valor.hasta || undefined}
          onChange={(e) => onCambiar({ desde: e.target.value })}
          className={clase("desde", "w-44")}
          {...invalido("desde")}
        />
        {mensaje("desde")}
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-hasta`} className={ETIQUETA_CLS}>
          Hasta
        </label>
        <input
          id={`${id}-hasta`}
          type="date"
          value={valor.hasta}
          min={valor.desde || undefined}
          onChange={(e) => onCambiar({ hasta: e.target.value })}
          className={clase("hasta", "w-44")}
          {...invalido("hasta")}
        />
        {mensaje("hasta")}
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-modo`} className={ETIQUETA_CLS}>
          Tipo
        </label>
        <select
          id={`${id}-modo`}
          value={valor.modo ?? ""}
          onChange={(e) => onCambiar({ modo: (e.target.value || null) as ModoDiagnostico | null })}
          className={clase("modo", "w-52")}
          {...invalido("modo")}
        >
          <option value="">Todos</option>
          {MODOS.map((m) => (
            <option key={m.valor} value={m.valor}>
              {m.nombre}
            </option>
          ))}
        </select>
        {mensaje("modo")}
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-estado`} className={ETIQUETA_CLS}>
          Estado
        </label>
        <select
          id={`${id}-estado`}
          value={valor.estado ?? ""}
          onChange={(e) => onCambiar({ estado: (e.target.value || null) as EstadoFiltrable | null })}
          className={clase("estado", "w-44")}
          {...invalido("estado")}
        >
          <option value="">Todos</option>
          {ESTADOS_FILTRABLES.map((e) => (
            <option key={e} value={e}>
              {etiquetaEstado(e).texto}
            </option>
          ))}
        </select>
        {mensaje("estado")}
      </div>
      <div className="flex min-w-56 flex-1 flex-col gap-1">
        <label htmlFor={`${id}-nombre`} className={ETIQUETA_CLS}>
          Nombre
        </label>
        <input
          id={`${id}-nombre`}
          type="search"
          value={nombre}
          maxLength={NOMBRE_FILTRO_MAXIMO}
          placeholder="Buscar por nombre del diagnóstico"
          onChange={(e) => setNombre(e.target.value)}
          className={clase("nombre", "")}
          {...invalido("nombre")}
        />
        {mensaje("nombre")}
      </div>
    </div>
  );
}

"use client";

import { useEffect, useEffectEvent, useId, useState } from "react";
import { BOTON_SECUNDARIO_CLS, CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";
import {
  BUSQUEDA_MAXIMA,
  ESPERA_BUSQUEDA_MS,
  ESTADOS_USUARIO,
  ETIQUETA_ESTADO,
  ETIQUETA_ROL,
  ROLES_DIAGNOSTICO,
  type EstadoUsuario,
  type FiltrosUsuarios as Filtros,
  type RolDiagnostico,
} from "@/lib/usuarios/tipos";

export type ValorFiltros = Pick<Filtros, "busqueda" | "rol" | "estado">;

export interface FiltrosUsuariosProps {
  valor: ValorFiltros;
  onCambiar: (cambios: Partial<ValorFiltros>) => void;
  /** «Limpiar filtros»: solo se ofrece con algún filtro aplicado. */
  onLimpiar?: () => void;
}

/** Hay algún filtro aplicado (para distinguir «sin usuarios» de «ningún usuario coincide»). */
export function hayFiltrosUsuarios(v: ValorFiltros): boolean {
  return Boolean(v.busqueda.trim() || v.rol || v.estado);
}

/**
 * Barra de filtros del módulo Usuarios (spec-ui §10.1): buscador por nombre o correo (espera 300 ms tras la última tecla),
 * Rol y Estado, y «Limpiar filtros». Filtran en el servidor (contrato §3.3: busqueda, rol, estado). Cada control tiene su
 * etiqueta visible. El buscador ocupa el ancho completo por debajo de 640 px.
 */
export function FiltrosUsuarios({ valor, onCambiar, onLimpiar }: FiltrosUsuariosProps) {
  const id = useId();
  const [busqueda, setBusqueda] = useState(valor.busqueda);
  const [externa, setExterna] = useState(valor.busqueda);

  // La búsqueda cambió desde fuera (Limpiar filtros): el campo la sigue.
  if (valor.busqueda !== externa) {
    setExterna(valor.busqueda);
    setBusqueda(valor.busqueda);
  }

  const emitir = useEffectEvent((texto: string) => {
    if (texto !== valor.busqueda) onCambiar({ busqueda: texto });
  });
  useEffect(() => {
    const espera = setTimeout(() => emitir(busqueda), ESPERA_BUSQUEDA_MS);
    return () => clearTimeout(espera);
  }, [busqueda]);

  return (
    <div role="group" aria-label="Filtros de usuarios" className="flex flex-wrap items-end gap-4">
      <div className="flex w-full min-w-56 flex-col gap-1 sm:w-auto sm:flex-1">
        <label htmlFor={`${id}-busqueda`} className={ETIQUETA_CLS}>
          Buscar por nombre o correo
        </label>
        <input
          id={`${id}-busqueda`}
          type="text"
          enterKeyHint="search"
          autoComplete="off"
          value={busqueda}
          maxLength={BUSQUEDA_MAXIMA}
          onChange={(e) => setBusqueda(e.target.value)}
          className={CAMPO_CLS}
        />
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-rol`} className={ETIQUETA_CLS}>
          Rol
        </label>
        <select
          id={`${id}-rol`}
          value={valor.rol ?? ""}
          onChange={(e) => onCambiar({ rol: (e.target.value || null) as RolDiagnostico | null })}
          className={`${CAMPO_CLS} w-60`}
        >
          <option value="">Todos</option>
          {ROLES_DIAGNOSTICO.map((r) => (
            <option key={r} value={r}>
              {ETIQUETA_ROL[r]}
            </option>
          ))}
        </select>
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-estado`} className={ETIQUETA_CLS}>
          Estado
        </label>
        <select
          id={`${id}-estado`}
          value={valor.estado ?? ""}
          onChange={(e) => onCambiar({ estado: (e.target.value || null) as EstadoUsuario | null })}
          className={`${CAMPO_CLS} w-44`}
        >
          <option value="">Todos</option>
          {ESTADOS_USUARIO.map((e) => (
            <option key={e} value={e}>
              {ETIQUETA_ESTADO[e]}
            </option>
          ))}
        </select>
      </div>
      {onLimpiar && (
        <button type="button" onClick={onLimpiar} className={`${BOTON_SECUNDARIO_CLS} min-h-11`}>
          Limpiar filtros
        </button>
      )}
    </div>
  );
}

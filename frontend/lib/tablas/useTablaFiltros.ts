"use client";

import { useCallback, useState } from "react";
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from "@/lib/validation/fieldRules";
import { condicionEsValida, type CondicionFiltro, type ColumnaFiltro } from "./filtrar-filas";

export interface UseTablaFiltrosOptions {
  columnas: readonly ColumnaFiltro[];
  /**
   * Columna `fecha` sobre la que actúa el botón «Periodo» de la barra (solo si se pinta, ver
   * `TablaFiltrosBar.periodo`). El encabezado de cualquier columna `fecha` filtra sin necesitarlo
   * y comparte estado con ese botón: es la misma condición.
   */
  periodoColumnaId?: string;
  /**
   * Se invoca cada vez que cambia lo APLICADO (aplicar/quitar una columna, limpiar, escribir en el
   * buscador): ahí el contenedor vuelve a la página 1 (`usePaginacion().setPagina(1)`).
   */
  onCambio?: () => void;
}

export interface TablaFiltrosEstado {
  columnas: readonly ColumnaFiltro[];
  periodoColumnaId?: string;
  /** Texto del buscador (el buscador filtra mientras se escribe). */
  busqueda: string;
  setBusqueda: (v: string) => void;
  /** Condiciones aplicadas (una por columna, ya sin vacías): lo que se pasa a `filtrarFilas` / `condicionesAParams`. */
  condiciones: CondicionFiltro[];
  /** Igual que `condiciones` (se conserva el nombre que ya usan las tablas). */
  condicionesTotales: CondicionFiltro[];
  /** Número de columnas con filtro aplicado. */
  count: number;
  hayFiltros: boolean;
  /** Condición aplicada de una columna (o `undefined`). */
  condicionDe: (columnaId: string) => CondicionFiltro | undefined;
  /** Aplica la condición de UNA columna. Una condición vacía equivale a quitarla. */
  aplicarColumna: (columnaId: string, condicion: CondicionFiltro | null) => void;
  /** Quita el filtro de una columna. */
  quitarColumna: (columnaId: string) => void;
  /** Quita todas las condiciones y el buscador, y avisa para volver a la página 1. */
  limpiar: () => void;
}

/** Estado de los filtros genéricos por columna: condiciones aplicadas, buscador, `limpiar` y `onCambio`. */
export function useTablaFiltros({
  columnas,
  periodoColumnaId,
  onCambio,
}: UseTablaFiltrosOptions): TablaFiltrosEstado {
  const [condiciones, setCondiciones] = useState<CondicionFiltro[]>([]);
  const [busqueda, setBusquedaState] = useState("");

  const aplicarColumna = useCallback(
    (columnaId: string, condicion: CondicionFiltro | null) => {
      setCondiciones((prev) => {
        const resto = prev.filter((c) => c.columnaId !== columnaId);
        return condicion && condicionEsValida(condicion) ? [...resto, condicion] : resto;
      });
      onCambio?.();
    },
    [onCambio],
  );

  const quitarColumna = useCallback(
    (columnaId: string) => aplicarColumna(columnaId, null),
    [aplicarColumna],
  );

  const limpiar = useCallback(() => {
    setCondiciones([]);
    setBusquedaState("");
    onCambio?.();
  }, [onCambio]);

  const setBusqueda = useCallback(
    (v: string) => {
      setBusquedaState(sanitizeNoAngleBrackets(v).slice(0, SEARCH_TEXT_MAX_LENGTH));
      onCambio?.();
    },
    [onCambio],
  );

  const condicionDe = useCallback(
    (columnaId: string) => condiciones.find((c) => c.columnaId === columnaId),
    [condiciones],
  );

  return {
    columnas,
    periodoColumnaId,
    busqueda,
    setBusqueda,
    condiciones,
    condicionesTotales: condiciones,
    count: condiciones.length,
    hayFiltros: condiciones.length > 0 || busqueda.trim() !== "",
    condicionDe,
    aplicarColumna,
    quitarColumna,
    limpiar,
  };
}

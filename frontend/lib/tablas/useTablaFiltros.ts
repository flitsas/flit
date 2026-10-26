"use client";

import { useCallback, useMemo, useState } from "react";
import { rangoDePeriodo } from "@/components/operacion/TramitesFiltrosBar";
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from "@/lib/validation/fieldRules";
import { condicionEsValida, type CondicionFiltro, type ColumnaFiltro } from "./filtrar-filas";

/** Lo que se edita en los popovers y todavía NO filtra la tabla (se aplica con «Aplicar»). */
interface Borrador {
  condiciones: CondicionFiltro[];
  periodo: string;
  rangoDesde: string;
  rangoHasta: string;
}

interface PeriodoAplicado {
  columnaId: string;
  desde: string;
  hasta: string;
}

const BORRADOR_VACIO: Borrador = {
  condiciones: [],
  periodo: "Sin periodo",
  rangoDesde: "",
  rangoHasta: "",
};

export interface UseTablaFiltrosOptions {
  columnas: readonly ColumnaFiltro[];
  /** `id` de la columna `fecha` sobre la que actúa «Periodo». Sin él, no hay Periodo. */
  periodoColumnaId?: string;
  /**
   * Se invoca cada vez que cambia lo APLICADO (aplicar, limpiar, escribir en el buscador): ahí el
   * contenedor vuelve a la página 1 (`usePaginacion().setPagina(1)`) o reinicia su paginación.
   */
  onCambio?: () => void;
}

export interface TablaFiltrosEstado {
  columnas: readonly ColumnaFiltro[];
  periodoColumnaId?: string;
  borrador: Borrador;
  setCondicionesBorrador: (c: CondicionFiltro[]) => void;
  setPeriodo: (v: string) => void;
  setRangoDesde: (v: string) => void;
  setRangoHasta: (v: string) => void;
  /** Texto del buscador (ya aplicado: el buscador filtra mientras se escribe). */
  busqueda: string;
  setBusqueda: (v: string) => void;
  /** Condiciones de «+ Filtro» ya aplicadas, SIN el periodo. */
  condiciones: CondicionFiltro[];
  /** Condiciones aplicadas + el periodo convertido en condición `fecha`: lo que se pasa a `filtrarFilas`. */
  condicionesTotales: CondicionFiltro[];
  /** Número de condiciones de «+ Filtro» aplicadas (lo que numera el disparador). */
  count: number;
  hayFiltros: boolean;
  /** Aplica el borrador. Opcionalmente reemplaza las condiciones del borrador en el mismo paso. */
  aplicar: (condiciones?: CondicionFiltro[]) => void;
  /** Quita condiciones, periodo y buscador, y avisa para volver a la página 1. */
  limpiar: () => void;
}

/**
 * Estado de los filtros genéricos: borrador vs. aplicado, `aplicar`, `limpiar`, contador y aviso
 * (`onCambio`) para volver a la página 1.
 */
export function useTablaFiltros({
  columnas,
  periodoColumnaId,
  onCambio,
}: UseTablaFiltrosOptions): TablaFiltrosEstado {
  const [borrador, setBorrador] = useState<Borrador>(BORRADOR_VACIO);
  const [condiciones, setCondiciones] = useState<CondicionFiltro[]>([]);
  const [periodoAplicado, setPeriodoAplicado] = useState<PeriodoAplicado | null>(null);
  const [busqueda, setBusquedaState] = useState("");

  const resolverPeriodo = useCallback(
    (b: Borrador): PeriodoAplicado | null => {
      if (!periodoColumnaId || b.periodo === "Sin periodo") return null;
      if (b.periodo === "Rango propio") {
        if (!b.rangoDesde && !b.rangoHasta) return null;
        return { columnaId: periodoColumnaId, desde: b.rangoDesde, hasta: b.rangoHasta };
      }
      const r = rangoDePeriodo(b.periodo, new Date());
      return r ? { columnaId: periodoColumnaId, ...r } : null;
    },
    [periodoColumnaId],
  );

  const aplicarBorrador = useCallback(
    (b: Borrador) => {
      const limpias = b.condiciones.filter(condicionEsValida);
      setCondiciones(limpias);
      setBorrador({ ...b, condiciones: limpias });
      setPeriodoAplicado(resolverPeriodo(b));
      onCambio?.();
    },
    [onCambio, resolverPeriodo],
  );

  const aplicar = useCallback(
    (nuevas?: CondicionFiltro[]) => {
      aplicarBorrador(nuevas ? { ...borrador, condiciones: nuevas } : borrador);
    },
    [aplicarBorrador, borrador],
  );

  const limpiar = useCallback(() => {
    setBorrador(BORRADOR_VACIO);
    setCondiciones([]);
    setPeriodoAplicado(null);
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

  const setPeriodo = useCallback(
    (v: string) => {
      if (v === "Sin periodo") {
        // «Limpiar» del popover de Periodo no pulsa «Aplicar»: quitar el periodo se aplica de una vez.
        setBorrador((b) => ({ ...b, periodo: v, rangoDesde: "", rangoHasta: "" }));
        setPeriodoAplicado(null);
        onCambio?.();
        return;
      }
      setBorrador((b) => ({ ...b, periodo: v }));
    },
    [onCambio],
  );

  const condicionesTotales = useMemo<CondicionFiltro[]>(
    () =>
      periodoAplicado
        ? [
            ...condiciones,
            {
              columnaId: periodoAplicado.columnaId,
              tipo: "fecha",
              desde: periodoAplicado.desde,
              hasta: periodoAplicado.hasta,
            },
          ]
        : condiciones,
    [condiciones, periodoAplicado],
  );

  return {
    columnas,
    periodoColumnaId,
    borrador,
    setCondicionesBorrador: (c) => setBorrador((b) => ({ ...b, condiciones: c })),
    setPeriodo,
    setRangoDesde: (v) => setBorrador((b) => ({ ...b, rangoDesde: v })),
    setRangoHasta: (v) => setBorrador((b) => ({ ...b, rangoHasta: v })),
    busqueda,
    setBusqueda,
    condiciones,
    condicionesTotales,
    count: condiciones.length,
    hayFiltros: condicionesTotales.length > 0 || busqueda.trim() !== "",
    aplicar,
    limpiar,
  };
}

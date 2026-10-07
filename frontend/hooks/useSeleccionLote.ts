'use client';

import { useCallback, useMemo, useState } from 'react';

/**
 * HU #13380 (épica #13216) — selección de trámites para un lote de descarga masiva.
 *
 * <p>El modelo es `{ modo, ids, excluidos }` y NO una lista de filas: con «Seleccionar todos» el
 * usuario elige el FILTRO entero (puede ser de miles de trámites que nunca llegaron a la página),
 * así que lo que se guarda es el criterio más las excepciones que desmarcó. El servidor resuelve el
 * universo al crear el lote (#13381); aquí solo se cuenta.</p>
 *
 * <p>Es genérico en el tipo de filtro porque lo reutiliza la bandeja del OT (#13393) con su propio
 * filtro. El hook no sabe qué hay dentro: lo compara por una clave estable, y cuando la clave cambia
 * la selección se reinicia (AC4) — una selección «todos los del filtro F» no significa nada bajo G.</p>
 *
 * <p>El contador es EXACTO, nunca estimado (AC2): `total` lo da el servidor para el filtro vigente y
 * se le restan las exclusiones; en selección manual es el número de ids marcados.</p>
 */

/** Tope de ids marcados a mano o de exclusiones (CF-03). Más allá, el filtro es la herramienta. */
export const TOPE_SELECCION_LOTE = 10_000;

export type ModoSeleccionLote = 'ids' | 'filtro';

/** Lo que la cabecera «Seleccionar todos» debe reflejar (AC7). */
export type EstadoCabeceraLote = 'nada' | 'parcial' | 'todo';

/** Modelo serializable de la selección: es lo que #13381 envía al crear el lote. */
export interface ModeloSeleccionLote<TFiltro> {
  modo: ModoSeleccionLote;
  /** Ids marcados uno a uno (solo en modo `ids`). */
  ids: string[];
  /** Ids desmarcados dentro de «todos los del filtro» (solo en modo `filtro`). */
  excluidos: string[];
  /** Filtro capturado al marcar «Seleccionar todos»; `null` en modo `ids`. */
  filtro: TFiltro | null;
}

export interface UseSeleccionLoteOptions<TFiltro> {
  /** Criterios vigentes del listado (sin paginación ni orden: cambiar de página no reinicia). */
  filtro: TFiltro;
  /** Total que el servidor informa para `filtro`. */
  total: number;
  /** Clave de comparación del filtro. Por defecto, JSON con claves ordenadas. */
  claveFiltro?: (filtro: TFiltro) => string;
  /** Tope de ids / exclusiones. Por defecto {@link TOPE_SELECCION_LOTE}. */
  tope?: number;
}

export interface SeleccionLote<TFiltro> {
  modo: ModoSeleccionLote;
  /** Cuántos trámites van en la selección, exacto. */
  contador: number;
  estadoCabecera: EstadoCabeceraLote;
  /** Se llegó al tope: no se admiten más ids (modo `ids`) ni más exclusiones (modo `filtro`). */
  topeAlcanzado: boolean;
  /** Mensaje del tope (sugiere usar el filtro) o `null` si no se ha llegado. */
  mensajeTope: string | null;
  estaSeleccionado: (id: string) => boolean;
  /** Marca o desmarca una fila. Devuelve `false` si el tope impidió el cambio. */
  alternar: (id: string) => boolean;
  /** Marca «todos los del filtro» (sin exclusiones). */
  seleccionarTodos: () => void;
  /** Vacía la selección. */
  limpiar: () => void;
  /** Acción de la casilla de cabecera: si está «todo», limpia; si no, selecciona todos. */
  alternarTodos: () => void;
  modelo: ModeloSeleccionLote<TFiltro>;
}

interface EstadoInterno<TFiltro> {
  clave: string;
  modo: ModoSeleccionLote;
  ids: ReadonlySet<string>;
  excluidos: ReadonlySet<string>;
  filtro: TFiltro | null;
}

const VACIO: ReadonlySet<string> = new Set<string>();

function vacio<TFiltro>(clave: string): EstadoInterno<TFiltro> {
  return { clave, modo: 'ids', ids: VACIO, excluidos: VACIO, filtro: null };
}

/** JSON con las claves de los objetos ordenadas: el mismo filtro da la misma clave sin importar
 *  el orden en que se armó el objeto. */
export function claveEstable(valor: unknown): string {
  return JSON.stringify(valor, (_k, v: unknown) => {
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      return Object.fromEntries(
        Object.entries(v as Record<string, unknown>).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)),
      );
    }
    return v;
  }) ?? '';
}

const formatoMiles = (n: number) => n.toLocaleString('es-CO');

export function useSeleccionLote<TFiltro>({
  filtro,
  total,
  claveFiltro,
  tope = TOPE_SELECCION_LOTE,
}: UseSeleccionLoteOptions<TFiltro>): SeleccionLote<TFiltro> {
  const clave = useMemo(
    () => (claveFiltro ? claveFiltro(filtro) : claveEstable(filtro)),
    [claveFiltro, filtro],
  );
  const [interno, setInterno] = useState<EstadoInterno<TFiltro>>(() => vacio(clave));

  // AC4 — el filtro cambió: la selección se reinicia. Es un ajuste de estado DURANTE el render
  // (patrón de React para «estado que depende de una prop»), no un efecto: así ningún render pinta
  // la selección vieja bajo el filtro nuevo, y volver al filtro anterior tampoco la resucita.
  let vigente = interno;
  if (interno.clave !== clave) {
    vigente = vacio(clave);
    setInterno(vigente);
  }

  const totalSeguro = Math.max(0, total);
  const contador =
    vigente.modo === 'filtro'
      ? Math.max(0, totalSeguro - vigente.excluidos.size)
      : vigente.ids.size;

  let estadoCabecera: EstadoCabeceraLote;
  if (contador === 0) estadoCabecera = 'nada';
  else if (vigente.modo === 'filtro') estadoCabecera = vigente.excluidos.size === 0 ? 'todo' : 'parcial';
  else estadoCabecera = totalSeguro > 0 && vigente.ids.size >= totalSeguro ? 'todo' : 'parcial';

  const topeAlcanzado =
    vigente.modo === 'filtro' ? vigente.excluidos.size >= tope : vigente.ids.size >= tope;
  const mensajeTope = topeAlcanzado
    ? vigente.modo === 'filtro'
      ? `Llegaste al tope de ${formatoMiles(tope)} trámites desmarcados. Ajusta el filtro para dejar fuera los que no quieres incluir.`
      : `Llegaste al tope de ${formatoMiles(tope)} trámites marcados uno a uno. Usa el filtro y «Seleccionar todos» para incluir más.`
    : null;

  const estaSeleccionado = useCallback(
    (id: string) => (vigente.modo === 'filtro' ? !vigente.excluidos.has(id) : vigente.ids.has(id)),
    [vigente],
  );

  const alternar = useCallback(
    (id: string): boolean => {
      // La respuesta se calcula sobre lo pintado; la escritura, sobre el estado más reciente (dos
      // clics en el mismo tick no se pisan).
      const conjunto = vigente.modo === 'filtro' ? vigente.excluidos : vigente.ids;
      if (!conjunto.has(id) && conjunto.size >= tope) return false;
      setInterno((prev) => {
        const base = prev.clave === clave ? prev : vacio<TFiltro>(clave);
        const campo = base.modo === 'filtro' ? 'excluidos' : 'ids';
        const siguiente = new Set(base[campo]);
        if (siguiente.has(id)) siguiente.delete(id);
        else if (siguiente.size >= tope) return base;
        else siguiente.add(id);
        return { ...base, [campo]: siguiente };
      });
      return true;
    },
    [vigente, clave, tope],
  );

  const seleccionarTodos = useCallback(() => {
    setInterno({ clave, modo: 'filtro', ids: VACIO, excluidos: VACIO, filtro });
  }, [clave, filtro]);

  const limpiar = useCallback(() => setInterno(vacio(clave)), [clave]);

  const alternarTodos = useCallback(() => {
    if (estadoCabecera === 'todo') limpiar();
    else seleccionarTodos();
  }, [estadoCabecera, limpiar, seleccionarTodos]);

  const modelo = useMemo<ModeloSeleccionLote<TFiltro>>(
    () => ({
      modo: vigente.modo,
      ids: vigente.modo === 'ids' ? [...vigente.ids] : [],
      excluidos: vigente.modo === 'filtro' ? [...vigente.excluidos] : [],
      filtro: vigente.modo === 'filtro' ? vigente.filtro : null,
    }),
    [vigente],
  );

  return {
    modo: vigente.modo,
    contador,
    estadoCabecera,
    topeAlcanzado,
    mensajeTope,
    estaSeleccionado,
    alternar,
    seleccionarTodos,
    limpiar,
    alternarTodos,
    modelo,
  };
}

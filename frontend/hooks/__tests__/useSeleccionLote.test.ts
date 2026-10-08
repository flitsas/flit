import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';

import {
  TOPE_SELECCION_LOTE,
  claveEstable,
  useSeleccionLote,
  type UseSeleccionLoteOptions,
} from '@/hooks/useSeleccionLote';

// Uso de ejemplo:
//   const lote = useSeleccionLote({ filtro: { estado: 'radicado' }, total: 350 });
//   lote.seleccionarTodos();      // lote.contador === 350, lote.modo === 'filtro'
//   lote.alternar('t-1');         // desmarca → lote.contador === 349
//   lote.modelo                   // { modo: 'filtro', ids: [], excluidos: ['t-1'], filtro: {...} }

type Filtro = { estado?: string; busqueda?: string };

function montar(inicial: UseSeleccionLoteOptions<Filtro>) {
  return renderHook((props: UseSeleccionLoteOptions<Filtro>) => useSeleccionLote(props), {
    initialProps: inicial,
  });
}

describe('useSeleccionLote — HU #13380', () => {
  it('arranca vacío: contador 0, cabecera «nada», modo ids', () => {
    const { result } = montar({ filtro: {}, total: 350 });
    expect(result.current.contador).toBe(0);
    expect(result.current.estadoCabecera).toBe('nada');
    expect(result.current.modo).toBe('ids');
    expect(result.current.mensajeTope).toBeNull();
  });

  it('AC2 — «Seleccionar todos» cuenta el total del servidor y resta exclusiones exactas', () => {
    const { result } = montar({ filtro: { estado: 'radicado' }, total: 350 });
    act(() => result.current.seleccionarTodos());
    expect(result.current.contador).toBe(350);
    expect(result.current.estadoCabecera).toBe('todo');
    expect(result.current.modo).toBe('filtro');

    act(() => {
      result.current.alternar('t-1');
      result.current.alternar('t-2');
    });
    expect(result.current.contador).toBe(348);
    expect(result.current.estadoCabecera).toBe('parcial');
    expect(result.current.estaSeleccionado('t-1')).toBe(false);
    expect(result.current.estaSeleccionado('t-3')).toBe(true);

    // Volver a marcar una excluida la reincorpora.
    act(() => result.current.alternar('t-1'));
    expect(result.current.contador).toBe(349);
  });

  it('AC2 — el contador sigue al total del servidor (no lo estima) dentro del mismo filtro', () => {
    const { result, rerender } = montar({ filtro: { estado: 'radicado' }, total: 350 });
    act(() => result.current.seleccionarTodos());
    act(() => result.current.alternar('t-9'));
    rerender({ filtro: { estado: 'radicado' }, total: 352 });
    expect(result.current.contador).toBe(351);
  });

  it('AC3 — selección manual: 3 filas cuentan 3 y un rerender con el mismo filtro (otra página) las conserva', () => {
    const { result, rerender } = montar({ filtro: { estado: 'radicado' }, total: 350 });
    act(() => {
      result.current.alternar('a');
      result.current.alternar('b');
      result.current.alternar('c');
    });
    expect(result.current.contador).toBe(3);
    expect(result.current.estadoCabecera).toBe('parcial');
    // Un objeto filtro NUEVO pero equivalente (otra página, orden distinto de claves) no reinicia.
    rerender({ filtro: { estado: 'radicado' }, total: 350 });
    expect(result.current.contador).toBe(3);
    expect(result.current.estaSeleccionado('b')).toBe(true);
  });

  it('AC3 — desmarcar una fila manual la quita y la cabecera vuelve a «nada» al vaciarse', () => {
    const { result } = montar({ filtro: {}, total: 5 });
    act(() => result.current.alternar('a'));
    act(() => result.current.alternar('a'));
    expect(result.current.contador).toBe(0);
    expect(result.current.estadoCabecera).toBe('nada');
  });

  it('AC4 — cambiar el filtro reinicia la selección «todos» y el contador vuelve a 0', () => {
    const { result, rerender } = montar({ filtro: { estado: 'radicado' }, total: 350 });
    act(() => result.current.seleccionarTodos());
    expect(result.current.contador).toBe(350);

    rerender({ filtro: { estado: 'radicado', busqueda: 'ABC' }, total: 350 });
    expect(result.current.contador).toBe(0);
    expect(result.current.modo).toBe('ids');
    expect(result.current.estadoCabecera).toBe('nada');
  });

  it('AC4 — volver al filtro anterior no resucita la selección vieja', () => {
    const { result, rerender } = montar({ filtro: { estado: 'radicado' }, total: 350 });
    act(() => result.current.seleccionarTodos());
    rerender({ filtro: { estado: 'borrador' }, total: 10 });
    rerender({ filtro: { estado: 'radicado' }, total: 350 });
    expect(result.current.contador).toBe(0);
  });

  it('AC5 — selección manual: en el tope no se admite una más y el mensaje sugiere el filtro', () => {
    const { result } = montar({ filtro: {}, total: 50, tope: 3 });
    act(() => {
      result.current.alternar('a');
      result.current.alternar('b');
      result.current.alternar('c');
    });
    let admitido = true;
    act(() => {
      admitido = result.current.alternar('d');
    });
    expect(admitido).toBe(false);
    expect(result.current.contador).toBe(3);
    expect(result.current.topeAlcanzado).toBe(true);
    expect(result.current.mensajeTope).toMatch(/filtro/i);
    expect(result.current.mensajeTope).toMatch(/Seleccionar todos/);
    // Desmarcar sí se admite en el tope.
    act(() => {
      admitido = result.current.alternar('a');
    });
    expect(admitido).toBe(true);
    expect(result.current.contador).toBe(2);
    expect(result.current.mensajeTope).toBeNull();
  });

  it('AC5 — exclusiones: en el tope no se admite una más y el mensaje sugiere ajustar el filtro', () => {
    const { result } = montar({ filtro: {}, total: 100, tope: 2 });
    act(() => result.current.seleccionarTodos());
    act(() => {
      result.current.alternar('a');
      result.current.alternar('b');
    });
    let admitido = true;
    act(() => {
      admitido = result.current.alternar('c');
    });
    expect(admitido).toBe(false);
    expect(result.current.contador).toBe(98);
    expect(result.current.mensajeTope).toMatch(/Ajusta el filtro/);
  });

  it('AC5 — el tope por defecto es 10.000 y el mensaje lo dice con separador de miles', () => {
    expect(TOPE_SELECCION_LOTE).toBe(10_000);
    const { result } = montar({ filtro: {}, total: 20_000 });
    act(() => {
      for (let i = 0; i <= TOPE_SELECCION_LOTE; i++) result.current.alternar(`id-${i}`);
    });
    expect(result.current.contador).toBe(10_000);
    expect(result.current.mensajeTope).toContain('10.000');
  }, 60_000);

  it('AC7 — alternarTodos: desde «nada» o «parcial» marca todos; desde «todo» limpia', () => {
    const { result } = montar({ filtro: {}, total: 20 });
    act(() => result.current.alternar('a'));
    expect(result.current.estadoCabecera).toBe('parcial');
    act(() => result.current.alternarTodos());
    expect(result.current.estadoCabecera).toBe('todo');
    expect(result.current.contador).toBe(20);
    act(() => result.current.alternarTodos());
    expect(result.current.estadoCabecera).toBe('nada');
    expect(result.current.contador).toBe(0);
  });

  it('AC7 — marcar a mano todas las filas del universo deja la cabecera en «todo»', () => {
    const { result } = montar({ filtro: {}, total: 2 });
    act(() => {
      result.current.alternar('a');
      result.current.alternar('b');
    });
    expect(result.current.estadoCabecera).toBe('todo');
  });

  it('borde — total negativo o 0 con «todos» no da contador negativo', () => {
    const { result } = montar({ filtro: {}, total: -5 });
    act(() => result.current.seleccionarTodos());
    expect(result.current.contador).toBe(0);
    expect(result.current.estadoCabecera).toBe('nada');
  });

  it('contrato — el modelo serializable lleva {modo, ids, excluidos, filtro}', () => {
    const { result } = montar({ filtro: { estado: 'radicado' }, total: 10 });
    act(() => result.current.alternar('x'));
    expect(result.current.modelo).toEqual({ modo: 'ids', ids: ['x'], excluidos: [], filtro: null });

    act(() => result.current.seleccionarTodos());
    act(() => result.current.alternar('y'));
    expect(result.current.modelo).toEqual({
      modo: 'filtro',
      ids: [],
      excluidos: ['y'],
      filtro: { estado: 'radicado' },
    });
  });

  it('contrato — genérico: acepta una clave propia del filtro (bandeja OT, #13393)', () => {
    type FiltroOt = { otId: string; pagina: number };
    const { result, rerender } = renderHook(
      (props: UseSeleccionLoteOptions<FiltroOt>) => useSeleccionLote<FiltroOt>(props),
      {
        initialProps: { filtro: { otId: 'ot-1', pagina: 1 }, total: 40, claveFiltro: (f) => f.otId },
      },
    );
    act(() => result.current.seleccionarTodos());
    rerender({ filtro: { otId: 'ot-1', pagina: 2 }, total: 40, claveFiltro: (f) => f.otId });
    expect(result.current.contador).toBe(40);
    rerender({ filtro: { otId: 'ot-2', pagina: 1 }, total: 40, claveFiltro: (f) => f.otId });
    expect(result.current.contador).toBe(0);
  });

  it('claveEstable — el orden de las claves no cambia la clave', () => {
    expect(claveEstable({ a: 1, b: { d: 2, c: 3 } })).toBe(claveEstable({ b: { c: 3, d: 2 }, a: 1 }));
    expect(claveEstable({ a: 1 })).not.toBe(claveEstable({ a: 2 }));
  });
});

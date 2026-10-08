import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';

import { useSeleccionLote, type UseSeleccionLoteOptions } from '@/hooks/useSeleccionLote';

// Uso de ejemplo (HU #13419 — vista de red):
//   const alcance = networkActive ? (childTenantId ?? 'red') : null;
//   const lote = useSeleccionLote({ filtro, total, alcance });
//   lote.alcance   // 'red' | '<uuid hija>' | null — lo que viaja como `alcanceRed` en la raíz
//   // Cambiar el alcance reinicia la selección (AC3), igual que cambiar el filtro.

type Filtro = { estado?: string };
const HIJA = '22222222-2222-2222-2222-222222222222';

function montar(inicial: UseSeleccionLoteOptions<Filtro>) {
  return renderHook((props: UseSeleccionLoteOptions<Filtro>) => useSeleccionLote(props), {
    initialProps: inicial,
  });
}

describe('useSeleccionLote — alcance de red en la clave (HU #13419)', () => {
  it('AC1 — casillas a mano con la red: el modelo es modo ids y `alcance` expone "red"', () => {
    const { result } = montar({ filtro: {}, total: 10, alcance: 'red' });
    act(() => {
      result.current.alternar('inst-propio');
      result.current.alternar('inst-hija');
    });
    expect(result.current.alcance).toBe('red');
    expect(result.current.modelo).toEqual({
      modo: 'ids',
      ids: ['inst-propio', 'inst-hija'],
      excluidos: [],
      filtro: null,
    });
  });

  it('AC2 — «Seleccionar todos» con una hija: modo filtro, contador = total de la vista y alcance = hija', () => {
    const { result } = montar({ filtro: { estado: 'preparado' }, total: 57, alcance: HIJA });
    act(() => result.current.seleccionarTodos());
    expect(result.current.contador).toBe(57);
    expect(result.current.alcance).toBe(HIJA);
    expect(result.current.modelo.modo).toBe('filtro');
    // El filtro del modelo NO lleva el alcance (va en la raíz del cuerpo).
    expect(result.current.modelo.filtro).toEqual({ estado: 'preparado' });
  });

  it('AC3 — pasar de toda la red a una hija reinicia la selección', () => {
    const { result, rerender } = montar({ filtro: {}, total: 10, alcance: 'red' });
    act(() => result.current.alternar('inst-1'));
    expect(result.current.contador).toBe(1);
    rerender({ filtro: {}, total: 10, alcance: HIJA });
    expect(result.current.contador).toBe(0);
    expect(result.current.estaSeleccionado('inst-1')).toBe(false);
    expect(result.current.alcance).toBe(HIJA);
  });

  it('AC3 — desactivar la vista de red (alcance null) reinicia «todos los del filtro»', () => {
    const { result, rerender } = montar({ filtro: {}, total: 10, alcance: 'red' });
    act(() => result.current.seleccionarTodos());
    expect(result.current.contador).toBe(10);
    rerender({ filtro: {}, total: 4, alcance: null });
    expect(result.current.contador).toBe(0);
    expect(result.current.modo).toBe('ids');
    expect(result.current.alcance).toBeNull();
  });

  it('AC3 — volver al alcance anterior no resucita la selección vieja', () => {
    const { result, rerender } = montar({ filtro: {}, total: 10, alcance: 'red' });
    act(() => result.current.alternar('inst-1'));
    rerender({ filtro: {}, total: 10, alcance: null });
    rerender({ filtro: {}, total: 10, alcance: 'red' });
    expect(result.current.contador).toBe(0);
  });

  it('borde — sin `alcance` (bandeja OT) el comportamiento es el de siempre y `alcance` es null', () => {
    const { result, rerender } = montar({ filtro: { estado: 'a' }, total: 3 });
    act(() => result.current.alternar('x'));
    rerender({ filtro: { estado: 'a' }, total: 3 });
    expect(result.current.contador).toBe(1);
    expect(result.current.alcance).toBeNull();
  });
});

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { SONDEO_CONTEOS_MS, useSondeoDeConteos } from '../useSondeoDeConteos';

// Epic #12686 — HU #12808.
let visibilidad: DocumentVisibilityState = 'visible';

function cambiarVisibilidad(v: DocumentVisibilityState) {
  visibilidad = v;
  document.dispatchEvent(new Event('visibilitychange'));
}

beforeEach(() => {
  vi.useFakeTimers();
  visibilidad = 'visible';
  vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibilidad);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

async function avanzar(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe('useSondeoDeConteos', () => {
  it('AC2 — con la pestaña visible pide los conteos cada 60 segundos', async () => {
    const pedir = vi.fn().mockResolvedValue({ entregado: 3 });
    const alRecibir = vi.fn();
    renderHook(() => useSondeoDeConteos({ pedir, alRecibir }));

    expect(pedir).not.toHaveBeenCalled();
    await avanzar(SONDEO_CONTEOS_MS);
    expect(pedir).toHaveBeenCalledTimes(1);
    expect(alRecibir).toHaveBeenCalledWith({ entregado: 3 });
    await avanzar(SONDEO_CONTEOS_MS);
    expect(pedir).toHaveBeenCalledTimes(2);
  });

  it('AC3 — con la pestaña oculta no pide nada, y al volver pide de inmediato', async () => {
    const pedir = vi.fn().mockResolvedValue({});
    renderHook(() => useSondeoDeConteos({ pedir, alRecibir: vi.fn() }));

    act(() => cambiarVisibilidad('hidden'));
    await avanzar(SONDEO_CONTEOS_MS * 5);
    expect(pedir).not.toHaveBeenCalled();

    act(() => cambiarVisibilidad('visible'));
    await avanzar(0);
    expect(pedir).toHaveBeenCalledTimes(1);
    await avanzar(SONDEO_CONTEOS_MS);
    expect(pedir).toHaveBeenCalledTimes(2);
  });

  it('AC5 — un fallo conserva los conteos anteriores y se reintenta en el siguiente ciclo', async () => {
    const pedir = vi.fn().mockRejectedValueOnce(new Error('500')).mockResolvedValue({ borrador: 1 });
    const alRecibir = vi.fn();
    renderHook(() => useSondeoDeConteos({ pedir, alRecibir }));

    await avanzar(SONDEO_CONTEOS_MS);
    expect(alRecibir).not.toHaveBeenCalled();
    await avanzar(SONDEO_CONTEOS_MS);
    expect(alRecibir).toHaveBeenCalledWith({ borrador: 1 });
  });

  it('AC6 — una petición nueva cancela la anterior: nunca gana una respuesta vieja', async () => {
    const señales: AbortSignal[] = [];
    let resolverPrimera: (v: unknown) => void = () => {};
    const pedir = vi
      .fn()
      .mockImplementationOnce((signal: AbortSignal) => {
        señales.push(signal);
        return new Promise((r) => {
          resolverPrimera = r;
        });
      })
      .mockImplementation((signal: AbortSignal) => {
        señales.push(signal);
        return Promise.resolve('nueva');
      });
    const alRecibir = vi.fn();
    renderHook(() => useSondeoDeConteos({ pedir, alRecibir }));

    await avanzar(SONDEO_CONTEOS_MS); // primera, se queda colgada
    await avanzar(SONDEO_CONTEOS_MS); // segunda, cancela la primera
    await act(async () => {
      resolverPrimera('vieja');
    });

    expect(señales[0].aborted).toBe(true);
    expect(alRecibir).toHaveBeenCalledWith('nueva');
    expect(alRecibir).not.toHaveBeenCalledWith('vieja');
  });

  it('usa la última versión de la petición sin reiniciar el reloj', async () => {
    const primera = vi.fn().mockResolvedValue('filtros viejos');
    const segunda = vi.fn().mockResolvedValue('filtros nuevos');
    const alRecibir = vi.fn();
    const { rerender } = renderHook(({ pedir }) => useSondeoDeConteos({ pedir, alRecibir }), {
      initialProps: { pedir: primera },
    });

    await avanzar(SONDEO_CONTEOS_MS / 2);
    rerender({ pedir: segunda });
    await avanzar(SONDEO_CONTEOS_MS / 2);

    expect(primera).not.toHaveBeenCalled();
    expect(segunda).toHaveBeenCalledTimes(1);
  });

  it('apagado no pide nada', async () => {
    const pedir = vi.fn().mockResolvedValue({});
    renderHook(() => useSondeoDeConteos({ pedir, alRecibir: vi.fn(), activo: false }));

    await avanzar(SONDEO_CONTEOS_MS * 3);
    expect(pedir).not.toHaveBeenCalled();
  });
});

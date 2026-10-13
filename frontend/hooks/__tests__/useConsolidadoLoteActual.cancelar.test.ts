import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';

import {
  INTERVALO_POLLING_LOTE_MS,
  OCULTAR_CANCELADO_MS,
  RETENCION_LOTE_MS,
  VENTANA_CANDADO_MS,
  esEstadoTerminal,
  loteExpirado,
  useConsolidadoLoteActual,
} from '@/hooks/useConsolidadoLoteActual';
import {
  ConsolidadoLotesApiError,
  MENSAJE_NO_SE_PUDO_CANCELAR,
  consolidadoLotesClient,
} from '@/lib/api/consolidado-lotes-client';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

// Uso de ejemplo (HU #13388):
//   const s = useConsolidadoLoteActual({ habilitado });
//   <LoteDescargaAlertCard onCancelar={() => void s.cancelar()} cancelando={s.cancelando}
//     errorCancelacion={s.errorCancelacion} ... />   // s.oculto = true ~8 s después de terminadoEn

/** Datos sintéticos según `LoteConsolidados` del contrato. */
const BASE: LoteConsolidados = {
  id: 'lote-c',
  estado: 'en_proceso',
  tipoDocumento: 'consolidado',
  total: 1200,
  procesados: 300,
  incluidos: 300,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  partes: [],
};
const PARTES = [1, 2].map((numero) => ({
  numero,
  nombreArchivo: `consolidados_parte-0${numero}-de-02.zip`,
  pdfs: 600,
  omitidos: 0,
  bytes: 1024,
}));
const AHORA = new Date('2026-10-07T16:00:00Z').getTime();

/** El servidor cancela «ahora»: terminadoEn = expiraEn = instante de cancelación (contrato §4 / D4). */
function cancelado(lote: LoteConsolidados, finMs = Date.now()): LoteConsolidados {
  const fin = new Date(finMs).toISOString();
  return { ...lote, estado: 'cancelado', terminadoEn: fin, expiraEn: fin, partes: [] };
}

let servidor: { lote: LoteConsolidados | null };
let actual: ReturnType<typeof vi.spyOn>;
let porId: ReturnType<typeof vi.spyOn>;
let descargar: ReturnType<typeof vi.spyOn>;
let cancelar: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] });
  vi.setSystemTime(AHORA);
  vi.stubGlobal('BroadcastChannel', undefined);
  localStorage.clear();
  servidor = { lote: null };
  actual = vi.spyOn(consolidadoLotesClient, 'obtenerLoteActual').mockImplementation(async () => servidor.lote);
  porId = vi.spyOn(consolidadoLotesClient, 'obtenerLote').mockImplementation(async () => {
    if (!servidor.lote) throw new ConsolidadoLotesApiError(404, null);
    return servidor.lote;
  });
  descargar = vi.spyOn(consolidadoLotesClient, 'descargarParte').mockResolvedValue(undefined);
  // Mock del contrato §4: 202 con el lote cancelado (el backend #13385 aún no existe).
  cancelar = vi.spyOn(consolidadoLotesClient, 'cancelar').mockImplementation(async () => {
    servidor.lote = cancelado(servidor.lote!);
    return servidor.lote;
  });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

const avanzar = (ms: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
const montar = () => renderHook(() => useConsolidadoLoteActual({ habilitado: true }));
const llamadas = () => actual.mock.calls.length + porId.mock.calls.length;

describe('useConsolidadoLoteActual — cancelar (HU #13388)', () => {
  it('AC2 — cancelar() envía la cancelación del lote en curso y marca «cancelando» mientras tanto', async () => {
    servidor.lote = { ...BASE, id: 'ac2' };
    let soltar!: (l: LoteConsolidados) => void;
    cancelar.mockImplementationOnce(() => new Promise<LoteConsolidados>((r) => (soltar = r)));
    const { result } = montar();
    await avanzar(0);
    expect(result.current.cancelando).toBe(false);

    let promesa!: Promise<void>;
    act(() => {
      promesa = result.current.cancelar();
    });
    expect(cancelar).toHaveBeenCalledWith('ac2');
    expect(result.current.cancelando).toBe(true);

    // Un segundo clic mientras la petición está en curso no envía otra.
    act(() => void result.current.cancelar());
    expect(cancelar).toHaveBeenCalledTimes(1);

    await act(async () => {
      soltar(cancelado(servidor.lote!));
      await promesa;
    });
    expect(result.current.cancelando).toBe(false);
    expect(result.current.lote?.estado).toBe('cancelado');
  });

  it('AC3 — 202 cancelado: es terminal, se detiene el polling y no hay autodescarga', async () => {
    servidor.lote = { ...BASE, id: 'ac3' };
    const { result } = montar();
    await avanzar(0);
    await act(async () => {
      await result.current.cancelar();
    });
    expect(result.current.lote?.estado).toBe('cancelado');
    expect(result.current.expirado).toBe(false);
    expect(result.current.oculto).toBe(false);

    const antes = llamadas();
    await avanzar(INTERVALO_POLLING_LOTE_MS * 2 + VENTANA_CANDADO_MS);
    expect(llamadas()).toBe(antes);
    expect(descargar).not.toHaveBeenCalled();
    expect(esEstadoTerminal('cancelado')).toBe(true);
  });

  it('AC3 — el aviso se oculta solo a los 8 s contados desde terminadoEn (temporizador simulado)', async () => {
    servidor.lote = { ...BASE, id: 'ac3-8s' };
    const { result } = montar();
    await avanzar(0);
    await act(async () => {
      await result.current.cancelar();
    });
    expect(OCULTAR_CANCELADO_MS).toBe(8_000);
    await avanzar(OCULTAR_CANCELADO_MS - 1);
    expect(result.current.oculto).toBe(false);
    await avanzar(1);
    expect(result.current.oculto).toBe(true);
  });

  it('AC3 — el plazo se cuenta desde terminadoEn del servidor, no desde el clic', async () => {
    servidor.lote = { ...BASE, id: 'ac3-servidor' };
    // El servidor canceló 5 s antes de que la respuesta llegara a esta pestaña.
    cancelar.mockImplementationOnce(async () => cancelado(servidor.lote!, AHORA - 5_000));
    const { result } = montar();
    await avanzar(0);
    await act(async () => {
      await result.current.cancelar();
    });
    expect(result.current.oculto).toBe(false);
    await avanzar(3_000);
    expect(result.current.oculto).toBe(true);
  });

  it('AC3 — otra pestaña pasa a «Descarga cancelada» en el siguiente ciclo, sin autodescarga, y se oculta igual', async () => {
    servidor.lote = { ...BASE, id: 'ac3-otra' };
    const otra = montar();
    await avanzar(0);
    servidor.lote = cancelado(servidor.lote, AHORA + 1_000); // cancelada desde la primera pestaña
    await avanzar(INTERVALO_POLLING_LOTE_MS);
    expect(otra.result.current.lote?.estado).toBe('cancelado');
    expect(otra.result.current.oculto).toBe(false);
    const antes = llamadas();
    await avanzar(INTERVALO_POLLING_LOTE_MS); // AHORA + 8 s = terminadoEn + 7 s
    expect(llamadas()).toBe(antes);
    expect(otra.result.current.oculto).toBe(false);
    await avanzar(1_000);
    expect(otra.result.current.oculto).toBe(true);
    expect(descargar).not.toHaveBeenCalled();
  });

  it('AC4 — recarga con un lote cancelado hace más de 8 s: no se muestra aviso', async () => {
    servidor.lote = cancelado({ ...BASE, id: 'ac4' }, AHORA - OCULTAR_CANCELADO_MS - 1_000);
    const { result } = montar();
    await avanzar(0);
    expect(result.current.lote?.estado).toBe('cancelado');
    expect(result.current.oculto).toBe(true);
  });

  it('AC4 — recarga con un lote cancelado hace menos de 8 s: se ve hasta cumplirlos', async () => {
    servidor.lote = cancelado({ ...BASE, id: 'ac4-reciente' }, AHORA - 2_000);
    const { result } = montar();
    await avanzar(0);
    expect(result.current.oculto).toBe(false);
    await avanzar(6_000);
    expect(result.current.oculto).toBe(true);
  });

  it('AC5 — 409 lote_terminado: refresca el estado real y, si está completado, muestra sus partes', async () => {
    servidor.lote = { ...BASE, id: 'ac5' };
    cancelar.mockImplementationOnce(async () => {
      servidor.lote = {
        ...servidor.lote!,
        estado: 'completado',
        procesados: 1200,
        incluidos: 1200,
        terminadoEn: new Date(AHORA).toISOString(),
        expiraEn: new Date(AHORA + RETENCION_LOTE_MS).toISOString(),
        partes: PARTES,
      };
      throw new ConsolidadoLotesApiError(409, 'lote_terminado');
    });
    const { result } = montar();
    await avanzar(0);
    const antes = porId.mock.calls.length;
    await act(async () => {
      await result.current.cancelar();
    });
    await avanzar(0);
    expect(porId.mock.calls.length).toBeGreaterThan(antes);
    expect(result.current.lote?.estado).toBe('completado');
    expect(result.current.lote?.partes).toHaveLength(2);
    expect(result.current.errorCancelacion).toBeNull();
    expect(result.current.cancelando).toBe(false);
    expect(result.current.oculto).toBe(false);
  });

  it.each([404, 403])('AC6 — %i: «No se pudo cancelar la descarga» y refresca el estado del lote', async (status) => {
    servidor.lote = { ...BASE, id: `ac6-${status}` };
    cancelar.mockRejectedValueOnce(new ConsolidadoLotesApiError(status, null));
    const { result } = montar();
    await avanzar(0);
    const antes = porId.mock.calls.length;
    await act(async () => {
      await result.current.cancelar();
    });
    await avanzar(0);
    expect(result.current.errorCancelacion).toBe(MENSAJE_NO_SE_PUDO_CANCELAR);
    expect(porId.mock.calls.length).toBeGreaterThan(antes);
    expect(result.current.cancelando).toBe(false);
  });

  it('AC6 — falla de red: el botón se rehabilita, el lote sigue visible y el polling continúa', async () => {
    servidor.lote = { ...BASE, id: 'ac6-red' };
    cancelar.mockRejectedValueOnce(new ConsolidadoLotesApiError(0, null));
    const { result } = montar();
    await avanzar(0);
    await act(async () => {
      await result.current.cancelar();
    });
    expect(result.current.cancelando).toBe(false);
    expect(result.current.lote?.estado).toBe('en_proceso');
    expect(result.current.oculto).toBe(false);
    expect(result.current.errorCancelacion).toBeNull();
    const antes = llamadas();
    await avanzar(INTERVALO_POLLING_LOTE_MS);
    expect(llamadas()).toBe(antes + 1);
  });

  it('cancelar() sin lote no llama a la API', async () => {
    const { result } = montar();
    await avanzar(0);
    await act(async () => {
      await result.current.cancelar();
    });
    expect(cancelar).not.toHaveBeenCalled();
  });

  it('contrato — un lote cancelado no cuenta como expirado aunque expiraEn = terminadoEn', () => {
    const c = cancelado(BASE, AHORA - 1_000);
    expect(loteExpirado(c, AHORA)).toBe(false);
    expect(loteExpirado(c, AHORA + RETENCION_LOTE_MS * 2)).toBe(false);
  });
});

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';

import {
  CANAL_LOTES_CONSOLIDADOS,
  INTERVALO_POLLING_LOTE_MS,
  RETENCION_LOTE_MS,
  VENTANA_CANDADO_MS,
  claveCandadoAutodescarga,
  esEstadoTerminal,
  loteExpirado,
  useConsolidadoLoteActual,
} from '@/hooks/useConsolidadoLoteActual';
import {
  ConsolidadoLotesApiError,
  MENSAJE_REINTENTAR_DESCARGA,
  consolidadoLotesClient,
} from '@/lib/api/consolidado-lotes-client';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

// Uso de ejemplo:
//   const seguimiento = useConsolidadoLoteActual({ habilitado: puedeDescargaMasiva });
//   seguimiento.lote            // lote activo o último terminal retenido (null si 204)
//   seguimiento.mostrarLote(id) // 409 lote_activo / bandeja OT: sigue ese lote concreto
//   seguimiento.descargarParte(2)

/** Datos sintéticos según `LoteConsolidados` del contrato §5. */
const BASE: LoteConsolidados = {
  id: 'lote-base',
  estado: 'en_proceso',
  tipoDocumento: 'consolidado',
  total: 1200,
  procesados: 300,
  incluidos: 300,
  omitidos: 0,
  generados: 12,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  nombreBase: 'consolidados_20261007_1000',
  partes: [],
};
const PARTES = [1, 2, 3].map((numero) => ({
  numero,
  nombreArchivo: `consolidados_20261007_1000_parte-0${numero}-de-03.zip`,
  pdfs: 400,
  omitidos: 0,
  bytes: 1024,
}));
const AHORA = new Date('2026-10-07T16:00:00Z').getTime();

function terminado(lote: LoteConsolidados, over: Partial<LoteConsolidados> = {}): LoteConsolidados {
  const fin = new Date(AHORA).toISOString();
  return {
    ...lote,
    estado: 'completado',
    procesados: lote.total,
    incluidos: lote.total,
    terminadoEn: fin,
    expiraEn: new Date(AHORA + RETENCION_LOTE_MS).toISOString(),
    partes: PARTES,
    ...over,
  };
}

/** BroadcastChannel en memoria: entrega a las demás instancias del mismo nombre (otras «pestañas»). */
class CanalFalso {
  static canales = new Map<string, Set<CanalFalso>>();
  private oyentes = new Set<(e: MessageEvent) => void>();
  constructor(public readonly name: string) {
    const set = CanalFalso.canales.get(name) ?? new Set<CanalFalso>();
    set.add(this);
    CanalFalso.canales.set(name, set);
  }
  postMessage(data: unknown) {
    for (const otro of CanalFalso.canales.get(this.name) ?? []) {
      if (otro === this) continue;
      queueMicrotask(() => otro.oyentes.forEach((l) => l({ data } as MessageEvent)));
    }
  }
  addEventListener(_t: string, l: (e: MessageEvent) => void) {
    this.oyentes.add(l);
  }
  removeEventListener(_t: string, l: (e: MessageEvent) => void) {
    this.oyentes.delete(l);
  }
  close() {
    CanalFalso.canales.get(this.name)?.delete(this);
  }
}

let servidor: { lote: LoteConsolidados | null; falla: boolean };
let actual: ReturnType<typeof vi.spyOn>;
let porId: ReturnType<typeof vi.spyOn>;
let descargar: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] });
  vi.setSystemTime(AHORA);
  CanalFalso.canales.clear();
  vi.stubGlobal('BroadcastChannel', CanalFalso);
  localStorage.clear();
  servidor = { lote: null, falla: false };
  actual = vi.spyOn(consolidadoLotesClient, 'obtenerLoteActual').mockImplementation(async () => {
    if (servidor.falla) throw new ConsolidadoLotesApiError(0, null);
    return servidor.lote;
  });
  porId = vi.spyOn(consolidadoLotesClient, 'obtenerLote').mockImplementation(async () => {
    if (servidor.falla) throw new ConsolidadoLotesApiError(0, null);
    if (!servidor.lote) throw new ConsolidadoLotesApiError(404, null);
    return servidor.lote;
  });
  descargar = vi.spyOn(consolidadoLotesClient, 'descargarParte').mockResolvedValue(undefined);
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
const montar = (habilitado = true) => renderHook(() => useConsolidadoLoteActual({ habilitado }));

describe('useConsolidadoLoteActual — HU #13382', () => {
  it('AC1 — consulta el lote al montar y lo actualiza cada 4 s con el total fijo', async () => {
    servidor.lote = { ...BASE, id: 'ac1', estado: 'en_cola', procesados: 0, incluidos: 0 };
    const { result } = montar();
    await avanzar(0);
    expect(result.current.lote?.estado).toBe('en_cola');
    expect(actual).toHaveBeenCalledTimes(1);

    servidor.lote = { ...servidor.lote, estado: 'en_proceso', procesados: 450, incluidos: 450 };
    await avanzar(INTERVALO_POLLING_LOTE_MS - 1);
    expect(actual).toHaveBeenCalledTimes(1);
    await avanzar(1);
    expect(actual).toHaveBeenCalledTimes(2);
    expect(result.current.lote?.procesados).toBe(450);
    expect(result.current.lote?.total).toBe(1200);
  });

  it('AC5 — 204: no hay lote y no se sigue consultando', async () => {
    const { result } = montar();
    await avanzar(0);
    expect(result.current.lote).toBeNull();
    await avanzar(INTERVALO_POLLING_LOTE_MS * 3);
    expect(actual).toHaveBeenCalledTimes(1);
  });

  it('AC5 — error de red: conserva el último estado, marca reintento y se recupera', async () => {
    servidor.lote = { ...BASE, id: 'ac5-red' };
    const { result } = montar();
    await avanzar(0);
    servidor.falla = true;
    await avanzar(INTERVALO_POLLING_LOTE_MS);
    expect(result.current.lote?.procesados).toBe(300);
    expect(result.current.errorConsulta).toBe(true);

    servidor.falla = false;
    servidor.lote = { ...BASE, id: 'ac5-red', procesados: 600, incluidos: 600 };
    await avanzar(INTERVALO_POLLING_LOTE_MS);
    expect(result.current.errorConsulta).toBe(false);
    expect(result.current.lote?.procesados).toBe(600);
  });

  it('AC5 — error de red en la primera consulta: reintenta sin lote que mostrar', async () => {
    servidor.falla = true;
    const { result } = montar();
    await avanzar(0);
    expect(result.current.lote).toBeNull();
    await avanzar(INTERVALO_POLLING_LOTE_MS);
    expect(actual).toHaveBeenCalledTimes(2);
  });

  it('AC5 — el polling se detiene cuando el lote es terminal', async () => {
    servidor.lote = { ...BASE, id: 'ac5-fin' };
    montar();
    await avanzar(0);
    servidor.lote = terminado(servidor.lote);
    await avanzar(INTERVALO_POLLING_LOTE_MS);
    const llamadas = actual.mock.calls.length;
    await avanzar(INTERVALO_POLLING_LOTE_MS * 5);
    expect(actual).toHaveBeenCalledTimes(llamadas);
  });

  it('sin permiso (habilitado=false) no consulta', async () => {
    montar(false);
    await avanzar(INTERVALO_POLLING_LOTE_MS * 2);
    expect(actual).not.toHaveBeenCalled();
  });

  it('AC3 — dos pestañas: al terminar con 3 partes solo la parte 1 se descarga, una vez', async () => {
    servidor.lote = { ...BASE, id: 'ac3-dos' };
    const a = montar();
    const b = montar();
    await avanzar(0);
    servidor.lote = terminado(servidor.lote);
    await avanzar(INTERVALO_POLLING_LOTE_MS + VENTANA_CANDADO_MS + 10);

    expect(descargar).toHaveBeenCalledTimes(1);
    expect(descargar).toHaveBeenCalledWith('ac3-dos', expect.objectContaining({ numero: 1 }));
    expect(a.result.current.lote?.partes).toHaveLength(3);
    expect(b.result.current.lote?.partes).toHaveLength(3);
    expect(localStorage.getItem(claveCandadoAutodescarga('ac3-dos'))).not.toBeNull();

    // Una tercera «pestaña» (o recarga) no vuelve a descargar.
    montar();
    await avanzar(VENTANA_CANDADO_MS * 2);
    expect(descargar).toHaveBeenCalledTimes(1);
  });

  it('AC3 — sin localStorage (lanza) el canal sigue garantizando una sola descarga', async () => {
    vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    vi.spyOn(localStorage, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    servidor.lote = { ...BASE, id: 'ac3-sin-storage' };
    montar();
    montar();
    await avanzar(0);
    servidor.lote = terminado(servidor.lote);
    await avanzar(INTERVALO_POLLING_LOTE_MS + VENTANA_CANDADO_MS + 10);
    expect(descargar).toHaveBeenCalledTimes(1);
  });

  it('AC3 — sin BroadcastChannel también descarga una sola vez (candado de localStorage)', async () => {
    vi.stubGlobal('BroadcastChannel', undefined);
    servidor.lote = { ...BASE, id: 'ac3-sin-canal' };
    montar();
    montar();
    await avanzar(0);
    servidor.lote = terminado(servidor.lote);
    await avanzar(INTERVALO_POLLING_LOTE_MS + VENTANA_CANDADO_MS + 10);
    expect(descargar).toHaveBeenCalledTimes(1);
  });

  it('AC3 — un lote fallido no se autodescarga', async () => {
    servidor.lote = { ...BASE, id: 'ac3-fallido' };
    const { result } = montar();
    await avanzar(0);
    servidor.lote = terminado(servidor.lote, { estado: 'fallido', partes: [] });
    await avanzar(INTERVALO_POLLING_LOTE_MS + VENTANA_CANDADO_MS + 10);
    expect(result.current.lote?.estado).toBe('fallido');
    expect(descargar).not.toHaveBeenCalled();
  });

  it('AC4 — recarga con lote terminado hace menos de 24 h: sin autodescarga y sin expirar', async () => {
    servidor.lote = terminado({ ...BASE, id: 'ac4-recarga' }, {
      terminadoEn: new Date(AHORA - 3_600_000).toISOString(),
      expiraEn: new Date(AHORA - 3_600_000 + RETENCION_LOTE_MS).toISOString(),
    });
    const { result } = montar();
    await avanzar(VENTANA_CANDADO_MS * 2);
    expect(result.current.lote?.partes).toHaveLength(3);
    expect(result.current.expirado).toBe(false);
    expect(descargar).not.toHaveBeenCalled();
  });

  it('AC4 — pasadas 24 h desde el fin queda expirado (aunque el servidor aún diga completado)', async () => {
    servidor.lote = terminado({ ...BASE, id: 'ac4-viejo' }, {
      terminadoEn: new Date(AHORA - RETENCION_LOTE_MS - 1000).toISOString(),
      expiraEn: new Date(AHORA - 1000).toISOString(),
    });
    const { result } = montar();
    await avanzar(0);
    expect(result.current.expirado).toBe(true);
  });

  it('AC4 — con la app abierta, al cumplirse las 24 h pasa a expirado', async () => {
    servidor.lote = terminado({ ...BASE, id: 'ac4-reloj' }, {
      expiraEn: new Date(AHORA + 60_000).toISOString(),
    });
    const { result } = montar();
    await avanzar(0);
    expect(result.current.expirado).toBe(false);
    await avanzar(60_001);
    expect(result.current.expirado).toBe(true);
  });

  it('mostrarLote(id) — sigue ese lote por id (409 lote_activo / bandeja OT)', async () => {
    const { result } = montar();
    await avanzar(0);
    expect(result.current.lote).toBeNull();
    servidor.lote = { ...BASE, id: 'fijado' };
    act(() => result.current.mostrarLote('fijado'));
    await avanzar(0);
    expect(porId).toHaveBeenCalledWith('fijado');
    expect(result.current.lote?.id).toBe('fijado');
    await avanzar(INTERVALO_POLLING_LOTE_MS);
    expect(porId).toHaveBeenCalledTimes(2);
  });

  it('mostrarLote(lote) — pinta al instante el lote recién creado y lo sigue', async () => {
    const { result } = montar();
    await avanzar(0);
    const creado = { ...BASE, id: 'creado', estado: 'en_cola' as const, procesados: 0, incluidos: 0 };
    servidor.lote = creado;
    act(() => result.current.mostrarLote(creado));
    expect(result.current.lote?.id).toBe('creado');
  });

  it('descargarParte — 410 marca expirado; otro error deja el mensaje de reintento', async () => {
    servidor.lote = terminado({ ...BASE, id: 'manual' });
    const { result } = montar();
    await avanzar(0);

    descargar.mockRejectedValueOnce(new ConsolidadoLotesApiError(503, 'auditoria_no_registrada'));
    await act(async () => {
      await result.current.descargarParte(2);
    });
    expect(descargar).toHaveBeenLastCalledWith('manual', expect.objectContaining({ numero: 2 }));
    expect(result.current.errorDescarga).toBe(MENSAJE_REINTENTAR_DESCARGA);

    descargar.mockRejectedValueOnce(new ConsolidadoLotesApiError(410, 'descarga_expirada'));
    await act(async () => {
      await result.current.descargarParte(3);
    });
    expect(result.current.expirado).toBe(true);
  });

  it('contrato — utilidades de estado y canal', () => {
    expect(CANAL_LOTES_CONSOLIDADOS).toBeTruthy();
    expect(esEstadoTerminal('en_cola')).toBe(false);
    expect(esEstadoTerminal('empaquetando')).toBe(false);
    expect(esEstadoTerminal('completado_con_omitidos')).toBe(true);
    expect(esEstadoTerminal('cancelado')).toBe(true);
    expect(loteExpirado({ ...BASE, estado: 'expirado' }, AHORA)).toBe(true);
    expect(loteExpirado(BASE, AHORA + RETENCION_LOTE_MS * 2)).toBe(false);
    // Sin expiraEn, se calcula desde terminadoEn + 24 h.
    expect(
      loteExpirado(
        { ...BASE, estado: 'completado', terminadoEn: new Date(AHORA - RETENCION_LOTE_MS).toISOString() },
        AHORA,
      ),
    ).toBe(true);
  });
});

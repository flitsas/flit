import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { tramitesClient } from '@/lib/api/tramites-client';

/**
 * HU #10885 (Feature #10862, CF-04) — `runConsultation` (POST
 * /instances/{id}/consultations/{templateCode}):
 *  - AC1: mapea `fromCache`/`queriedAt` del `ConsultationResult` al `PreflightSnapshot` devuelto.
 *  - AC2: `forceRefresh=true` agrega `?forceRefresh=true` a la URL (salta el reúso de caché,
 *    ADR-0030); sin el flag (default), la URL no lo lleva (cero regresión).
 */
function lastCallUrl() {
  const mock = (globalThis.fetch as ReturnType<typeof vi.fn>).mock;
  const [url] = mock.calls[mock.calls.length - 1];
  return String(url);
}

describe('tramitesClient.runConsultation — AC1/AC2 (HU #10885)', () => {
  beforeEach(() => {
    const json = (body: unknown) =>
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        json({
          provider: 'verifik',
          overall: 'green',
          checks: [],
          hydratedFields: [],
          fromCache: true,
          queriedAt: '2026-07-20T08:30:00Z',
        }),
      ),
    );
  });

  afterEach(() => vi.restoreAllMocks());

  it('AC1 — mapea fromCache/queriedAt del backend al PreflightSnapshot devuelto', async () => {
    const snapshot = await tramitesClient.runConsultation('inst-1', 'RUNT_VEHICLE');
    expect(snapshot.fromCache).toBe(true);
    expect(snapshot.queriedAt).toBe('2026-07-20T08:30:00Z');
    expect(lastCallUrl()).not.toContain('forceRefresh');
  });

  it('AC2 — forceRefresh=true agrega el query param a la URL de consulta', async () => {
    await tramitesClient.runConsultation('inst-1', 'RUNT_VEHICLE', undefined, true);
    expect(lastCallUrl()).toContain(
      '/api/v1/tramites/instances/inst-1/consultations/RUNT_VEHICLE?forceRefresh=true',
    );
  });

  it('sin forceRefresh (default) no manda el query param', async () => {
    await tramitesClient.runConsultation('inst-1', 'RUNT_VEHICLE');
    expect(lastCallUrl()).toBe(
      'http://localhost:3000/api/v1/tramites/instances/inst-1/consultations/RUNT_VEHICLE',
    );
  });

  /**
   * El organismo de tránsito se elige DESPUÉS de consultar, así que el paso 1 lo pasa como cadena
   * vacía mientras no hay elección. El backend lo declara `Guid?`: un `""` no lo puede leer el
   * binder y responde 400 con cuerpo vacío ANTES de entrar al handler — en pantalla se veía como
   * "No se pudo completar la solicitud. Revisa los datos e inténtalo de nuevo." y la consulta
   * quedaba rota siempre en matrícula. Ausente tiene que viajar como null.
   */
  it('el organismo sin elegir viaja como null, no como cadena vacía', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify({ previewToken: 'tok', overall: 'green', checks: [], vehicleFields: [] }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        ),
      ),
    );

    await tramitesClient.runPreflightPreview({
      modalidad: 'matricula_inicial',
      vin: '9BWZZZ377VT004251',
      transitOfficeId: '',
    });

    const mock = (globalThis.fetch as ReturnType<typeof vi.fn>).mock;
    const [, init] = mock.calls[mock.calls.length - 1] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toMatchObject({ transitOfficeId: null });
  });

  it('respuesta sin fromCache/queriedAt (MISS) degrada a fromCache=false, queriedAt=null', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify({ provider: 'verifik', overall: 'green', checks: [], hydratedFields: [] }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        ),
      ),
    );
    const snapshot = await tramitesClient.runConsultation('inst-1', 'RUNT_VEHICLE');
    expect(snapshot.fromCache).toBe(false);
    expect(snapshot.queriedAt).toBeNull();
  });
});

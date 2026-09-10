/**
 * `runConsultation` mapea el resultado del servidor campo por campo, así que todo lo que el backend
 * añada al check se pierde en silencio si no se agrega también aquí.
 *
 * <p>Le pasó a `datos` —el respaldo del proveedor: vencimiento del SOAT, póliza, aseguradora, CDA—:
 * el servidor lo mandaba, el panel sabía pintarlo, y no aparecía nunca porque este mapeo lo dejaba
 * fuera. Y a `details`, con él, el listado de comparendos bajo la advertencia de multas. Ninguno de
 * los dos daba error: TypeScript no se queja de un campo que el objeto destino no declara.</p>
 */
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

const fetchMock = vi.fn();

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function respuesta(cuerpo: unknown) {
  return {
    ok: true,
    status: 200,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => cuerpo,
    text: async () => JSON.stringify(cuerpo),
  } as unknown as Response;
}

describe('tramitesClient.runConsultation', () => {
  it('conserva los datos del proveedor y el detalle de comparendos', async () => {
    const { tramitesClient } = await import('@/lib/api/tramites-client');

    fetchMock.mockResolvedValue(
      respuesta({
        provider: 'kyverum_runt',
        overall: 'green',
        hydratedFields: [],
        fromCache: false,
        queriedAt: '2026-08-31T20:42:00Z',
        checks: [
          {
            key: 'soat',
            label: 'SOAT',
            status: 'ok',
            source: 'kyverum_runt',
            message: null,
            details: null,
            datos: [
              { etiqueta: 'Vigente hasta', valor: '2027/01/23' },
              { etiqueta: 'Póliza', valor: '3506349600' },
            ],
          },
          {
            key: 'multas',
            label: 'Multas SIMIT',
            status: 'warn',
            source: 'kyverum_fines',
            message: '1 comparendo pendiente',
            details: [{ numero: 'C-1', valor: 500000 }],
            datos: null,
          },
        ],
      }),
    );

    const snapshot = await tramitesClient.runConsultation('inst-1', 'vehiculo');

    expect(snapshot.checks[0].datos).toHaveLength(2);
    expect(snapshot.checks[0].datos?.[0]).toEqual({
      etiqueta: 'Vigente hasta',
      valor: '2027/01/23',
    });
    expect(snapshot.checks[1].details).toHaveLength(1);
  });
});

/**
 * HU #12787 — contrato del cliente de la ruta de entrega OT del consolidado (HU #12785):
 * `GET /api/v1/admin/ot/client-procedures/{id}/consolidado/entrega` con `transitOfficeId` (SuperAdmin),
 * `tipo`, `force` y `soloLectura` solo cuando se piden.
 *
 * Uso de ejemplo:
 *   await entregarOtConsolidado('proc-1', { transitOfficeId }, { tipo: 'consolidado_maestro' });
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';

const apiFetch = vi.hoisted(() => vi.fn());
vi.mock('@/lib/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api/client')>();
  return { ...actual, apiFetch };
});

import { entregarOtConsolidado, generarOtConsolidadoMaestro } from '@/lib/api/admin-ot';

beforeEach(() => {
  apiFetch.mockReset();
  apiFetch.mockResolvedValue({
    document: { attachmentId: 'a', tipo: 'consolidado_maestro', filename: 'm.pdf', sha256: 's' },
    regenerado: false,
    definitivoPorEstadoFinal: true,
    modo: 'definitivo_estado_final',
  });
});

describe('entregarOtConsolidado — contrato', () => {
  it('GET a la ruta de entrega OT sin query por defecto', async () => {
    const res = await entregarOtConsolidado('proc-1');

    expect(apiFetch).toHaveBeenCalledWith(
      '/api/v1/admin/ot/client-procedures/proc-1/consolidado/entrega',
      { query: {} },
    );
    expect(res.definitivoPorEstadoFinal).toBe(true);
    expect(res.modo).toBe('definitivo_estado_final');
  });

  it('manda transitOfficeId del scope y solo los parámetros pedidos', async () => {
    await entregarOtConsolidado(
      'proc-1',
      { transitOfficeId: 'ot-1' },
      { tipo: 'consolidado_maestro', soloLectura: true },
    );

    expect(apiFetch).toHaveBeenCalledWith(
      '/api/v1/admin/ot/client-procedures/proc-1/consolidado/entrega',
      { query: { transitOfficeId: 'ot-1', tipo: 'consolidado_maestro', soloLectura: true } },
    );
  });

  it('force/soloLectura en false no viajan', async () => {
    await entregarOtConsolidado('proc-1', undefined, { force: false, soloLectura: false });

    expect(apiFetch.mock.calls[0][1]).toEqual({ query: {} });
  });

  it('generarOtConsolidadoMaestro sigue siendo el POST de generación (no la entrega)', async () => {
    await generarOtConsolidadoMaestro('proc-1', undefined, true);

    expect(apiFetch).toHaveBeenCalledWith(
      '/api/v1/admin/ot/client-procedures/proc-1/consolidado-maestro',
      { method: 'POST', query: { force: true } },
    );
  });
});

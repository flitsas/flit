import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { consolidadoLotesClient } from '@/lib/api/consolidado-lotes-client';
import { setActiveTramitesTenant, tenantHeader } from '@/lib/api/tramites-client';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';
import { TOKEN_STORAGE_KEY } from '@/lib/auth/jwt';

// Uso de ejemplo (Super Admin en /tramites, listado pedido SIN X-Tenant-Id):
//   await consolidadoLotesClient.crearLote({
//     seleccion: lote.modelo,
//     tipoDocumento: 'consolidado_maestro',
//     cabecerasDelListado: {},            // = searchPayload sin filterTenantId → sin X-Tenant-Id
//   });
// Gestor (sin cambios): consolidadoLotesClient.crearLote({ seleccion }) → mismas cabeceras de siempre.

/** Datos sintéticos (sin PII), forma `LoteConsolidados` del contrato. */
const LOTE: LoteConsolidados = {
  id: '0f8c6a1e-0000-4000-8000-000000000077',
  estado: 'en_cola',
  tipoDocumento: 'consolidado_maestro',
  total: 118,
  procesados: 0,
  incluidos: 0,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  partes: [],
};
const SELECCION: ModeloSeleccionLote<Record<string, unknown>> = {
  modo: 'filtro',
  ids: [],
  excluidos: ['inst-0002', 'inst-0005'],
  filtro: { condiciones: [{ field: 'compania', operator: 'in', value: ['tenant-b'] }] },
};
const TENANT_INTERNO_SA = '22222222-2222-2222-2222-222222222222';
const TENANT_ACTIVO = '33333333-3333-3333-3333-333333333333';
const TENANT_GESTOR = '44444444-4444-4444-4444-444444444444';

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${header}.${body}.`;
}
const respuesta = (status: number, body?: unknown) =>
  new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });

describe('consolidadoLotesClient.crearLote — cabeceras y tipo (HU #13387)', () => {
  const fetchMock = vi.fn();
  const cabeceras = () => (fetchMock.mock.calls[0][1] as RequestInit).headers as Record<string, string>;
  const cuerpo = () => JSON.parse(String((fetchMock.mock.calls[0][1] as RequestInit).body));

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue(respuesta(202, LOTE));
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    setActiveTramitesTenant(undefined);
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it('AC2 — Super Admin con listado sin X-Tenant-Id: la creación tampoco lo lleva (ni el del JWT ni el activo)', async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: 'sa', role: 'SuperAdmin', tenant_id: TENANT_INTERNO_SA }),
    );
    setActiveTramitesTenant(TENANT_ACTIVO);
    await consolidadoLotesClient.crearLote({ seleccion: SELECCION, cabecerasDelListado: {} });
    const h = cabeceras();
    expect(Object.keys(h).map((k) => k.toLowerCase())).not.toContain('x-tenant-id');
    expect(h.Authorization).toMatch(/^Bearer /);
    expect(h['Content-Type']).toBe('application/json');
  });

  it('AC2 (contrato) — si el listado sí llevaba filterTenantId, la creación lleva ese mismo X-Tenant-Id', async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: 'sa', role: 'SuperAdmin', tenant_id: TENANT_INTERNO_SA }),
    );
    setActiveTramitesTenant(TENANT_ACTIVO);
    await consolidadoLotesClient.crearLote({
      seleccion: SELECCION,
      cabecerasDelListado: { filterTenantId: 'tenant-b' },
    });
    expect(cabeceras()['X-Tenant-Id']).toBe('tenant-b');
  });

  it('AC2 (Gestor sin cambios) — sin cabecerasDelListado conserva exactamente las cabeceras previas', async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: 'g', role: 'Gestor', tenant_id: TENANT_GESTOR, permissions: ['consolidado-masivo.download'] }),
    );
    await consolidadoLotesClient.crearLote({ seleccion: SELECCION });
    expect(cabeceras()).toEqual({
      ...(tenantHeader() as Record<string, string>),
      'Content-Type': 'application/json',
    });
    expect(cabeceras()['X-Tenant-Id']).toBe(TENANT_GESTOR);
    expect(cuerpo().tipoDocumento).toBe('consolidado');
  });

  it('AC4 — el cuerpo lleva el tipoDocumento elegido y la selección tal cual (condición compañía incluida)', async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: 'sa', role: 'SuperAdmin' }));
    const lote = await consolidadoLotesClient.crearLote({
      seleccion: SELECCION,
      tipoDocumento: 'consolidado_maestro',
      cabecerasDelListado: {},
    });
    expect(cuerpo()).toEqual({ tipoDocumento: 'consolidado_maestro', confirmaEfectos: true, seleccion: SELECCION });
    expect(lote.total).toBe(118);
  });
});

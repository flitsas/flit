import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  tramitesClient,
  setActiveTramitesTenant,
} from '@/lib/api/tramites-client';

/**
 * #1 — Resolución de tenant + auth en el cliente de trámites:
 *  - El listado NO manda X-Tenant-Id salvo que el SuperAdmin filtre por compañía (ve todo por defecto).
 *  - Las llamadas per-instance llevan Bearer del JWT y X-Tenant-Id resuelto (override activo o tenant
 *    del token), para que el SuperAdmin pueda operar el trámite de otra compañía.
 */
const COMPANY = '22222222-2222-2222-2222-222222222222';
const OTHER = '33333333-3333-3333-3333-333333333333';

/** JWT sin firma con el payload dado (decodeJwtPayload solo lee el claim del medio). */
function makeToken(payload: Record<string, unknown>): string {
  const b64 = (o: unknown) =>
    btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64(payload)}.`;
}

function setCookieToken(token: string) {
  document.cookie = `flit_token=${token}; path=/`;
}

function lastCall() {
  const mock = (globalThis.fetch as ReturnType<typeof vi.fn>).mock;
  const [, init] = mock.calls[mock.calls.length - 1];
  return { headers: (init?.headers ?? {}) as Record<string, string> };
}

describe('tramitesClient — tenant/auth resolution (#1)', () => {
  beforeEach(() => {
    document.cookie = 'flit_token=; path=/; Max-Age=0';
    setActiveTramitesTenant(undefined);
    const json = (body: unknown) =>
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    vi.stubGlobal('fetch', vi.fn(async () => json({ items: [], id: 'x' })));
  });

  afterEach(() => vi.restoreAllMocks());

  it('listInstances no manda X-Tenant-Id por defecto (superadmin ve todo)', async () => {
    await tramitesClient.listInstances();
    expect(lastCall().headers['X-Tenant-Id']).toBeUndefined();
  });

  it('listInstances manda X-Tenant-Id solo cuando se filtra por compañía', async () => {
    await tramitesClient.listInstances(OTHER);
    expect(lastCall().headers['X-Tenant-Id']).toBe(OTHER);
  });

  it('llamada per-instance lleva Bearer y el tenant del JWT (company-user)', async () => {
    const token = makeToken({ sub: 'u1', role: 'AdminCompany', tenant_id: COMPANY });
    setCookieToken(token);

    await tramitesClient.getInstance('inst-1');

    const { headers } = lastCall();
    expect(headers.Authorization).toBe(`Bearer ${token}`);
    expect(headers['X-Tenant-Id']).toBe(COMPANY);
  });

  it('el tenant activo (superadmin abriendo otra compañía) tiene prioridad', async () => {
    setCookieToken(makeToken({ sub: 'admin', role: 'SuperAdmin', tenant_id: COMPANY }));
    setActiveTramitesTenant(OTHER);

    await tramitesClient.getInstance('inst-1');

    expect(lastCall().headers['X-Tenant-Id']).toBe(OTHER);
  });

  // Regresión: las "atascadas" mandaban DEV_TENANT_ID hardcodeado → leían la cola de OTRA compañía.
  // Ahora resuelven el tenant del JWT como el resto del runtime.
  it('listStuckIdentityValidations manda el X-Tenant-Id del JWT (no DEV_TENANT_ID)', async () => {
    setCookieToken(makeToken({ sub: 'u1', role: 'AdminCompany', tenant_id: COMPANY }));

    await tramitesClient.listStuckIdentityValidations();

    expect(lastCall().headers['X-Tenant-Id']).toBe(COMPANY);
  });

  it('requeueAllStuckIdentityValidations resuelve el tenant del token, no un default fijo', async () => {
    setCookieToken(makeToken({ sub: 'admin', role: 'SuperAdmin', tenant_id: COMPANY }));
    setActiveTramitesTenant(OTHER);

    await tramitesClient.requeueAllStuckIdentityValidations();

    // El override activo (superadmin acotando a otra compañía) tiene prioridad, igual que per-instance.
    expect(lastCall().headers['X-Tenant-Id']).toBe(OTHER);
  });
});

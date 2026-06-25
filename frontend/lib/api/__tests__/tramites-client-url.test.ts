import { afterEach, describe, expect, it, vi } from 'vitest';

/**
 * Regresión: el cliente de trámites/superadmin construye las URLs con `new URL(path, BASE_URL)`
 * (helper `apiUrl`), NO por concatenación `${BASE_URL}${path}`. El CD inyecta
 * NEXT_PUBLIC_API_BASE_URL con sufijo `/api/v1`; la concatenación duplicaba el prefijo
 * (`…/api/v1/api/v1/…`) y devolvía 404 en subida/descarga/portal/biométrica. Este test
 * fija la invariante: el path absoluto toma solo el ORIGEN del base, traiga o no el sufijo.
 */
async function loadApiUrl(base: string) {
  vi.resetModules();
  vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', base);
  const mod = await import('../tramites-client');
  return mod.apiUrl;
}

describe('tramites-client · apiUrl', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  const PATH = '/api/v1/tramites/instances/abc/attachments';
  const EXPECTED = 'https://api.example.com/api/v1/tramites/instances/abc/attachments';

  it('con BASE_URL con sufijo /api/v1 NO duplica el prefijo', async () => {
    const apiUrl = await loadApiUrl('https://api.example.com/api/v1');
    const url = apiUrl(PATH);
    expect(url).toBe(EXPECTED);
    expect(url).not.toContain('/api/v1/api/v1');
  });

  it('con BASE_URL sin sufijo (solo origen) resuelve igual', async () => {
    const apiUrl = await loadApiUrl('https://api.example.com');
    expect(apiUrl(PATH)).toBe(EXPECTED);
  });

  it('sin variables de entorno usa el origen local (proxy Next → core-api)', async () => {
    vi.resetModules();
    vi.unstubAllEnvs();
    const mod = await import('../tramites-client');
    expect(mod.apiUrl(PATH)).toBe(`http://localhost:3000${PATH}`);
  });
});

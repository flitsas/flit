/** Bug #14603 — el enlace al trámite lleva `?t=` solo para el Super Admin. */
import { describe, expect, it } from 'vitest';
import { hrefTramite } from '@/lib/tramites/href-tramite';

describe('hrefTramite', () => {
  it('admin con tenant: agrega ?t= codificado', () => {
    expect(hrefTramite('inst-1', 'ten a/b', true)).toBe('/tramites/inst-1?t=ten%20a%2Fb');
  });
  it('usuario de compañía: sin ?t=', () => {
    expect(hrefTramite('inst-1', 'ten-1', false)).toBe('/tramites/inst-1');
  });
  it.each([[undefined], [null], ['']])('admin sin tenant (%s): sin ?t=', (t) => {
    expect(hrefTramite('inst-1', t, true)).toBe('/tramites/inst-1');
  });
});

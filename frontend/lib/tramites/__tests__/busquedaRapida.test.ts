import { describe, expect, it } from 'vitest';
import { ATAJOS_GESTOR, atajoGestor } from '../busquedaRapida';
import { FILTRO_RECHAZADO_PREASIGNACION } from '../estados';

// Epic #12686 — HU #12806.
describe('ATAJOS_GESTOR', () => {
  it('AC1 — los atajos de la épica, sin conteo, con «Mis trámites» tras «Sin firmas»', () => {
    expect(ATAJOS_GESTOR.map((a) => a.label)).toEqual([
      'En subsanación',
      'Rechazado desde preasignación',
      'Más de 5 días en gestión',
      'Más de 10 días en gestión',
      'Trámites sin firmas',
      'Mis trámites',
      'Faltantes por aprobar',
      'Sin documento',
      'Trámites pausados',
    ]);
  });

  it('AC4 — los atajos de un solo estado fijan ese estado (su tarjeta se resalta)', () => {
    expect(atajoGestor('mas_de_5_dias')?.estado).toBe('entregado');
    expect(atajoGestor('faltantes_por_aprobar')?.estado).toBe('entregado');
    expect(atajoGestor('sin_documento')?.estado).toBe('borrador');
    expect(atajoGestor('rechazado_preasignacion')?.estado).toBe(FILTRO_RECHAZADO_PREASIGNACION);
  });

  it('cada atajo se traduce a lo que el servidor ya entiende', () => {
    expect(atajoGestor('en_subsanacion')?.condiciones).toEqual([
      { fieldId: 'en_subsanacion', operator: 'es_alguno', values: ['true'] },
    ]);
    for (const key of ['mas_de_5_dias', 'mas_de_10_dias', 'sin_firmas', 'sin_documento', 'pausados', 'mis_tramites'] as const) {
      expect(atajoGestor(key)?.busquedaRapida).toBe(key);
    }
    expect(atajoGestor('faltantes_por_aprobar')?.busquedaRapida).toBeUndefined();
  });

  it('AC6 — ningún texto menciona ICT', () => {
    for (const a of ATAJOS_GESTOR) {
      expect(`${a.label} ${a.hint}`).not.toMatch(/ICT/i);
    }
  });

  it('sin atajo no hay definición', () => {
    expect(atajoGestor('')).toBeUndefined();
  });

  it('«Mis trámites» no fija estado: trae los del usuario en cualquier estado', () => {
    expect(atajoGestor('mis_tramites')?.estado).toBeUndefined();
  });
});

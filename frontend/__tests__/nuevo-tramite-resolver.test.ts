import { describe, expect, it } from 'vitest';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { resolveNuevoTramiteCode } from '@/lib/tramites/nuevo-tramite-resolver';

function tipo(
  code: string,
  name: string,
  family: ProcedureTypeSummary['family'],
  wizardEnabled = true,
): ProcedureTypeSummary {
  return {
    id: code,
    code,
    name,
    family,
    publicationStatus: 'published',
    isActive: true,
    wizardEnabled,
    publishedAt: null,
  };
}

describe('resolveNuevoTramiteCode', () => {
  const catalogo: ProcedureTypeSummary[] = [
    tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS'),
    tipo('MATRICULA_LEASING', 'Matrícula leasing', 'MATRICULAS'),
    tipo('TRASPASO_STANDARD', 'Traspaso', 'TRASPASO'),
    tipo('TRASPASO_UNILATERAL', 'Traspaso unilateral', 'TRASPASO'),
    tipo('BLINDAJE', 'Blindaje', 'OTROS'),
  ];

  it('resuelve matrícula estándar', () => {
    const r = resolveNuevoTramiteCode({ tipo: 'MATRICULAS', leasing: false }, catalogo);
    expect(r).toEqual({ ok: true, procedureTypeCode: 'MATRICULA_NUEVA' });
  });

  it('resuelve matrícula leasing cuando el code existe', () => {
    const r = resolveNuevoTramiteCode({ tipo: 'MATRICULAS', leasing: true }, catalogo);
    expect(r).toEqual({ ok: true, procedureTypeCode: 'MATRICULA_LEASING' });
  });

  it('cae a matrícula estándar si leasing no está en catálogo', () => {
    const sinLeasing = catalogo.filter((t) => t.code !== 'MATRICULA_LEASING');
    const r = resolveNuevoTramiteCode({ tipo: 'MATRICULAS', leasing: true }, sinLeasing);
    expect(r).toEqual({ ok: true, procedureTypeCode: 'MATRICULA_NUEVA' });
  });

  it('resuelve traspaso bilateral y unilateral', () => {
    expect(
      resolveNuevoTramiteCode(
        { tipo: 'TRASPASO', modalidadTraspaso: 'bilateral' },
        catalogo,
      ),
    ).toEqual({ ok: true, procedureTypeCode: 'TRASPASO_STANDARD' });
    expect(
      resolveNuevoTramiteCode(
        { tipo: 'TRASPASO', modalidadTraspaso: 'unilateral' },
        catalogo,
      ),
    ).toEqual({ ok: true, procedureTypeCode: 'TRASPASO_UNILATERAL' });
  });

  it('exige subtipo en Otros y usa el code del catálogo', () => {
    expect(resolveNuevoTramiteCode({ tipo: 'OTROS' }, catalogo).ok).toBe(false);
    expect(
      resolveNuevoTramiteCode({ tipo: 'OTROS', subtipoOtrosCode: 'BLINDAJE' }, catalogo),
    ).toEqual({ ok: true, procedureTypeCode: 'BLINDAJE' });
  });

  it('rechaza familias bloqueadas por compañía', () => {
    const r = resolveNuevoTramiteCode(
      { tipo: 'MATRICULAS' },
      catalogo,
      { matriculas: true },
    );
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe('blocked');
  });

  it('no inventa codes sin wizardEnabled', () => {
    const r = resolveNuevoTramiteCode(
      { tipo: 'OTROS', subtipoOtrosCode: 'BLINDAJE' },
      [tipo('BLINDAJE', 'Blindaje', 'OTROS', false)],
    );
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe('not-found');
  });
});

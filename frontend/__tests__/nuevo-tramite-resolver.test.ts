import { describe, expect, it } from 'vitest';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { infoTextNuevoTramite, resolveNuevoTramiteCode } from '@/lib/tramites/nuevo-tramite-resolver';

function tipo(
  code: string,
  name: string,
  family: ProcedureTypeSummary['family'],
  wizardEnabled = true,
  description: string | null = null,
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
    description,
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

  // Defecto silencioso: `primerCodeDisponible` cae al primer tipo habilitado de la familia, así que
  // con TRASPASO_UNILATERAL apagado —como estuvo el catálogo desde que el tipo existe— elegir
  // «Traspaso Unilateral» abría un traspaso BILATERAL sin avisar: otro FUR, otros firmantes, otro
  // checklist. El mensaje de «no está habilitado» no llegaba a verse nunca.
  it('el unilateral NO cae al bilateral cuando su tipo no está habilitado', () => {
    const sinUnilateral = catalogo.filter((t) => t.code !== 'TRASPASO_UNILATERAL');
    const r = resolveNuevoTramiteCode(
      { tipo: 'TRASPASO', modalidadTraspaso: 'unilateral' },
      sinUnilateral,
    );

    expect(r.ok).toBe(false);
    if (!r.ok) {
      expect(r.reason).toBe('not-found');
      expect(r.message).toMatch(/unilateral no está habilitado/i);
    }
  });

  it('el unilateral tampoco cae al bilateral cuando su tipo existe pero está deshabilitado', () => {
    const apagado = catalogo.map((t) =>
      t.code === 'TRASPASO_UNILATERAL' ? { ...t, wizardEnabled: false } : t,
    );
    const r = resolveNuevoTramiteCode(
      { tipo: 'TRASPASO', modalidadTraspaso: 'unilateral' },
      apagado,
    );

    expect(r.ok).toBe(false);
  });

  // El bilateral SÍ conserva la caída: ahí las variantes son intercambiables para empezar (da igual
  // que el catálogo lo llame TRASPASO_STANDARD, TRASPASO_BILATERAL o TRASPASO).
  it('el bilateral conserva la caída al primer traspaso habilitado', () => {
    const otroNombre = [tipo('TRASPASO_OTRO_NOMBRE', 'Traspaso', 'TRASPASO')];
    expect(
      resolveNuevoTramiteCode({ tipo: 'TRASPASO', modalidadTraspaso: 'bilateral' }, otroNombre),
    ).toEqual({ ok: true, procedureTypeCode: 'TRASPASO_OTRO_NOMBRE' });
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

// HU #12126 — el copy de la franja informativa ya no está fijo en código: sale del `description`
// del tipo resuelto en el catálogo, para las tres familias (antes Matrícula/Traspaso tenían texto
// hardcodeado y Otros no mostraba nada nunca).
describe('infoTextNuevoTramite', () => {
  it('AC1 — retorna el description del code resuelto para Matrícula y Traspaso', () => {
    const catalogo: ProcedureTypeSummary[] = [
      tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS', true, 'Copy matrícula tradicional.'),
      tipo('MATRICULA_LEASING', 'Matrícula leasing', 'MATRICULAS', true, 'Copy matrícula leasing.'),
      tipo('TRASPASO_STANDARD', 'Traspaso', 'TRASPASO', true, 'Copy traspaso bilateral.'),
      tipo('TRASPASO_UNILATERAL', 'Traspaso unilateral', 'TRASPASO', true, 'Copy traspaso unilateral.'),
    ];

    expect(infoTextNuevoTramite('MATRICULAS', { leasing: false }, catalogo)).toBe(
      'Copy matrícula tradicional.',
    );
    expect(infoTextNuevoTramite('MATRICULAS', { leasing: true }, catalogo)).toBe(
      'Copy matrícula leasing.',
    );
    expect(
      infoTextNuevoTramite('TRASPASO', { modalidadTraspaso: 'bilateral' }, catalogo),
    ).toBe('Copy traspaso bilateral.');
    expect(
      infoTextNuevoTramite('TRASPASO', { modalidadTraspaso: 'unilateral' }, catalogo),
    ).toBe('Copy traspaso unilateral.');
  });

  it('AC1/AC3 — Otros también resuelve su description por el subtipo elegido (ya no retorna null siempre)', () => {
    const catalogo: ProcedureTypeSummary[] = [
      tipo('BLINDAJE', 'Blindaje', 'OTROS', true, 'Copy de blindaje.'),
    ];
    expect(
      infoTextNuevoTramite('OTROS', { subtipoOtrosCode: 'BLINDAJE' }, catalogo),
    ).toBe('Copy de blindaje.');
  });

  it('AC2 — sin description configurado no hay franja (null, no texto de relleno)', () => {
    const catalogo: ProcedureTypeSummary[] = [
      tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS', true, null),
    ];
    expect(infoTextNuevoTramite('MATRICULAS', { leasing: false }, catalogo)).toBeNull();
  });

  it('AC2 — description en blanco cuenta como no configurado', () => {
    const catalogo: ProcedureTypeSummary[] = [
      tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS', true, '   '),
    ];
    expect(infoTextNuevoTramite('MATRICULAS', { leasing: false }, catalogo)).toBeNull();
  });

  it('sin tipo elegido no hay franja', () => {
    expect(infoTextNuevoTramite(null, {}, [])).toBeNull();
  });

  it('sin ningún tipo resoluble en el catálogo no hay franja', () => {
    expect(infoTextNuevoTramite('MATRICULAS', { leasing: false }, [])).toBeNull();
  });
});

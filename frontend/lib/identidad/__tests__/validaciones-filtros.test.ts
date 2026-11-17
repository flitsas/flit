import { describe, expect, it } from 'vitest';
import {
  EMPTY_VALIDACIONES_FILTERS,
  buildPersonApiFilters,
  describeIdentityFilter,
  hasActiveValidacionesFilters,
  splitPersonaODocumentoQuery,
  toUiFilters,
  validateIdentityFilter,
} from '@/lib/identidad/validaciones-filtros';

// HU #12707 — la barra nueva debe producir EXACTAMENTE los parámetros que producía el panel anterior.
describe('buildPersonApiFilters (AC2 — mismos parámetros de API)', () => {
  it('sin filtros no envía ningún parámetro', () => {
    expect(buildPersonApiFilters(EMPTY_VALIDACIONES_FILTERS)).toEqual({
      name: undefined,
      documentNumber: undefined,
      status: undefined,
      createdFrom: undefined,
      createdTo: undefined,
      vigenciaEstado: undefined,
      expiraDesde: undefined,
      expiraHasta: undefined,
      venceEnDias: undefined,
    });
    // HU #11006 (CF-03) — sin «Origen» la clave ni siquiera viaja: se listan ambos tipos.
    expect(buildPersonApiFilters(EMPTY_VALIDACIONES_FILTERS)).not.toHaveProperty('standalone');
  });

  it('la combinación del panel anterior se traduce igual', () => {
    const ui = toUiFilters('1020445118', { desde: '2026-09-01', hasta: '2026-09-18' }, [
      { fieldId: 'status', values: ['aprobado'] },
      { fieldId: 'vigencia', values: ['por_vencer'] },
      { fieldId: 'venceEntre', values: ['2026-10-01', '2026-10-15'] },
      { fieldId: 'venceEnDias', values: ['7'] },
    ]);

    expect(buildPersonApiFilters(ui)).toEqual({
      name: undefined,
      documentNumber: '1020445118',
      status: 'aprobado',
      createdFrom: '2026-09-01T00:00:00',
      createdTo: '2026-09-18T23:59:59',
      vigenciaEstado: 'por_vencer',
      expiraDesde: '2026-10-01T00:00:00',
      expiraHasta: '2026-10-15T23:59:59',
      venceEnDias: 7,
    });
  });

  it('el buscador distingue nombre de documento con la misma regla de antes', () => {
    expect(buildPersonApiFilters(toUiFilters('Ana Compradora', null, []))).toMatchObject({
      name: 'Ana Compradora',
      documentNumber: undefined,
    });
    expect(buildPersonApiFilters(toUiFilters('CC 1020 4451', null, []))).toMatchObject({
      name: undefined,
      documentNumber: '10204451',
    });
  });

  it('AC3 — Origen envía standalone=true (prevalidación) o standalone=false (trámite)', () => {
    expect(buildPersonApiFilters(toUiFilters('', null, [{ fieldId: 'origen', values: ['prevalidacion'] }])).standalone).toBe(true);
    expect(buildPersonApiFilters(toUiFilters('', null, [{ fieldId: 'origen', values: ['tramite'] }])).standalone).toBe(false);
  });

  it('AC4 — «Sin periodo» no envía fechas', () => {
    const api = buildPersonApiFilters(toUiFilters('', null, []));
    expect(api.createdFrom).toBeUndefined();
    expect(api.createdTo).toBeUndefined();
  });

  it('cualquier filtro aplicado cuenta como activo (vacío con filtros ≠ módulo sin datos)', () => {
    expect(hasActiveValidacionesFilters(EMPTY_VALIDACIONES_FILTERS)).toBe(false);
    expect(hasActiveValidacionesFilters(toUiFilters('', null, [{ fieldId: 'origen', values: ['tramite'] }]))).toBe(true);
  });
});

// Ajuste 2026-11-10 (D1=a) — la caja única también busca por radicado: un texto que empieza por
// «FT» (sin distinguir mayúsculas) viaja como `referenceNumber`, normalizado en mayúsculas, ANTES de
// la regla del 80 % de dígitos (que hoy convertía «FT1-0000012» en el documento 10000012).
describe('buscador — radicado FT (ajuste 2026-11-10)', () => {
  it('«FT1-0000012» se busca como radicado, no como documento', () => {
    expect(splitPersonaODocumentoQuery('FT1-0000012')).toEqual({
      name: '',
      documentNumber: '',
      referenceNumber: 'FT1-0000012',
    });
  });

  it('insensible a mayúsculas, recorta espacios y normaliza a mayúsculas (coincidencia parcial)', () => {
    expect(splitPersonaODocumentoQuery('  ft1-00000 ').referenceNumber).toBe('FT1-00000');
    expect(splitPersonaODocumentoQuery('Ft2').referenceNumber).toBe('FT2');
    expect(splitPersonaODocumentoQuery('ft').referenceNumber).toBe('FT');
  });

  it('solo dígitos sigue siendo documento y el texto libre sigue siendo nombre', () => {
    expect(splitPersonaODocumentoQuery('1020445118')).toEqual({
      name: '',
      documentNumber: '1020445118',
      referenceNumber: '',
    });
    expect(splitPersonaODocumentoQuery('Ana Compradora')).toEqual({
      name: 'Ana Compradora',
      documentNumber: '',
      referenceNumber: '',
    });
  });

  it('un nombre que empieza por «Ft» seguido de letras no se toma por radicado', () => {
    expect(splitPersonaODocumentoQuery('Ftima Gómez')).toMatchObject({
      name: 'Ftima Gómez',
      referenceNumber: '',
    });
  });

  it('contrato — llega a la API como `referenceNumber` y cuenta como filtro activo', () => {
    const ui = toUiFilters('ft1-0000012', null, []);
    expect(buildPersonApiFilters(ui)).toMatchObject({
      referenceNumber: 'FT1-0000012',
      name: undefined,
      documentNumber: undefined,
    });
    expect(hasActiveValidacionesFilters(ui)).toBe(true);
    expect(buildPersonApiFilters(EMPTY_VALIDACIONES_FILTERS).referenceNumber).toBeUndefined();
  });
});

describe('validateIdentityFilter (AC10 — un filtro inválido no se aplica)', () => {
  it('«Vence en ≤ N días» exige un entero', () => {
    expect(validateIdentityFilter('venceEnDias', ['abc'])).toMatch(/solo números enteros/);
    expect(validateIdentityFilter('venceEnDias', ['7.5'])).toMatch(/solo números enteros/);
    expect(validateIdentityFilter('venceEnDias', [''])).toMatch(/número de días/);
    expect(validateIdentityFilter('venceEnDias', ['9999'])).toMatch(/entre 0 y/);
    expect(validateIdentityFilter('venceEnDias', ['7'])).toBeNull();
  });

  it('«Vence entre…» rechaza un rango invertido y acepta un extremo solo', () => {
    expect(validateIdentityFilter('venceEntre', ['2026-10-15', '2026-10-01'])).toMatch(/no puede ser anterior/);
    expect(validateIdentityFilter('venceEntre', ['', ''])).toMatch(/al menos una fecha/);
    expect(validateIdentityFilter('venceEntre', ['2026-10-01', ''])).toBeNull();
  });

  it('una opción fuera del catálogo no se acepta', () => {
    expect(validateIdentityFilter('status', ['inventado'])).toMatch(/Elige una opción/);
    expect(validateIdentityFilter('status', ['rechazado'])).toBeNull();
  });
});

describe('describeIdentityFilter (texto del chip)', () => {
  it('rotula cada filtro con su valor legible', () => {
    expect(describeIdentityFilter({ fieldId: 'status', values: ['pendiente_envio'] })).toBe('Estado: Pendiente de envío');
    expect(describeIdentityFilter({ fieldId: 'origen', values: ['prevalidacion'] })).toBe('Origen: Prevalidación');
    expect(describeIdentityFilter({ fieldId: 'venceEnDias', values: ['1'] })).toBe('Vence en ≤ 1 día');
    expect(describeIdentityFilter({ fieldId: 'venceEntre', values: ['2026-10-01', '2026-10-15'] })).toBe(
      'Vence entre 01/10/2026 y 15/10/2026',
    );
    expect(describeIdentityFilter({ fieldId: 'venceEntre', values: ['', '2026-10-15'] })).toBe('Vence hasta 15/10/2026');
  });
});

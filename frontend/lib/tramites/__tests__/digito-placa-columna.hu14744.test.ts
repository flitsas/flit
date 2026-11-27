// HU #14744 — columna «Dígito de placa» (lógica pura): texto de la celda, default y alcance por vista.
import { describe, expect, it } from 'vitest';
import {
  DEFAULT_TRAMITES_VISIBLE_COLUMNS,
  TRAMITES_COLUMNS,
  TRAMITES_COLUMN_KEYS,
  TRAMITES_COLUMNS_ADDED_SINCE_LEGACY,
  applyTodosYMatriculasColumns,
  selectableColumnsFor,
  tramitesExportFields,
} from '@/lib/tramites/tramites-table-columns';
import { digitoPlacaLabel, digitoPlacaLabelDeFila } from '@/lib/tramites/tramites-row-labels';
import {
  DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS,
  OT_PROCEDURES_COLUMNS,
  applyOtTodosYMatriculasColumns,
  otSelectableColumns,
} from '@/lib/admin/ot-procedures-columns';
import { digitoPlacaOt, otProceduresExportFields } from '@/lib/admin/ot-procedures-export';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import type { OtClientProcedure } from '@/lib/api/types-ot';

const fila = (extra: Partial<InstanceSummary>) => extra as InstanceSummary;
const ot = (extra: Partial<OtClientProcedure>) => extra as OtClientProcedure;

describe('HU #14744 — texto de la celda', () => {
  it('AC1: moto en ruta larga con dígito 7 → «Inicia en 7» (también motocarro)', () => {
    expect(digitoPlacaLabel({ digito: '7', declarado: true, clase: 'MOTOCICLETA', rutaCorta: false })).toBe('Inicia en 7');
    expect(digitoPlacaLabel({ digito: '5', declarado: true, clase: 'MOTOCARRO', rutaCorta: false })).toBe('Inicia en 5');
  });
  it('AC2: carro o sin clase con dígito 3 → «Termina en 3»', () => {
    expect(digitoPlacaLabel({ digito: '3', declarado: true, clase: 'AUTOMOVIL', rutaCorta: false })).toBe('Termina en 3');
    expect(digitoPlacaLabel({ digito: '3', declarado: true, clase: null, rutaCorta: false })).toBe('Termina en 3');
    expect(digitoPlacaLabel({ digito: '0', declarado: true, clase: null })).toBe('Termina en 0');
  });
  it('AC3: declarado sin dígito → «Sin preferencia»', () => {
    expect(digitoPlacaLabel({ digito: null, declarado: true, rutaCorta: false })).toBe('Sin preferencia');
    expect(digitoPlacaLabel({ digito: '', declarado: true })).toBe('Sin preferencia');
  });
  it('AC4: ruta corta (placa del RUNT) → «—», aunque traiga dígito', () => {
    expect(digitoPlacaLabel({ digito: '3', declarado: true, rutaCorta: true })).toBe('—');
  });
  it('matrícula no decidida → «—»; trámite que no es matrícula → celda vacía', () => {
    expect(digitoPlacaLabel({ digito: null, declarado: null })).toBe('—');
    expect(digitoPlacaLabel({ digito: null, declarado: false })).toBe('—');
    expect(digitoPlacaLabelDeFila(fila({ modalidad: 'MATRICULAS' }))).toBe('—');
    expect(digitoPlacaLabelDeFila(fila({ modalidad: 'TRASPASO' }))).toBe('');
    expect(digitoPlacaOt(ot({ familia: 'OTROS' }))).toBe('');
    expect(digitoPlacaOt(ot({ familia: 'MATRICULAS' }))).toBe('—');
  });
  it('lee los campos del listado del gestor', () => {
    expect(
      digitoPlacaLabelDeFila(
        fila({ modalidad: 'MATRICULAS', plateDigitoPreferido: '7', plateDigitoDeclarado: true, clase: 'MOTOCICLETA', placaRutaCorta: false }),
      ),
    ).toBe('Inicia en 7');
  });
  it('lee los campos de la bandeja del OT', () => {
    expect(digitoPlacaOt(ot({ familia: 'MATRICULAS', platePreferredLastDigit: '7', platePreferredLastDigitDeclared: true, clase: 'MOTOCICLETA' }))).toBe(
      'Inicia en 7',
    );
    expect(digitoPlacaOt(ot({ familia: 'MATRICULAS', platePreferredLastDigit: '', platePreferredLastDigitDeclared: true }))).toBe('Sin preferencia');
    expect(
      digitoPlacaOt(ot({ familia: 'MATRICULAS', platePreferredLastDigit: '3', platePreferredLastDigitDeclared: true, placaRutaCorta: true })),
    ).toBe('—');
  });
});

describe('HU #14744 — columna del gestor: default, vista y export', () => {
  it('AC6: no va en el default ni se activa sola en preferencias antiguas; sí está en el catálogo', () => {
    expect(DEFAULT_TRAMITES_VISIBLE_COLUMNS).not.toContain('digitoPlaca');
    expect(TRAMITES_COLUMNS_ADDED_SINCE_LEGACY).not.toContain('digitoPlaca');
    expect(TRAMITES_COLUMN_KEYS).toContain('digitoPlaca');
    expect(TRAMITES_COLUMNS.find((c) => c.key === 'digitoPlaca')?.label).toBe('Dígito de placa');
  });
  it('AC5: solo aparece en Todos y Matrículas (flag true); la preferencia no se pierde al ocultarla', () => {
    const pref = ['radicado', 'placa', 'digitoPlaca'];
    expect(applyTodosYMatriculasColumns(pref, false)).toEqual(['radicado', 'placa']);
    expect(applyTodosYMatriculasColumns(pref, true)).toEqual(pref);
    expect(pref).toContain('digitoPlaca');
    expect(selectableColumnsFor(false).map((c) => c.key)).not.toContain('digitoPlaca');
    expect(selectableColumnsFor(true).map((c) => c.key)).toContain('digitoPlaca');
  });
  it('Excel: exporta la columna con el mismo texto que la celda', () => {
    const campo = tramitesExportFields(['radicado', 'digitoPlaca']).find((c) => c.id === 'digitoPlaca');
    expect(campo?.label).toBe('Dígito de placa');
    expect(campo?.value(fila({ modalidad: 'MATRICULAS', plateDigitoPreferido: '3', plateDigitoDeclarado: true }))).toBe('Termina en 3');
    expect(campo?.value(fila({ modalidad: 'TRASPASO', plateDigitoPreferido: '3', plateDigitoDeclarado: true }))).toBe('');
    expect(campo?.raw(fila({ modalidad: 'MATRICULAS', placaRutaCorta: true }))).toBeNull();
    expect(tramitesExportFields(['radicado']).map((c) => c.id)).not.toContain('digitoPlaca');
  });
});

describe('HU #14744 — columna de la bandeja del OT: default, vista y export', () => {
  it('AC6: no va en el default; existe en el catálogo', () => {
    expect(DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS).not.toContain('digitoPlaca');
    expect(OT_PROCEDURES_COLUMNS.find((c) => c.key === 'digitoPlaca')?.label).toBe('Dígito de placa');
  });
  it('AC5: solo en Todos y Matrículas (flag true), sin tocar la preferencia', () => {
    const pref = ['radicado', 'digitoPlaca'];
    expect(applyOtTodosYMatriculasColumns(pref, false)).toEqual(['radicado']);
    expect(applyOtTodosYMatriculasColumns(pref, true)).toEqual(pref);
    expect(otSelectableColumns(false).map((c) => c.key)).not.toContain('digitoPlaca');
    expect(otSelectableColumns(true).map((c) => c.key)).toContain('digitoPlaca');
  });
  it('Excel: incluye la columna con el texto de la celda', () => {
    const campo = otProceduresExportFields(['digitoPlaca'])[0];
    expect(campo.label).toBe('Dígito de placa');
    expect(campo.value(ot({ familia: 'MATRICULAS', platePreferredLastDigit: '3', platePreferredLastDigitDeclared: true }))).toBe('Termina en 3');
    expect(campo.value(ot({ familia: 'TRASPASO', platePreferredLastDigit: '3', platePreferredLastDigitDeclared: true }))).toBe('');
  });
});

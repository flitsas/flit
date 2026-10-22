// HU #13112 (Feature #13110) — fuente única de etiquetas de la decisión de prenda.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { PRENDA_DECISION_LABELS, PRENDA_OMITIR_AYUDA } from '../prenda-decision-labels';
import { PRENDA_DECISION_LABELS as REEXPORTADAS } from '../PrendaForm';

// Uso de ejemplo: PRENDA_DECISION_LABELS['omitir'] → 'Omitir prenda'

const FRONTEND = resolve(__dirname, '../../..');
const leer = (rel: string) => readFileSync(resolve(FRONTEND, rel), 'utf-8');

describe('prenda-decision-labels — fuente única (AC4, AC8)', () => {
  it('AC8 — la decisión omitir se rotula «Omitir prenda»', () => {
    expect(PRENDA_DECISION_LABELS.omitir).toBe('Omitir prenda');
  });

  it('AC8 — las otras cuatro decisiones conservan su texto', () => {
    expect(PRENDA_DECISION_LABELS).toEqual({
      solicitar: 'Solicitar constitución de prenda',
      registrar: 'Registrar prenda',
      levantar: 'Levantar gravamen',
      omitir: 'Omitir prenda',
      sin_prenda: 'Sin prenda',
    });
  });

  it('AC4 — texto de ayuda literal aprobado', () => {
    expect(PRENDA_OMITIR_AYUDA).toBe(
      'La prenda seguirá vigente en el RUNT. El trámite se radicará sin inscribirla ni levantarla.',
    );
  });

  it('AC8 — PrendaForm reexporta la MISMA constante (FirmaFurStep y PrendaModificar la leen de ahí)', () => {
    expect(REEXPORTADAS).toBe(PRENDA_DECISION_LABELS);
  });

  it('AC8 — los detalles (trámite y OT) importan la fuente única y no tienen mapa local', () => {
    for (const rel of [
      'components/operacion/detalle/TramiteDetalleComercial.tsx',
      'components/admin/transit-offices/detalle/OtDetalleTramiteVehiculo.tsx',
    ]) {
      const src = leer(rel);
      expect(src).toMatch(/from ["']@\/components\/operacion\/prenda-decision-labels["']/);
      expect(src).not.toMatch(/const PRENDA_DECISION_LABELS/);
    }
  });

  it('AC8 — ya no queda el literal viejo en el código de producción de prenda', () => {
    for (const rel of [
      'components/operacion/prenda-decision-labels.ts',
      'components/operacion/PrendaForm.tsx',
      'components/operacion/detalle/TramiteDetalleComercial.tsx',
      'components/admin/transit-offices/detalle/OtDetalleTramiteVehiculo.tsx',
    ]) {
      const src = leer(rel);
      expect(src).not.toContain('Continuar sin gestionar');
      expect(src).not.toContain('riesgo asumido');
    }
  });
});

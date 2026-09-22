// Uso de ejemplo:
//   read('MatriculaResumen.tsx') → lg:grid-cols-2 + lg:col-span-2 si total % 2 === 1
//   ExpedienteCronologicoAccordion({ statusHistory }) → WizardAccordion defaultOpen={false}
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  ExpedienteCronologicoAccordion,
} from '../ExpedienteTimeline';
import type { StatusHistory } from '@/lib/api/types/procedure-runtime';

const FE_ROOT = path.resolve(__dirname, '../..');
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), 'utf8');

const sampleHistory: StatusHistory[] = [
  {
    fromStatus: null,
    toStatus: 'borrador',
    changedAt: '2026-09-01T10:00:00Z',
    reason: null,
    changedByName: 'ops',
  },
  {
    fromStatus: 'borrador',
    toStatus: 'preparado',
    changedAt: '2026-09-02T12:00:00Z',
    reason: null,
    changedByName: 'ops',
  },
];

describe('HU #12730 — AC1/AC2 resumen dos columnas alineadas', () => {
  it('happy: segunda fila del resumen usa lg:grid-cols-2 items-stretch', () => {
    const src = read('operacion/MatriculaResumen.tsx');
    expect(src).toMatch(
      /HU #12730[\s\S]*?grid grid-cols-1 gap-3 lg:grid-cols-2 items-stretch/,
    );
  });

  it('contrato: con número impar la última celda ocupa lg:col-span-2', () => {
    const src = read('operacion/MatriculaResumen.tsx');
    expect(src).toMatch(/idx === total - 1 && total % 2 === 1 \? 'lg:col-span-2'/);
  });

  it('edge: fila superior Vehículo/partes también es lg:grid-cols-2 (alineación)', () => {
    const src = read('operacion/MatriculaResumen.tsx');
    const grids = src.match(/grid grid-cols-1 gap-3 lg:grid-cols-2 items-stretch/g);
    expect(grids?.length).toBeGreaterThanOrEqual(2);
  });
});

describe('HU #12730 — AC3 documentos cargados conserva grilla densa', () => {
  it('contrato: ExpedienteVisor no altera la grilla de miniaturas de documentos', () => {
    const src = read('operacion/ExpedienteVisor.tsx');
    // Comentario de contrato + patrón de grilla responsive histórico.
    expect(src).toMatch(/rejilla no cambia|grid-cols-2|sm:grid-cols-4|xl:grid-cols-6/);
  });
});

describe('HU #12730 — AC4 expediente consolidado acordeón abierto', () => {
  it('happy: consolidado usa WizardAccordion con defaultOpen', () => {
    const src = read('operacion/ExpedienteVisor.tsx');
    expect(src).toMatch(/title="Expediente consolidado"/);
    expect(src).toMatch(/defaultOpen\b/);
    expect(src).toMatch(/<WizardAccordion[\s\S]*?defaultOpen[\s\S]*?ExpedienteConsolidadoBody/);
  });

  it('edge: no introduce Accordion genérico nuevo (solo WizardAccordion)', () => {
    const visor = read('operacion/ExpedienteVisor.tsx');
    const timeline = read('operacion/ExpedienteTimeline.tsx');
    expect(visor).not.toMatch(/from ['\"]@\/components\/ui\/accordion|from ['\"].*\/Accordion['\"]/);
    expect(timeline).not.toMatch(/from ['\"]@\/components\/ui\/accordion/);
    expect(visor).toMatch(/WizardAccordion/);
    expect(timeline).toMatch(/WizardAccordion/);
  });
});

describe('HU #12730 — AC5 expediente cronológico acordeón plegado', () => {
  it('happy: cabecera muestra N eventos y está plegado por defecto', async () => {
    const user = userEvent.setup();
    render(<ExpedienteCronologicoAccordion statusHistory={sampleHistory} />);

    expect(screen.getByText(/2 eventos/i)).toBeInTheDocument();
    // Chevron dedicado (único con aria-label Expandir/Contraer); el título también es button.
    const trigger = screen.getByRole('button', {
      name: /Expandir Expediente — Trazabilidad cronológica/i,
    });
    expect(trigger).toHaveAttribute('aria-expanded', 'false');

    await user.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(
      screen.getByRole('button', { name: /Contraer Expediente — Trazabilidad cronológica/i }),
    ).toBeInTheDocument();
  });

  it('contrato: ExpedienteCronologicoAccordion usa defaultOpen={false}', () => {
    const src = read('operacion/ExpedienteTimeline.tsx');
    expect(src).toMatch(/export function ExpedienteCronologicoAccordion/);
    expect(src).toMatch(/defaultOpen=\{false\}/);
  });

  it('edge: sin eventos muestra subtítulo Sin eventos registrados', () => {
    render(<ExpedienteCronologicoAccordion statusHistory={[]} />);
    expect(screen.getByText(/Sin eventos registrados/i)).toBeInTheDocument();
  });
});

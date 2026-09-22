// Uso de ejemplo:
//   read('TramiteWizard.tsx') contiene `lg:grid-cols-2` junto a Prenda + Observaciones
//   PrendaForm(modalidad=traspaso, decisions=[...4]) → `.md:grid-cols-3` con certificado sin span-3
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { PrendaForm } from '../PrendaForm';
import { furAutoObservations, furObservationsPreview } from '../fur-auto-observations';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { FieldValue } from '@/lib/api/types/procedure-runtime';

function fields(pairs: Record<string, string>): FieldValue[] {
  return Object.entries(pairs).map(([fieldKey, valueText]) => ({ fieldKey, valueText }) as FieldValue);
}

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getPrenda: vi.fn().mockResolvedValue([]),
    getInstance: vi.fn().mockResolvedValue({ fieldValues: [] }),
    putPrenda: vi.fn().mockResolvedValue({
      id: '1',
      decision: 'registrar',
      estado: 'vigente',
      acreedorNombre: 'Banco XYZ',
      acreedorDocumento: null,
      createdAt: '2026-07-07T00:00:00Z',
    }),
    getChecklist: vi.fn().mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true }),
    getAttachments: vi.fn().mockResolvedValue([]),
    uploadAttachment: vi.fn(),
    deleteAttachment: vi.fn(),
    fetchAttachmentPreviewUrl: vi.fn(),
    downloadAttachment: vi.fn(),
  },
}));

const client = vi.mocked(tramitesClient);
const FE_ROOT = path.resolve(__dirname, '../..');
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), 'utf8');

describe('HU #12727 — AC1 Prenda y Observaciones misma fila (lg)', () => {
  it('happy: traspaso y matrícula usan lg:grid-cols-2 con items-stretch', () => {
    const wizard = read('operacion/TramiteWizard.tsx');
    const matches = wizard.match(
      /grid grid-cols-1 items-stretch gap-3 lg:grid-cols-2/g,
    );
    expect(matches?.length).toBeGreaterThanOrEqual(2);
  });

  it('contrato: Observaciones entra en la misma WizardAccordionRow que Prenda', () => {
    const wizard = read('operacion/TramiteWizard.tsx');
    expect(wizard).toMatch(/muestraSeccionPrenda/);
    expect(wizard).toMatch(/observacionesAccordion/);
    // Dos filas (traspaso + matrícula): cada bloque lg:grid-cols-2 cierra con {observacionesAccordion}.
    const pairs = [
      ...wizard.matchAll(
        /lg:grid-cols-2">[\s\S]*?Asignación de Prenda \/ Limitación a la Propiedad[\s\S]*?\{observacionesAccordion\}/g,
      ),
    ];
    expect(pairs.length).toBeGreaterThanOrEqual(2);
  });

  it('edge: sin sección de prenda Observaciones se renderiza sola (fuera de la fila)', () => {
    const wizard = read('operacion/TramiteWizard.tsx');
    expect(wizard).toMatch(/\{!muestraSeccionPrenda && observacionesAccordion\}/);
  });
});

describe('HU #12727 — AC2 grilla de tres columnas (prenda + certificado)', () => {
  beforeEach(() => {
    client.getPrenda.mockClear();
    client.getAttachments.mockClear();
    client.getInstance.mockResolvedValue({ fieldValues: [] } as never);
  });

  it('happy: traspaso con acreedor declara md:grid-cols-3 y certificado sin col-span-3', async () => {
    const { container } = render(
      <PrendaForm
        instanceId="hu12727"
        modalidad="traspaso"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        embeddedInWizard
        documentRequired
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('¿Al vehículo se le asociará una prenda?'), {
      target: { value: 'registrar' },
    });

    const grid = container.querySelector('.md\\:grid-cols-3');
    expect(grid).toBeTruthy();
    const g = grid as HTMLElement;
    expect(within(g).getByLabelText('Acreedor (beneficiario)')).toBeInTheDocument();
    expect(within(g).getByLabelText(/NIT \/ documento del acreedor/i)).toBeInTheDocument();
    expect(within(g).getByText('Certificado / registro de prenda')).toBeInTheDocument();
    expect(g.querySelector('.md\\:col-span-3')).toBeNull();
  });

  it('edge: sin acreedor ni documento la grilla no fuerza tres columnas', async () => {
    const { container } = render(
      <PrendaForm
        instanceId="hu12727"
        modalidad="traspaso"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        embeddedInWizard
        documentRequired={false}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('¿Al vehículo se le asociará una prenda?'), {
      target: { value: 'omitir' },
    });

    expect(container.querySelector('.md\\:grid-cols-3')).toBeNull();
    expect(screen.queryByText('Certificado / registro de prenda')).not.toBeInTheDocument();
  });

  it('contrato: upload de certificado sigue visible (Adjuntar / Por cargar)', async () => {
    render(
      <PrendaForm
        instanceId="hu12727"
        modalidad="traspaso"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        embeddedInWizard
        documentRequired
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('¿Al vehículo se le asociará una prenda?'), {
      target: { value: 'registrar' },
    });

    expect(screen.getByText(/Por cargar/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Adjuntar/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/Subir Certificado/i)).toBeInTheDocument();
  });
});

describe('HU #12727 — AC3 apilado en pantallas pequeñas', () => {
  it('happy: fila Prenda+Observaciones arranca en grid-cols-1 (mobile-first)', () => {
    const wizard = read('operacion/TramiteWizard.tsx');
    expect(wizard).toMatch(/grid grid-cols-1 items-stretch gap-3 lg:grid-cols-2/);
  });

  it('contrato: PrendaForm traspaso usa grid-cols-1 antes del breakpoint md', async () => {
    const { container } = render(
      <PrendaForm
        instanceId="hu12727-sm"
        modalidad="traspaso"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        embeddedInWizard
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    fireEvent.change(screen.getByLabelText('¿Al vehículo se le asociará una prenda?'), {
      target: { value: 'registrar' },
    });
    const grid = container.querySelector('.md\\:grid-cols-3');
    expect(grid?.className).toMatch(/grid-cols-1/);
    expect(grid?.className).toMatch(/md:grid-cols-3/);
  });

  it('edge: labels de Acreedor/NIT y botón Adjuntar no se omiten al apilar', async () => {
    render(
      <PrendaForm
        instanceId="hu12727-sm2"
        modalidad="traspaso"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        embeddedInWizard
        documentRequired
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    fireEvent.change(screen.getByLabelText('¿Al vehículo se le asociará una prenda?'), {
      target: { value: 'registrar' },
    });
    expect(screen.getByLabelText('Acreedor (beneficiario)')).toBeVisible();
    expect(screen.getByLabelText(/NIT \/ documento del acreedor/i)).toBeVisible();
    expect(screen.getByRole('button', { name: /Adjuntar/i })).toBeVisible();
  });
});

describe('HU #12727 — AC4 contenedor de trámites simultáneos intacto', () => {
  it('happy: acordeón de simultáneos sigue fuera de la fila Prenda/Observaciones', () => {
    const wizard = read('operacion/TramiteWizard.tsx');
    expect(wizard).toMatch(
      /Trámites Simultáneos — Transformaciones del Vehículo/,
    );
    const prendaRowIdx = wizard.indexOf(
      'Asignación de Prenda / Limitación a la Propiedad',
    );
    const simIdx = wizard.indexOf('Trámites Simultáneos — Transformaciones del Vehículo');
    expect(prendaRowIdx).toBeGreaterThan(-1);
    expect(simIdx).toBeGreaterThan(prendaRowIdx);
  });

  it('contrato: gate onSimultaneosGateChange / onCompletenessChange se conserva', () => {
    const wizard = read('operacion/TramiteWizard.tsx');
    expect(wizard).toMatch(/onCompletenessChange=\{onSimultaneosGateChange\}/);
    expect(wizard).toMatch(/simultaneosGateOk/);
  });

  it('edge: DeclaracionesTramite.tsx no fue modificado en esta HU (fuente vigente)', () => {
    // Contrato de alcance: la HU declara no tocar DeclaracionesTramite; el archivo debe existir
    // y exportar el componente (regresión de módulo, no del layout de prenda).
    const decl = read('operacion/DeclaracionesTramite.tsx');
    expect(decl).toMatch(/export (function|const) DeclaracionesTramite/);
  });
});

describe('HU #12727 — AC5 previsualización FUR independiente de la posición DOM', () => {
  it('happy: transformaciones largas + manual aparecen juntos en el preview', () => {
    const colorLargo = 'NEGRO MATE METALIZADO PERLADO ESPECIAL';
    const carroceriaLarga = 'FURGON CARGA REFRIGERADA EXTENDIDA';
    const fv = fields({
      vehicle_color_runt: 'AZUL',
      vehicle_color: colorLargo,
      vehicle_body_type_runt: 'SEDAN',
      vehicle_body_type: carroceriaLarga,
    });
    const preview = furObservationsPreview('Texto libre del gestor en observaciones', fv);
    expect(preview.auto).toEqual(furAutoObservations(fv));
    expect(preview.auto.length).toBeGreaterThanOrEqual(2);
    expect(preview.auto.some((l) => l.includes(colorLargo))).toBe(true);
    expect(preview.auto.some((l) => l.includes(carroceriaLarga))).toBe(true);
    expect(preview.manual).toBe('Texto libre del gestor en observaciones');
  });

  it('contrato: el orden auto → manual no depende de dónde viva el acordeón', () => {
    const fv = fields({
      vehicle_color_runt: 'AZUL',
      vehicle_color: 'ROJO',
      vehicle_body_type_runt: 'SEDAN',
      vehicle_body_type: 'COUPE',
    });
    const preview = furObservationsPreview('Libre', fv);
    expect(preview.auto[0]).toMatch(/^Color nuevo/);
    expect(preview.auto[1]).toMatch(/^Carroceria nueva/);
    expect(preview.manual).toBe('Libre');
  });

  it('edge: valores largos no se recortan en el string del preview', () => {
    const enorme = 'X'.repeat(200);
    const fv = fields({ vehicle_color_runt: 'AZUL', vehicle_color: enorme });
    const preview = furObservationsPreview(enorme, fv);
    expect(preview.manual).toHaveLength(200);
    expect(preview.auto[0]).toContain(enorme);
    expect(preview.manual).not.toMatch(/\.\.\.$/);
  });
});

describe('HU #12727 — AC6 tema oscuro (tokens de acordeón)', () => {
  it('happy: WizardAccordion usa tokens dark:bg y borde del design system', () => {
    const accordion = read('operacion/WizardAccordion.tsx');
    expect(accordion).toMatch(/dark:bg-\[#162744\]/);
    expect(accordion).toMatch(/borderColor: '#DFE5ED'/);
    expect(accordion).toMatch(/bg-white/);
  });

  it('contrato: fila de dos columnas no introduce colores hardcodeados ajenos al acordeón', () => {
    const wizard = read('operacion/TramiteWizard.tsx');
    const rowSnippet = wizard.match(
      /grid grid-cols-1 items-stretch gap-3 lg:grid-cols-2[\s\S]{0,400}/,
    )?.[0];
    expect(rowSnippet).toBeTruthy();
    expect(rowSnippet).not.toMatch(/bg-\[#|text-\[#FF|purple/i);
  });

  it('edge: className h-full min-w-0 en ambos acordeones de la fila (estiramiento)', () => {
    const wizard = read('operacion/TramiteWizard.tsx');
    expect(wizard).toMatch(
      /title="Asignación de Prenda \/ Limitación a la Propiedad"[\s\S]{0,200}?className="h-full min-w-0"/,
    );
    expect(wizard).toMatch(
      /title="Observaciones del trámite"[\s\S]{0,120}?className="h-full min-w-0"/,
    );
  });
});

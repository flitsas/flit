// HU #13778 (Feature #13110, Bug QA #13768) — «Omitir prenda» lo decide el servidor en todo tipo
// con selector y el aviso/resumen de gravamen usan `runtReportaGravamen` del GET /wizard.
//
// Uso de ejemplo:
//   traspasoDecisions(wizard.prendaOmitAllowed)  → con `true` incluye 'omitir'; si no, nunca.
//   <PrendaForm instanceId="i" decisions={traspasoDecisions(true)} documentRequired runtReportaGravamen />
//   buildRuntPrendaSummary(fieldValues, true).tienePrendas → 'SI'
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { PrendaForm, buildRuntPrendaSummary, traspasoDecisions } from '../PrendaForm';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { FieldValue, WizardState } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getPrenda: vi.fn().mockResolvedValue([]),
    getInstance: vi.fn().mockResolvedValue({ fieldValues: [] }),
    putPrenda: vi.fn().mockResolvedValue({
      id: '1',
      decision: 'omitir',
      estado: 'vigente',
      acreedorNombre: null,
      acreedorDocumento: null,
      createdAt: '2026-10-21T00:00:00Z',
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
const SELECT_LABEL = '¿Al vehículo se le asociará una prenda?';
const AYUDA =
  'La prenda seguirá vigente en el RUNT. El trámite se radicará sin inscribirla ni levantarla.';
const AVISO_GRAVAMEN = 'RUNT reporta gravamen o prenda';

const fv = (fieldKey: string, valueText: string | null, valueJson: string | null = null): FieldValue => ({
  formFieldId: '',
  fieldKey,
  valueText,
  valueJson,
  source: 'consultation',
});

/** RUNT con banderas NO/NO y sin garantías: la señal solo puede venir del servidor (limitaciones). */
const SIN_BANDERAS_NI_GARANTIAS: FieldValue[] = [
  fv('runt_tiene_prendas', 'NO'),
  fv('runt_tiene_gravamenes', 'NO'),
  fv('runt_gravamenes', null, '[]'),
];

beforeEach(() => {
  vi.clearAllMocks();
  client.getPrenda.mockResolvedValue([]);
  client.getInstance.mockResolvedValue({ fieldValues: [] } as never);
  client.getAttachments.mockResolvedValue([]);
});

describe('HU #13778 — AC1 traspaso con OT que exige el certificado', () => {
  it('traspasoDecisions(true) ofrece «Omitir prenda» además de las tres gestiones', () => {
    expect(traspasoDecisions(true)).toEqual(['solicitar', 'registrar', 'levantar', 'omitir']);
  });

  it('el selector de traspaso ofrece «Omitir prenda» con documentRequired=true y prendaOmitAllowed=true', async () => {
    render(
      <PrendaForm
        instanceId="t1"
        modalidad="traspaso"
        documentRequired
        omitAllowed
        decisions={traspasoDecisions(true)}
        embeddedInWizard
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    const select = screen.getByLabelText(SELECT_LABEL);
    expect(within(select).getByRole('option', { name: 'Omitir prenda' })).toBeInTheDocument();
  });
});

describe('HU #13778 — AC2 Matrícula Inicial u otra familia con selector', () => {
  it('matrícula con OT que exige el certificado y omitAllowed=true ofrece «Omitir prenda»', async () => {
    render(<PrendaForm instanceId="m1" documentRequired omitAllowed />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: 'Omitir prenda' })).toBeInTheDocument();
  });

  it('otra familia que entra por placa (selector de traspaso) con omitAllowed=true ofrece «Omitir prenda»', async () => {
    render(
      <PrendaForm
        instanceId="o1"
        modalidad="traspaso"
        documentRequired
        omitAllowed
        decisions={traspasoDecisions(true)}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    expect(screen.getByRole('option', { name: 'Omitir prenda' })).toBeInTheDocument();
  });

  it('contrato: TramiteWizard resuelve la lista del selector solo con prendaOmitAllowed (sin la política del OT)', () => {
    const wizard = readFileSync(path.resolve(__dirname, '../TramiteWizard.tsx'), 'utf8');
    expect(wizard).not.toMatch(/traspasoDecisions\(prendaDocumentRequired/);
    expect(wizard.match(/traspasoDecisions\(prendaOmitAllowed\)/g)?.length).toBe(2);
  });
});

describe('HU #13778 — AC3 sin prendaOmitAllowed no hay «Omitir prenda» (fallback retirado)', () => {
  it('false o ausente nunca incluye omitir', () => {
    expect(traspasoDecisions(false)).not.toContain('omitir');
    expect(traspasoDecisions(undefined)).not.toContain('omitir');
    expect(traspasoDecisions()).toEqual(['solicitar', 'registrar', 'levantar']);
  });

  it('OT con certificado opcional (documentRequired=false) y omitAllowed ausente: el selector no ofrece omitir', async () => {
    render(
      <PrendaForm
        instanceId="t1"
        modalidad="traspaso"
        documentRequired={false}
        decisions={traspasoDecisions(undefined)}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    expect(screen.getByRole('option', { name: 'Registrar prenda' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Omitir prenda' })).not.toBeInTheDocument();
    expect(screen.queryByText(AYUDA, { exact: false })).not.toBeInTheDocument();
  });

  it('matrícula con documentRequired=false y omitAllowed ausente no ofrece omitir', async () => {
    render(<PrendaForm instanceId="m1" documentRequired={false} />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: 'Omitir prenda' })).not.toBeInTheDocument();
  });
});

describe('HU #13778 — AC4 omitir en OT que exige el certificado', () => {
  it('no monta la carga ni el rótulo obligatorio y el gate de Continuar queda listo', async () => {
    const onGate = vi.fn();
    render(
      <PrendaForm
        instanceId="t1"
        modalidad="traspaso"
        documentRequired
        omitAllowed
        decisions={traspasoDecisions(true)}
        onDocumentGateChange={onGate}
        embeddedInWizard
      />,
    );
    const select = await screen.findByLabelText(SELECT_LABEL);

    // Antes: «Solicitar prenda» exige el certificado obligatorio y bloquea Continuar.
    fireEvent.change(select, { target: { value: 'solicitar' } });
    expect(await screen.findByLabelText('Documento de soporte de prenda')).toBeInTheDocument();
    await waitFor(() => expect(onGate).toHaveBeenLastCalledWith(false));

    fireEvent.change(select, { target: { value: 'omitir' } });
    await waitFor(() => expect(onGate).toHaveBeenLastCalledWith(true));
    expect(screen.queryByLabelText('Documento de soporte de prenda')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Carga del documento de prenda')).not.toBeInTheDocument();
    expect(screen.queryByText(/obligatorio/i)).not.toBeInTheDocument();
  });
});

describe('HU #13778 — AC5 runtReportaGravamen true sin banderas ni garantías', () => {
  it('buildRuntPrendaSummary con la señal del servidor pinta SI en prendas y gravámenes', () => {
    const s = buildRuntPrendaSummary(SIN_BANDERAS_NI_GARANTIAS, true);
    expect(s.tienePrendas).toBe('SI');
    expect(s.tieneGravamenes).toBe('SI');
  });

  it('muestra el aviso de gravamen, el resumen en SI y ofrece «Omitir prenda»', async () => {
    client.getInstance.mockResolvedValue({ fieldValues: SIN_BANDERAS_NI_GARANTIAS } as never);
    render(
      <PrendaForm
        instanceId="t5"
        modalidad="traspaso"
        documentRequired
        omitAllowed
        decisions={traspasoDecisions(true)}
        runtReportaGravamen
      />,
    );
    expect(await screen.findByText(AVISO_GRAVAMEN)).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Omitir prenda' })).toBeInTheDocument();
    await waitFor(() => expect(client.getInstance).toHaveBeenCalled());
    fireEvent.click(screen.getByRole('button', { name: /Ver información de prenda \/ gravamen del RUNT/i }));
    const region = await screen.findByRole('region', { name: /detalle de prenda reportado por el runt/i });
    await waitFor(() =>
      expect(within(region).getByText('Tiene prendas').nextElementSibling?.textContent).toBe('SI'),
    );
    expect(within(region).getByText('Tiene gravámenes').nextElementSibling?.textContent).toBe('SI');
  });

  it('contrato: el tipo del GET /wizard declara runtReportaGravamen y el wizard lo pasa a PrendaForm', () => {
    const w: Pick<WizardState, 'runtReportaGravamen' | 'prendaOmitAllowed'> = {
      runtReportaGravamen: true,
      prendaOmitAllowed: true,
    };
    expect(w.runtReportaGravamen).toBe(true);
    const wizard = readFileSync(path.resolve(__dirname, '../TramiteWizard.tsx'), 'utf8');
    expect(wizard).toMatch(/runtReportaGravamen=\{wizard\?\.runtReportaGravamen\}/);
    expect(wizard.match(/runtReportaGravamen=\{runtReportaGravamen\}/g)?.length).toBe(2);
  });
});

describe('HU #13778 — AC6 runtReportaGravamen false', () => {
  it('no muestra el aviso aunque el pre-vuelo marque gravamen; el resumen conserva las banderas', async () => {
    client.getInstance.mockResolvedValue({ fieldValues: SIN_BANDERAS_NI_GARANTIAS } as never);
    render(
      <PrendaForm
        instanceId="t6"
        modalidad="traspaso"
        decisions={traspasoDecisions(false)}
        runtHasGravamen
        runtReportaGravamen={false}
      />,
    );
    await waitFor(() => expect(client.getInstance).toHaveBeenCalled());
    expect(screen.queryByText(AVISO_GRAVAMEN)).not.toBeInTheDocument();
    expect(buildRuntPrendaSummary(SIN_BANDERAS_NI_GARANTIAS, false).tienePrendas).toBe('NO');
    expect(buildRuntPrendaSummary(SIN_BANDERAS_NI_GARANTIAS).tieneGravamenes).toBe('NO');
  });

  it('runtReportaGravamen ausente: el aviso sigue el pre-vuelo como hoy', async () => {
    render(<PrendaForm instanceId="t7" modalidad="traspaso" decisions={traspasoDecisions(false)} runtHasGravamen />);
    expect(await screen.findByText(AVISO_GRAVAMEN)).toBeInTheDocument();
  });
});

describe('HU #13778 — AC7 texto de ayuda de «Omitir prenda»', () => {
  it('es exactamente el literal y no menciona la política del organismo', async () => {
    render(
      <PrendaForm
        instanceId="t8"
        modalidad="traspaso"
        documentRequired
        omitAllowed
        decisions={traspasoDecisions(true)}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    const ayuda = document.getElementById('prenda-omitir-ayuda');
    expect(ayuda).not.toBeNull();
    expect(ayuda!.textContent).toBe(`Omitir prenda: ${AYUDA}`);
    expect(ayuda!.textContent).not.toMatch(/organismo|pol[ií]tica|\bOT\b/i);
    expect(screen.getByLabelText(SELECT_LABEL)).toHaveAttribute('aria-describedby', 'prenda-omitir-ayuda');
  });
});

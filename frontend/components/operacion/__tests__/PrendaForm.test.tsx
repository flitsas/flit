// HU #10596 (R4) — formulario declarativo de prenda en matrícula.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { createRef } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import {
  PrendaForm,
  type PrendaFormHandle,
  parseRuntGravamenesJson,
  buildRuntPrendaSummary,
} from '../PrendaForm';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { FieldValue } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getPrenda: vi.fn().mockResolvedValue(null),
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

describe('PrendaForm (matrícula, R4)', () => {
  beforeEach(() => {
    client.getPrenda.mockClear();
    client.getInstance.mockClear();
    client.putPrenda.mockClear();
    client.getChecklist.mockClear();
    client.getAttachments.mockClear();
    client.getInstance.mockResolvedValue({ fieldValues: [] } as never);
  });

  it('ofrece solo las decisiones de matrícula (registrar / sin prenda) como radios', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    expect(screen.getByRole('radio', { name: 'Registrar prenda' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Sin prenda' })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: 'Levantar gravamen' })).not.toBeInTheDocument();
  });

  it('con "sin prenda" no exige documento ni datos del acreedor (puede continuar)', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('radio', { name: 'Sin prenda' }));

    expect(screen.queryByLabelText('Acreedor (beneficiario)')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Documento de soporte de prenda')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Adjuntar/i })).not.toBeInTheDocument();
  });

  it('con "registrar" pide acreedor y muestra el contenedor de carga del certificado', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('radio', { name: 'Registrar prenda' }));

    expect(screen.getByLabelText('Acreedor (beneficiario)')).toBeInTheDocument();
    expect(screen.getByLabelText('Documento de soporte de prenda')).toBeInTheDocument();
    expect(screen.getByText('Certificado / registro de prenda')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Adjuntar' })).toBeInTheDocument();
    expect(screen.getByLabelText(/Subir Certificado/i)).toBeInTheDocument();
    await waitFor(() => expect(client.getAttachments).toHaveBeenCalled());
  });

  it('en traspaso ofrece las 4 decisiones de gestión (sin "sin prenda") como radios', async () => {
    render(
      <PrendaForm
        instanceId="abc"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    expect(screen.getByRole('radio', { name: 'Solicitar constitución de prenda' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Levantar gravamen' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Continuar sin gestionar (asumo el riesgo)' })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: 'Sin prenda' })).not.toBeInTheDocument();
  });

  it('con "omitir" no muestra el contenedor de carga', async () => {
    render(
      <PrendaForm
        instanceId="abc"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(
      screen.getByRole('radio', { name: 'Continuar sin gestionar (asumo el riesgo)' }),
    );

    expect(screen.queryByLabelText('Documento de soporte de prenda')).not.toBeInTheDocument();
  });

  it('embeddedInWizard oculta el botón de guardado; save() vía ref persiste', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="abc" embeddedInWizard />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    expect(screen.queryByRole('button', { name: /Guardar decisión de prenda/i })).toBeNull();

    fireEvent.click(screen.getByRole('radio', { name: 'Registrar prenda' }));
    fireEvent.change(screen.getByLabelText('Acreedor (beneficiario)'), {
      target: { value: 'Banco XYZ' },
    });

    const ok = await ref.current!.save();
    expect(ok).toBe(true);
    await waitFor(() =>
      expect(client.putPrenda).toHaveBeenCalledWith('abc', {
        decision: 'registrar',
        acreedorNombre: 'Banco XYZ',
        acreedorDocumento: null,
      }),
    );
  });

  it('muestra alerta y desplegable con detalle RUNT hidratado', async () => {
    const fieldValues: FieldValue[] = [
      {
        formFieldId: '',
        fieldKey: 'runt_tiene_gravamenes',
        valueText: 'SI',
        valueJson: null,
        source: 'consultation',
      },
      {
        formFieldId: '',
        fieldKey: 'runt_tiene_prendas',
        valueText: 'SI',
        valueJson: null,
        source: 'consultation',
      },
      {
        formFieldId: '',
        fieldKey: 'runt_gravamenes',
        valueText: null,
        valueJson: JSON.stringify([
          {
            idPrenda: 99,
            nombreAcreedor: 'Banco Demo',
            tipoDocumentoAcreedor: 'NIT',
            numeroDocumentoAcreedor: '900111222',
            fechaInscripcion: '2024-05-10',
            estadoPrenda: 'VIGENTE',
          },
        ]),
        source: 'consultation',
      },
    ];
    client.getInstance.mockResolvedValue({ fieldValues } as never);

    render(
      <PrendaForm
        instanceId="abc"
        embeddedInWizard
        runtHasGravamen
        runtGravamenMessage="El vehículo tiene gravámenes o prendas"
      />,
    );
    await waitFor(() => expect(client.getInstance).toHaveBeenCalled());

    expect(screen.getByText('RUNT reporta gravamen o prenda')).toBeInTheDocument();
    // Con detalle de acreedor el panel se abre solo.
    await waitFor(() => expect(screen.getByText('Banco Demo')).toBeInTheDocument());
    expect(screen.getByText(/NIT 900111222/)).toBeInTheDocument();
    expect(screen.getByText('VIGENTE')).toBeInTheDocument();
  });
});

describe('parseRuntGravamenesJson / buildRuntPrendaSummary', () => {
  it('parsea ítems Kyverum (acreedor) desde valueJson', () => {
    const items = parseRuntGravamenesJson(
      JSON.stringify([
        {
          acreedor: 'BANCOLOMBIA S.A.',
          tipoDocumentoAcreedor: 'NIT',
          numeroDocumentoAcreedor: '890903938',
          fechaInscripcion: '13/06/2026',
        },
      ]),
    );
    expect(items).toHaveLength(1);
    expect(items[0].acreedor).toBe('BANCOLOMBIA S.A.');
    expect(items[0].documentoAcreedor).toBe('890903938');
    expect(items[0].fechaInscripcion).toBe('13/06/2026');
  });

  it('parsea ítems Intempo desde valueJson', () => {
    const items = parseRuntGravamenesJson(
      JSON.stringify([
        {
          idPrenda: 1,
          nombreAcreedor: 'ACME',
          numeroDocumentoAcreedor: '123',
          estadoPrenda: 'VIGENTE',
        },
      ]),
    );
    expect(items).toHaveLength(1);
    expect(items[0].acreedor).toBe('ACME');
    expect(items[0].documentoAcreedor).toBe('123');
  });

  it('arma resumen desde field_values', () => {
    const summary = buildRuntPrendaSummary([
      {
        formFieldId: '',
        fieldKey: 'runt_tiene_prendas',
        valueText: 'SI',
        valueJson: null,
        source: 'consultation',
      },
      {
        formFieldId: '',
        fieldKey: 'runt_nombre_acreedor',
        valueText: 'Banco X',
        valueJson: null,
        source: 'consultation',
      },
    ]);
    expect(summary.tienePrendas).toBe('SI');
    expect(summary.nombreAcreedor).toBe('Banco X');
  });
});

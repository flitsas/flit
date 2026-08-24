// HU #10596 (R4) — formulario declarativo de prenda en matrícula.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { createRef } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import {
  PrendaForm,
  type PrendaFormHandle,
  parseRuntGravamenesJson,
  buildRuntPrendaSummary,
  pickRuntAcreedor,
  traspasoDecisions,
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

// CF-06 (HU #10881) — "asumo el riesgo" no puede convivir con un organismo que exige el certificado:
// con el override activo, elegir `omitir` satisfacía los dos gates y dejaba la regla del OT evadible.
describe('traspasoDecisions — "omitir" y el override del organismo', () => {
  it('no ofrece "omitir" cuando el OT exige el certificado de prenda', () => {
    expect(traspasoDecisions(true)).not.toContain('omitir');
  });

  it('con el opt-out del OT vigente, "omitir" sigue disponible', () => {
    expect(traspasoDecisions(false)).toContain('omitir');
  });

  it('las decisiones que gestionan la prenda se ofrecen en ambos casos', () => {
    for (const documentRequired of [true, false]) {
      expect(traspasoDecisions(documentRequired)).toEqual(
        expect.arrayContaining(['solicitar', 'registrar', 'levantar']),
      );
    }
  });
});

describe('PrendaForm (matrícula, R4)', () => {
  beforeEach(() => {
    client.getPrenda.mockClear();
    client.getInstance.mockClear();
    client.putPrenda.mockClear();
    client.getChecklist.mockClear();
    client.getAttachments.mockClear();
    client.getInstance.mockResolvedValue({ fieldValues: [] } as never);
  });

  it('ofrece solo las decisiones de matrícula (registrar / sin prenda) como segmentado', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    expect(screen.getByRole('button', { name: 'Registrar prenda' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sin prenda' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Levantar gravamen' })).not.toBeInTheDocument();
  });

  it('con "sin prenda" no exige documento ni datos del acreedor (puede continuar)', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Sin prenda' }));

    expect(screen.queryByLabelText('Acreedor (beneficiario)')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Documento de soporte de prenda')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Adjuntar/i })).not.toBeInTheDocument();
  });

  it('con "registrar" pide acreedor y muestra el contenedor de carga del certificado', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));

    expect(screen.getByLabelText('Acreedor (beneficiario)')).toBeInTheDocument();
    expect(screen.getByLabelText('Documento de soporte de prenda')).toBeInTheDocument();
    expect(screen.getByText('Certificado / registro de prenda')).toBeInTheDocument();
    expect(screen.getByText(/Obligatorio/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Adjuntar/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/Subir Certificado/i)).toBeInTheDocument();
    await waitFor(() => expect(client.getAttachments).toHaveBeenCalled());
  });

  it('con documentRequired=false muestra el certificado como opcional', async () => {
    render(<PrendaForm instanceId="abc" documentRequired={false} />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));

    expect(screen.getByLabelText('Documento de soporte de prenda')).toBeInTheDocument();
    expect(screen.getByText(/Opcional/i)).toBeInTheDocument();
    expect(screen.queryByText(/Obligatorio/i)).not.toBeInTheDocument();
  });

  it('con documentRequired=true y registrar sin adjunto reporta gate Continuar en false', async () => {
    const onGate = vi.fn();
    render(
      <PrendaForm instanceId="abc" documentRequired onDocumentGateChange={onGate} />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));

    await waitFor(() => expect(onGate).toHaveBeenCalledWith(false));
  });

  it('con documentRequired=false y registrar reporta gate Continuar en true', async () => {
    const onGate = vi.fn();
    render(
      <PrendaForm
        instanceId="abc"
        documentRequired={false}
        onDocumentGateChange={onGate}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));

    await waitFor(() => expect(onGate).toHaveBeenCalledWith(true));
  });

  it('precarga acreedor y NIT desde la consulta RUNT cuando no hay prenda guardada', async () => {
    client.getInstance.mockResolvedValue({
      fieldValues: [
        {
          formFieldId: '',
          fieldKey: 'runt_gravamenes',
          valueText: null,
          valueJson: JSON.stringify([
            {
              nombreAcreedor: 'BANCO RUNT SA',
              numeroDocumentoAcreedor: '900123456',
            },
          ]),
          source: 'consultation',
        },
      ],
    } as never);

    render(<PrendaForm instanceId="abc" runtHasGravamen />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Registrar prenda' })).toHaveAttribute(
        'aria-pressed',
        'true',
      );
    });
    expect(screen.getByLabelText('Acreedor (beneficiario)')).toHaveValue('BANCO RUNT SA');
    expect(screen.getByLabelText('NIT / documento del acreedor')).toHaveValue('900123456');
  });

  it('en traspaso ofrece el select de gestión (solicitar/registrar/levantar/omitir)', async () => {
    render(
      <PrendaForm
        instanceId="abc"
        modalidad="traspaso"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    const select = screen.getByLabelText('¿Al vehículo se le asociará una prenda?');
    expect(select.tagName).toBe('SELECT');
    expect(screen.getByRole('option', { name: 'Registrar prenda' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Levantar gravamen' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /Continuar sin gestionar/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sí' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'No' })).not.toBeInTheDocument();
  });

  it('en traspaso, al elegir registrar muestra acreedor', async () => {
    render(
      <PrendaForm
        instanceId="abc"
        modalidad="traspaso"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        documentRequired={false}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('¿Al vehículo se le asociará una prenda?'), {
      target: { value: 'registrar' },
    });

    expect(screen.getByLabelText('Acreedor (beneficiario)')).toBeInTheDocument();
    expect(screen.getByLabelText(/documento del acreedor/i)).toBeInTheDocument();
  });

  it('en traspaso, con omitir no muestra acreedor ni carga de certificado', async () => {
    render(
      <PrendaForm
        instanceId="abc"
        modalidad="traspaso"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        documentRequired={false}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('¿Al vehículo se le asociará una prenda?'), {
      target: { value: 'omitir' },
    });

    expect(screen.queryByLabelText('Acreedor (beneficiario)')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Documento de soporte de prenda')).not.toBeInTheDocument();
  });

  it('embeddedInWizard oculta el botón de guardado; save() vía ref persiste', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="abc" embeddedInWizard />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    expect(screen.queryByRole('button', { name: /Guardar decisión de prenda/i })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));
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

  it('pickRuntAcreedor prioriza el primer ítem con acreedor/documento', () => {
    const pick = pickRuntAcreedor({
      nombreAcreedor: 'Resumen',
      items: [
        { acreedor: 'BANCO ITEM', documentoAcreedor: '800111222' },
      ],
    });
    expect(pick).toEqual({ nombre: 'BANCO ITEM', documento: '800111222' });
  });
});

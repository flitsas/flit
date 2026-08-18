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

    expect(screen.queryByLabelText('Acreedor (beneficiario)', { exact: false })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Documento de soporte de prenda')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Adjuntar/i })).not.toBeInTheDocument();
  });

  it('con "registrar" pide acreedor y muestra el contenedor de carga del certificado', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));

    expect(screen.getByLabelText('Acreedor (beneficiario)', { exact: false })).toBeInTheDocument();
    expect(screen.getByLabelText('Documento de soporte de prenda')).toBeInTheDocument();
    expect(screen.getByText('Certificado / registro de prenda')).toBeInTheDocument();
    expect(screen.getByText(/Obligatorio/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Adjuntar' })).toBeInTheDocument();
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
    expect(screen.getByLabelText('Acreedor (beneficiario)', { exact: false })).toHaveValue('BANCO RUNT SA');
    expect(screen.getByLabelText('NIT / documento del acreedor', { exact: false })).toHaveValue('900123456');
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

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));
    fireEvent.change(screen.getByLabelText('Acreedor (beneficiario)', { exact: false }), {
      target: { value: 'Banco XYZ' },
    });
    // HU #11594 — "registrar" constituye gravamen: el documento del acreedor también es obligatorio.
    fireEvent.change(screen.getByLabelText('NIT / documento del acreedor', { exact: false }), {
      target: { value: '900123456' },
    });

    const ok = await ref.current!.save();
    expect(ok).toBe(true);
    await waitFor(() =>
      expect(client.putPrenda).toHaveBeenCalledWith('abc', {
        decision: 'registrar',
        acreedorNombre: 'Banco XYZ',
        acreedorDocumento: '900123456',
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

// HU #11594 — acreedor obligatorio en el paso de prenda (client-side, gate ya existe en backend
// desde HU #11591/#11592).
describe('PrendaForm — validación de acreedor obligatorio (HU #11594)', () => {
  beforeEach(() => {
    client.getPrenda.mockClear();
    client.getInstance.mockClear();
    client.putPrenda.mockClear();
    client.getInstance.mockResolvedValue({ fieldValues: [] } as never);
  });

  it('AC1 — con "solicitar" y acreedor diligenciado, guarda y el wizard avanza', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(
      <PrendaForm
        ref={ref}
        instanceId="abc"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        embeddedInWizard
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('radio', { name: 'Solicitar constitución de prenda' }));
    fireEvent.change(screen.getByLabelText('Acreedor (beneficiario)', { exact: false }), {
      target: { value: 'Banco ABC' },
    });
    fireEvent.change(screen.getByLabelText('NIT / documento del acreedor', { exact: false }), {
      target: { value: '900555666' },
    });

    const ok = await ref.current!.save();
    expect(ok).toBe(true);
    expect(client.putPrenda).toHaveBeenCalledWith('abc', {
      decision: 'solicitar',
      acreedorNombre: 'Banco ABC',
      acreedorDocumento: '900555666',
    });
  });

  it('AC2 — con "registrar" y acreedor vacío, no guarda y marca los inputs como requeridos', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="abc" embeddedInWizard />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));

    const ok = await ref.current!.save();
    expect(ok).toBe(false);
    expect(client.putPrenda).not.toHaveBeenCalled();

    const nombreInput = screen.getByLabelText('Acreedor (beneficiario)', { exact: false });
    const docInput = screen.getByLabelText('NIT / documento del acreedor', { exact: false });
    expect(nombreInput).toBeRequired();
    expect(docInput).toBeRequired();
    await waitFor(() => expect(nombreInput).toHaveAttribute('aria-invalid', 'true'));
    expect(docInput).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText(/Completa los datos del acreedor/i)).toBeInTheDocument();
    expect(screen.getByText('Ingresa el nombre del acreedor.')).toBeInTheDocument();
    expect(screen.getByText('Ingresa el documento del acreedor.')).toBeInTheDocument();
  });

  it('AC3 — con "sin_prenda" no exige acreedor y el wizard avanza', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="abc" embeddedInWizard />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Sin prenda' }));

    const ok = await ref.current!.save();
    expect(ok).toBe(true);
    expect(client.putPrenda).toHaveBeenCalledWith('abc', {
      decision: 'sin_prenda',
      acreedorNombre: null,
      acreedorDocumento: null,
    });
  });

  it('AC3b — con "omitir" (traspaso) no exige acreedor y el wizard avanza', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(
      <PrendaForm
        ref={ref}
        instanceId="abc"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
        embeddedInWizard
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(
      screen.getByRole('radio', { name: 'Continuar sin gestionar (asumo el riesgo)' }),
    );

    const ok = await ref.current!.save();
    expect(ok).toBe(true);
    expect(client.putPrenda).toHaveBeenCalledWith('abc', {
      decision: 'omitir',
      acreedorNombre: null,
      acreedorDocumento: null,
    });
  });

  it('AC4 — sin decisión seleccionada, el wizard NO avanza y pide elegir una decisión', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="abc" embeddedInWizard />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    const ok = await ref.current!.save();
    expect(ok).toBe(false);
    expect(client.putPrenda).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(screen.getByText(/Selecciona una decisión de prenda/i)).toBeInTheDocument(),
    );
  });

  it('corrige el error de campo al escribir en el input tras un intento fallido', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="abc" embeddedInWizard />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));
    expect(await ref.current!.save()).toBe(false);

    const nombreInput = screen.getByLabelText('Acreedor (beneficiario)', { exact: false });
    await waitFor(() => expect(nombreInput).toHaveAttribute('aria-invalid', 'true'));
    fireEvent.change(nombreInput, { target: { value: 'Banco XYZ' } });
    expect(nombreInput).toHaveAttribute('aria-invalid', 'false');
    expect(screen.queryByText('Ingresa el nombre del acreedor.')).not.toBeInTheDocument();
  });

  it('defensa en profundidad: traduce el código prenda_acreedor_requerido del backend', async () => {
    client.putPrenda.mockRejectedValueOnce(new Error('prenda_acreedor_requerido'));
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="abc" embeddedInWizard />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Registrar prenda' }));
    fireEvent.change(screen.getByLabelText('Acreedor (beneficiario)', { exact: false }), {
      target: { value: 'Banco XYZ' },
    });
    fireEvent.change(screen.getByLabelText('NIT / documento del acreedor', { exact: false }), {
      target: { value: '900123456' },
    });

    const ok = await ref.current!.save();
    expect(ok).toBe(false);
    await waitFor(() =>
      expect(
        screen.getByText(/requiere los datos del acreedor \(nombre y documento\)/i),
      ).toBeInTheDocument(),
    );
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

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
    expect(screen.getByLabelText('Acreedor (beneficiario)', { exact: false })).toHaveValue('BANCO RUNT SA');
    expect(screen.getByLabelText('NIT / documento del acreedor', { exact: false })).toHaveValue('900123456');
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
        levantamientoEntidad: null,
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
      levantamientoEntidad: null,
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
      levantamientoEntidad: null,
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
      levantamientoEntidad: null,
    });
  });

  // ── Default «Sin prenda» en matrícula inicial ─────────────────────────────────────
  // El control arrancaba sin ninguna opción marcada cuando el RUNT no reportaba gravamen, y de ahí
  // salía un `prenda_decision_requerida` al Preparar si el gestor nunca abría la sección.

  it('matrícula sin gravamen reportado — preselecciona «Sin prenda» y el wizard avanza', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="abc" embeddedInWizard />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Sin prenda' })).toHaveAttribute(
        'aria-pressed',
        'true',
      );
    });

    // La preselección se persiste sin que el gestor toque nada, al guardar el paso.
    const ok = await ref.current!.save();
    expect(ok).toBe(true);
    expect(client.putPrenda).toHaveBeenCalledWith('abc', {
      decision: 'sin_prenda',
      acreedorNombre: null,
      acreedorDocumento: null,
      levantamientoEntidad: null,
    });
  });

  it('matrícula CON gravamen reportado — el default no le gana a la sugerencia del RUNT', async () => {
    render(<PrendaForm instanceId="abc" runtHasGravamen />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Registrar prenda' })).toHaveAttribute(
        'aria-pressed',
        'true',
      );
    });
    expect(screen.getByRole('button', { name: 'Sin prenda' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  it('matrícula con decisión ya guardada — el default no la pisa', async () => {
    client.getPrenda.mockResolvedValueOnce({
      id: '1',
      decision: 'registrar',
      estado: 'vigente',
      acreedorNombre: 'Banco XYZ',
      acreedorDocumento: '900111222',
      levantamientoEntidad: null,
      createdAt: '2026-07-07T00:00:00Z',
    } as never);

    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Registrar prenda' })).toHaveAttribute(
        'aria-pressed',
        'true',
      );
    });
  });

  it('traspaso — NO preselecciona nada (queda como estaba)', async () => {
    // Alcance deliberado: en traspaso el vehículo tiene historial, así que «el RUNT no reportó
    // gravamen» puede significar que la consulta no lo trajo. Su lista tampoco ofrece `sin_prenda`.
    render(
      <PrendaForm
        instanceId="abc"
        modalidad="traspaso"
        decisions={traspasoDecisions(false)}
        embeddedInWizard
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    // En traspaso el control es un <select>: sin decisión se queda en la opción vacía.
    const select = screen.getByLabelText('¿Al vehículo se le asociará una prenda?');
    expect((select as HTMLSelectElement).value).toBe('');
  });

  it('AC4 — sin decisión seleccionada, el wizard NO avanza y pide elegir una decisión', async () => {
    // El guard se ejercita en TRASPASO: desde el default de «Sin prenda», la matrícula ya no puede
    // llegar a `save()` sin decisión (ver el bloque de abajo). El guard sigue vivo para el resto.
    const ref = createRef<PrendaFormHandle>();
    render(
      <PrendaForm
        ref={ref}
        instanceId="abc"
        modalidad="traspaso"
        decisions={traspasoDecisions(false)}
        embeddedInWizard
      />,
    );
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

/**
 * ADR-0050 — tipos prendarios de la familia OTROS: la decisión NO se elige, la eligió quien eligió
 * el trámite. En un LEVANTAMIENTO_PRENDA la única acción posible es levantar, y ofrecer un control
 * con una sola opción es una pregunta cuya respuesta ya está dada — además de un paso más que dar
 * para poder continuar.
 */
describe('PrendaForm — decisión fija del tipo (familia OTROS)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    client.getPrenda.mockResolvedValue(null);
    client.getInstance.mockResolvedValue({ fieldValues: [] } as never);
    client.getAttachments.mockResolvedValue([]);
    client.putPrenda.mockResolvedValue({
      id: '1',
      decision: 'levantar',
      estado: 'vigente',
      acreedorNombre: null,
      acreedorDocumento: null,
      createdAt: '2026-07-07T00:00:00Z',
    } as never);
  });

  it('afirma el trámite en vez de pintar un selector de una sola opción', async () => {
    render(<PrendaForm instanceId="i1" decisions={['levantar']} embeddedInWizard />);

    expect(await screen.findByText(/Este trámite es/)).toBeInTheDocument();
    expect(
      screen.queryByText('¿Al vehículo se le asociará una prenda?'),
    ).not.toBeInTheDocument();
  });

  it('no ofrece omitir ni «sin prenda»: sería ofrecer no hacer el trámite que se radica', async () => {
    render(<PrendaForm instanceId="i1" decisions={['levantar']} embeddedInWizard />);
    await screen.findByText(/Este trámite es/);

    expect(screen.queryByText('Continuar sin gestionar (asumo el riesgo)')).not.toBeInTheDocument();
    expect(screen.queryByText('Sin prenda')).not.toBeInTheDocument();
    expect(screen.queryByText('Registrar prenda')).not.toBeInTheDocument();
  });

  it('guarda la decisión del tipo sin que el gestor tenga que marcarla', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(<PrendaForm ref={ref} instanceId="i1" decisions={['levantar']} embeddedInWizard />);
    await screen.findByText(/Este trámite es/);

    await waitFor(async () => {
      expect(await ref.current!.save()).toBe(true);
    });
    expect(client.putPrenda).toHaveBeenCalledWith(
      'i1',
      expect.objectContaining({ decision: 'levantar' }),
    );
  });

  it('con varias decisiones ofrecidas sigue habiendo control de elección (regresión)', async () => {
    render(<PrendaForm instanceId="i1" decisions={['registrar', 'sin_prenda']} embeddedInWizard />);

    expect(
      await screen.findByText('¿Al vehículo se le asociará una prenda?'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Este trámite es/)).not.toBeInTheDocument();
  });
});

/**
 * Trámite de levantamiento de prenda: su FUR declara en el párrafo 23 ANTE QUIÉN se levantó, y en el
 * numeral 20 «A FAVOR DE» al acreedor. Por eso aquí se captura la entidad y —a diferencia del
 * traspaso— el acreedor SÍ se persiste: antes se mostraba precargado del RUNT pero viajaba como
 * null, y el formulario salía afirmando el levantamiento sin decir de quién.
 */
describe('PrendaForm — levantamiento como trámite propio', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    client.getPrenda.mockResolvedValue(null);
    client.getInstance.mockResolvedValue({ fieldValues: [] } as never);
    client.getAttachments.mockResolvedValue([]);
    client.putPrenda.mockResolvedValue({
      id: '1',
      decision: 'levantar',
      estado: 'vigente',
      acreedorNombre: null,
      acreedorDocumento: null,
      levantamientoEntidad: null,
      createdAt: '2026-08-25T00:00:00Z',
    } as never);
  });

  it('captura la entidad ante la que se levantó y persiste también el acreedor', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(
      <PrendaForm
        ref={ref}
        instanceId="abc"
        embeddedInWizard
        decisions={['levantar']}
        exigeEntidadLevantamiento
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    const entidad = await screen.findByLabelText('Entidad ante la que se levantó', {
      exact: false,
    });
    fireEvent.change(entidad, { target: { value: 'Notaría 15 de Medellín' } });

    const ok = await ref.current!.save();

    expect(ok).toBe(true);
    expect(client.putPrenda).toHaveBeenCalledWith('abc', {
      decision: 'levantar',
      acreedorNombre: null,
      acreedorDocumento: null,
      levantamientoEntidad: 'Notaría 15 de Medellín',
    });
  });

  it('sin entidad no guarda y explica por qué', async () => {
    const ref = createRef<PrendaFormHandle>();
    render(
      <PrendaForm
        ref={ref}
        instanceId="abc"
        embeddedInWizard
        decisions={['levantar']}
        exigeEntidadLevantamiento
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    await screen.findByLabelText('Entidad ante la que se levantó', { exact: false });

    const ok = await ref.current!.save();

    expect(ok).toBe(false);
    expect(client.putPrenda).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(screen.getByText(/ante qué entidad se levantó la prenda/i)).toBeInTheDocument(),
    );
  });

  it('en traspaso NO se pide la entidad: ese flujo conserva su literal', async () => {
    render(
      <PrendaForm
        instanceId="abc"
        embeddedInWizard
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    expect(
      screen.queryByLabelText('Entidad ante la que se levantó', { exact: false }),
    ).not.toBeInTheDocument();
  });
});

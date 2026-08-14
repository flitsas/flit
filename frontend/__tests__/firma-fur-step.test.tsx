import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  Participant,
  ProcedureAttachment,
  Signature,
} from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listFirmas: vi.fn(),
  solicitarFirma: vi.fn(),
  simularFirma: vi.fn(),
  listParticipantes: vi.fn(),
  invitarParticipante: vi.fn(),
  reinvitarParticipante: vi.fn(),
  generarFur: vi.fn(),
  generarImpronta: vi.fn(),
  generarConsolidado: vi.fn(),
  getFurTemplateFormat: vi.fn(),
  getAttachments: vi.fn(),
  getInstance: vi.fn(),
  listBiometric: vi.fn(),
  listBiometricExpediente: vi.fn(),
  getBiometricState: vi.fn(),
  patchFieldValues: vi.fn(),
  submitInstance: vi.fn(),
  downloadAttachment: vi.fn(),
  listTransitOffices: vi.fn(),
  runRnmc: vi.fn(),
  getPrenda: vi.fn(),
  getFirmaPosterior: vi.fn(),
  marcarFirmaPosterior: vi.fn(),
  getActors: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listFirmas: mocks.listFirmas,
    solicitarFirma: mocks.solicitarFirma,
    simularFirma: mocks.simularFirma,
    listParticipantes: mocks.listParticipantes,
    invitarParticipante: mocks.invitarParticipante,
    reinvitarParticipante: mocks.reinvitarParticipante,
    generarFur: mocks.generarFur,
    generarImpronta: mocks.generarImpronta,
    generarConsolidado: mocks.generarConsolidado,
    getFurTemplateFormat: mocks.getFurTemplateFormat,
    getAttachments: mocks.getAttachments,
    getInstance: mocks.getInstance,
    listBiometric: mocks.listBiometric,
    listBiometricExpediente: mocks.listBiometricExpediente,
    getBiometricState: mocks.getBiometricState,
    patchFieldValues: mocks.patchFieldValues,
    submitInstance: mocks.submitInstance,
    downloadAttachment: mocks.downloadAttachment,
    listTransitOffices: mocks.listTransitOffices,
    runRnmc: mocks.runRnmc,
    getPrenda: mocks.getPrenda,
    getFirmaPosterior: mocks.getFirmaPosterior,
    marcarFirmaPosterior: mocks.marcarFirmaPosterior,
    getActors: mocks.getActors,
  },
}));

import { FirmaFurStep } from '@/components/operacion/FirmaFurStep';

const INSTANCE = 'inst-1';

/** Las tres casillas base de «Expediente consolidado» (punto 6, rediseño Step5). Los tests de este
 * archivo no declaran transformaciones, así que no hay cuarta casilla. */
const CONFIRMACIONES_BASE = (organismo = 'Secretaría Distrital de Movilidad de Bogotá') => [
  'Confirmo que los datos del vehículo coinciden con la factura de venta.',
  'Confirmo que el comprador autorizó el tratamiento de sus datos personales.',
  `Confirmo la radicación ante ${organismo}.`,
];

const FIRMA_ENVIADA: Signature = {
  id: 'sig-1',
  parte: 'comprador',
  docTipo: 'compraventa',
  proveedor: 'mock',
  estado: 'enviada',
  envelopeId: 'env-1',
  signUrl: 'https://mock/sign/sig-1',
  firmada: false,
  solicitadoAt: '2026-06-19T00:00:00Z',
  firmadoAt: null,
};

const PARTICIPANT: Participant = {
  id: 'part-1',
  rol: 'comprador',
  nombre: 'Ana Comprador',
  email: 'ana@example.com',
  telefono: null,
  whatsappOptIn: false,
  consentDado: false,
  consentVersion: null,
  consent1581At: null,
  expiresAt: '2026-06-20T00:00:00Z',
  completedAt: null,
  lastReminderAt: null,
  expirado: false,
  completado: false,
};

const FUR_DOC: ProcedureAttachment = {
  id: 'att-fur',
  tipo: 'fur',
  filename: 'fur.txt',
  mimetype: 'text/plain',
  sizeBytes: 100,
  sha256: 'abc123',
  source: 'system',
  uploadedAt: '2026-06-19T00:00:00Z',
};

const IMPRONTA_DOC: ProcedureAttachment = {
  id: 'att-impronta',
  tipo: 'impronta',
  filename: 'impronta.pdf',
  mimetype: 'application/pdf',
  sizeBytes: 200,
  sha256: 'def456',
  source: 'user',
  uploadedAt: '2026-06-19T00:00:00Z',
};

// Detalle con organismo YA seleccionado: el modal no se auto-abre y no
// interfiere con las aserciones de las regiones/botones existentes.
const INSTANCE_DETAIL = {
  id: INSTANCE,
  referenceNumber: 'REF-1',
  status: 'borrador' as const,
  procedureTypeId: 'pt-1',
  tenantId: 't-1',
  createdAt: '2026-06-19T00:00:00Z',
  submittedAt: null,
  completedAt: null,
  fieldValues: [
    { formFieldId: null, fieldKey: 'transit_office_code', valueText: '11001', valueJson: null, source: 'runt' },
    { formFieldId: null, fieldKey: 'transit_office_name', valueText: 'Secretaría Distrital de Movilidad de Bogotá', valueJson: null, source: 'runt' },
    { formFieldId: null, fieldKey: 'transit_office_city', valueText: 'Bogotá D.C.', valueJson: null, source: 'runt' },
  ],
  statusHistory: [],
  actors: [
    {
      actorType: 'vendedor',
      documentType: 'CC',
      documentNumber: '1001',
      fullName: 'Vendedor Demo',
      email: 'vendedor@example.com',
    },
    {
      actorType: 'comprador',
      documentType: 'CC',
      documentNumber: '2002',
      fullName: 'Comprador Demo',
      email: 'comprador@example.com',
    },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.getAttachments.mockResolvedValue([]);
  mocks.getInstance.mockResolvedValue(INSTANCE_DETAIL);
  mocks.listBiometric.mockResolvedValue([]);
  mocks.listBiometricExpediente.mockResolvedValue({ validations: [], provider: 'mock', firmaBaulPartes: [] });
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock', firmaBaulPartes: [] });
  mocks.patchFieldValues.mockResolvedValue(INSTANCE_DETAIL);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.runRnmc.mockResolvedValue([]);
  mocks.getPrenda.mockResolvedValue(null);
  mocks.getActors.mockResolvedValue([
    {
      rol: 'vendedor',
      tipoDocumento: 'CC',
      numeroDocumento: '1001',
      nombreCompleto: 'Vendedor Demo',
      email: 'vendedor@example.com',
      telefono: '3001112233',
      direccion: 'Calle 1 # 2-3',
      ciudad: 'Medellín',
    },
    {
      rol: 'comprador',
      tipoDocumento: 'CC',
      numeroDocumento: '2002',
      nombreCompleto: 'Comprador Demo',
      email: 'comprador@example.com',
      telefono: '3004445566',
      direccion: 'Carrera 10 # 20-30',
      ciudad: 'Bogotá',
    },
  ]);
  mocks.getFirmaPosterior.mockResolvedValue({
    aplica: false,
    marcado: false,
    representanteNombre: null,
    marcadoAt: null,
  });
  // HU #11052 — el consolidado es el único disparador de generación del paso FUR.
  mocks.generarConsolidado.mockResolvedValue({
    attachmentId: 'att-consolidado',
    tipo: 'consolidado',
    filename: 'consolidado.pdf',
    sha256: 'cns123',
    regenerado: true,
    incompleto: false,
    documentosFaltantes: [],
  });
  mocks.getFurTemplateFormat.mockResolvedValue({ format: 'AUTOMOTOR', vehicleClass: 'AUTOMOVIL' });
  mocks.submitInstance.mockResolvedValue({ id: INSTANCE, status: 'entregado' });
  mocks.downloadAttachment.mockResolvedValue({
    blob: new Blob(['x'], { type: 'text/plain' }),
    filename: 'fur.txt',
    mimetype: 'text/plain',
  });
  mocks.solicitarFirma.mockResolvedValue(FIRMA_ENVIADA);
  mocks.simularFirma.mockResolvedValue({ id: 'sig-1', estado: 'firmada', pdfPath: 'p', sha256: 's' });
  mocks.invitarParticipante.mockResolvedValue({
    participant: PARTICIPANT,
    token: 'raw-token-xyz',
    magicLinkPath: '/portal/raw-token-xyz',
  });
  mocks.reinvitarParticipante.mockResolvedValue({
    participant: PARTICIPANT,
    token: 'raw-token-new',
    magicLinkPath: '/portal/raw-token-new',
  });
  mocks.generarFur.mockResolvedValue({
    documents: [{ attachmentId: 'att-fur', tipo: 'fur', filename: 'fur.txt', sha256: 'abc123' }],
  });
  mocks.generarImpronta.mockResolvedValue({
    attachmentId: 'att-impronta',
    filename: 'impronta.pdf',
    sha256: 'def456',
    radicado: 'IMPR-TEST0001',
    hash: 'hash-abc',
  });
});

describe('FirmaFurStep — firma solo en traspaso', () => {
  it('matrícula NO muestra la sección de firma', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(screen.queryByRole('region', { name: 'Firma de la compraventa' })).not.toBeInTheDocument();
    expect(screen.queryByRole('group', { name: /Firma compraventa/i })).not.toBeInTheDocument();
  });

  it('traspaso tampoco muestra firma de compraventa en el resumen', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(screen.queryByRole('group', { name: 'Firma compraventa Comprador' })).not.toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Firma compraventa Vendedor' })).not.toBeInTheDocument();
  });
});

describe('FirmaFurStep — solicitar y simular firma', () => {
  // La UI de firma de compraventa se retiró del resumen (ambos actores).
  it('no muestra tarjeta de firma de compraventa en el resumen', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(screen.queryByRole('group', { name: /Firma compraventa/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Solicitar firma' })).toBeNull();
    expect(mocks.solicitarFirma).not.toHaveBeenCalled();
  });

  it('no ofrece simular firma de compraventa desde el resumen', async () => {
    mocks.listFirmas.mockResolvedValue([FIRMA_ENVIADA]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(screen.queryByRole('button', { name: 'Simular firma (DEV)' })).toBeNull();
    expect(mocks.simularFirma).not.toHaveBeenCalled();
  });
});

describe('FirmaFurStep — participantes portal ocultos (Feature #11211)', () => {
  it('no renderiza Participantes del portal en el camino feliz', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(
      screen.queryByRole('region', { name: 'Participantes del portal' }),
    ).not.toBeInTheDocument();
    expect(mocks.listParticipantes).not.toHaveBeenCalled();
  });
});

// Feature #11066 — la impronta se pre-genera al entrar al paso (best-effort). Sin sección dedicada:
// aparece en Documentos del expediente cuando existe.
describe('FirmaFurStep — impronta (Feature #11066)', () => {
  it('sin impronta: no muestra sección dedicada ni botón Generar Improntas', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(screen.queryByRole('region', { name: 'Impronta de motor y chasis' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Generar Improntas/i })).not.toBeInTheDocument();
  });

  it('con impronta existente la lista en Documentos del expediente', async () => {
    mocks.getAttachments.mockResolvedValue([IMPRONTA_DOC]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const docs = await screen.findByRole('list', { name: 'Documentos del expediente (visor)' });
    expect(within(docs).getByText(/impronta\.pdf/i)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Impronta de motor y chasis' })).not.toBeInTheDocument();
  });
});

describe('FirmaFurStep — FUR / consolidado (Feature #11066 + HU #11052)', () => {
  it('sin consolidado: no genera FUR a mano; pre-genera paquete+impronta al entrar', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Expediente digital' });

    expect(screen.queryByRole('button', { name: /Generar FUR/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Re-generar FUR/i })).not.toBeInTheDocument();
    expect(
      await screen.findByRole('button', { name: 'Ver expediente consolidado (PDF)' }),
    ).toBeInTheDocument();

    await waitFor(() => {
      expect(mocks.generarFur).toHaveBeenCalledWith(INSTANCE);
      expect(mocks.generarImpronta).toHaveBeenCalledWith(INSTANCE);
    });
  });

  // Punto 6 del rediseño (captura Step5) — las casillas nacen desmarcadas, pero NO gatean el botón:
  // ver el PDF no es un acto que haya que confirmar. Lo que sí deben condicionar es radicar, y ese
  // gateo vive en el wizard (`Requisitos pendientes antes del envío`), fuera de este paso.
  it('las casillas de confirmación nacen desmarcadas y NO bloquean «Ver expediente consolidado»', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const boton = await screen.findByRole('button', {
      name: 'Ver expediente consolidado (PDF)',
    });
    expect(boton).toBeEnabled();

    for (const texto of CONFIRMACIONES_BASE()) {
      expect(screen.getByLabelText(texto)).not.toBeChecked();
    }

    await user.click(boton);
    await waitFor(() => expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE, undefined, true));
  });

  // Contrato de `onConfirmacionesExpedienteChange`: reporta las confirmaciones SIN marcar, para que
  // el wizard (dueño de `TramiteWizard.tsx`) las sume a sus requisitos pendientes.
  it('reporta las confirmaciones pendientes y las retira al marcarlas', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    const user = userEvent.setup();
    const reportes: string[][] = [];
    render(
      <FirmaFurStep
        instanceId={INSTANCE}
        modalidad="traspaso"
        onConfirmacionesExpedienteChange={(pendientes) => reportes.push(pendientes)}
      />,
    );

    await screen.findByRole('button', { name: 'Ver expediente consolidado (PDF)' });
    await waitFor(() => expect(reportes.at(-1)).toEqual(CONFIRMACIONES_BASE()));

    for (const texto of CONFIRMACIONES_BASE()) {
      await user.click(screen.getByLabelText(texto));
    }
    await waitFor(() => expect(reportes.at(-1)).toEqual([]));
  });

  it('lista el FUR en Documentos del expediente y no vuelve a generarFur', async () => {
    mocks.getAttachments.mockResolvedValue([FUR_DOC]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const docs = await screen.findByRole('list', { name: 'Documentos del expediente (visor)' });
    expect(within(docs).getByText(/fur\.txt/i)).toBeInTheDocument();
    await waitFor(() => expect(mocks.generarImpronta).toHaveBeenCalled());
    expect(mocks.generarFur).not.toHaveBeenCalled();
  });

  it('muestra Fecha del trámite al inicio del resumen como hoy y no editable', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const resumen = await screen.findByRole('region', { name: 'Consolidado del trámite' });
    const fecha = within(resumen).getByLabelText('Fecha del trámite', {
      selector: 'input',
    }) as HTMLInputElement;
    const today = new Date();
    const yyyy = today.getFullYear();
    const mm = String(today.getMonth() + 1).padStart(2, '0');
    const dd = String(today.getDate()).padStart(2, '0');
    expect(fecha.value).toBe(`${yyyy}-${mm}-${dd}`);
    expect(fecha).toBeDisabled();
    expect(fecha).toHaveAttribute('readonly');
    // Debe ir antes del bloque Vehículo (visible al inicio). Vehículo ya no es un acordeón
    // (rediseño: tarjeta siempre abierta), así que se ubica por su región, no por su botón.
    const vehiculoRegion = within(resumen).getByRole('region', { name: /Vehículo/i });
    expect(
      fecha.compareDocumentPosition(vehiculoRegion) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith(
        INSTANCE,
        expect.arrayContaining([
          expect.objectContaining({ fieldKey: 'fur_processing_date', valueText: fecha.value }),
        ]),
      ),
    );
  });

  it('persiste la fecha ANTES de generar el consolidado', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('button', { name: 'Ver expediente consolidado (PDF)' });

    await user.click(screen.getByRole('button', { name: 'Ver expediente consolidado (PDF)' }));

    await waitFor(() => expect(mocks.patchFieldValues).toHaveBeenCalled());
    await waitFor(() =>
      expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE, undefined, true),
    );
    const ordenGuardado = mocks.patchFieldValues.mock.invocationCallOrder[0]!;
    const ordenGenerado = mocks.generarConsolidado.mock.invocationCallOrder[0]!;
    expect(ordenGuardado).toBeLessThan(ordenGenerado);
  });

  it('traduce el rechazo por estado final del backend', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    mocks.generarConsolidado.mockRejectedValue(
      new Error('409 Conflict: generacion_bloqueada_estado_final'),
    );
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('button', { name: 'Ver expediente consolidado (PDF)' });

    await user.click(screen.getByRole('button', { name: 'Ver expediente consolidado (PDF)' }));

    expect(
      await screen.findByText(/su documentación es definitiva y no se regenera/i),
    ).toBeInTheDocument();
  });

  it('avisa si el consolidado reporta un aviso (p.ej. impronta no disponible)', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    mocks.generarConsolidado.mockResolvedValue({
      attachmentId: 'att-consolidado',
      tipo: 'consolidado',
      filename: 'consolidado.pdf',
      sha256: 'cns123',
      regenerado: true,
      incompleto: false,
      documentosFaltantes: [],
      avisosCascada: ['impronta: provider_unavailable'],
    });
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('button', { name: 'Ver expediente consolidado (PDF)' });

    await user.click(screen.getByRole('button', { name: 'Ver expediente consolidado (PDF)' }));

    const aviso = await screen.findByRole('alert');
    expect(aviso).toHaveTextContent(/Expediente consolidado generado/i);
    expect(aviso).toHaveTextContent(/No se pudo generar/i);
    expect(aviso).toHaveTextContent(/proveedor no está disponible/i);
  });

  it('en trámite aprobado no ofrece generar y lo explica', async () => {
    mocks.getAttachments.mockResolvedValue([FUR_DOC]);
    mocks.getInstance.mockResolvedValue({ ...INSTANCE_DETAIL, status: 'aprobado' });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    expect(
      await screen.findByText(/El trámite ya está aprobado: su documentación es definitiva/i),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Generar expediente consolidado/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Re-generar expediente consolidado/i }),
    ).not.toBeInTheDocument();
  });
});

describe('FirmaFurStep — consulta RNMC en el paso final (FEATURE 05)', () => {
  const rnmcCheck = (key: string, status: 'ok' | 'warn', message: string) => ({
    key,
    label: 'Medidas correctivas (Policía)',
    status,
    source: 'verifik_rnmc',
    message,
    action: null,
  });

  it('con RNMC inactivo no consulta ni muestra la sección', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Expediente digital' });
    expect(mocks.runRnmc).not.toHaveBeenCalled();
    expect(screen.queryByText('Consulta RNMC — Medidas correctivas')).not.toBeInTheDocument();
  });

  it('con RNMC activo auto-consulta y muestra comprador y vendedor (traspaso)', async () => {
    mocks.runRnmc.mockResolvedValue([
      rnmcCheck('rnmc_comprador_medidas_correctivas', 'ok', 'Sin medidas correctivas registradas en el RNMC'),
      rnmcCheck('rnmc_vendedor_medidas_correctivas', 'warn', '1 medida correctiva pendiente'),
    ]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" rnmcEnabled />);
    expect(await screen.findByText('Consulta RNMC — Medidas correctivas')).toBeInTheDocument();
    await waitFor(() => expect(mocks.runRnmc).toHaveBeenCalledWith(INSTANCE));
    const section = screen.getByRole('list', { name: 'Resultados RNMC por actor' });
    expect(within(section).getByText(/\(comprador\)/)).toBeInTheDocument();
    expect(within(section).getByText(/\(vendedor\)/)).toBeInTheDocument();
    expect(within(section).getByText('Sin medidas correctivas registradas en el RNMC')).toBeInTheDocument();
    expect(within(section).getByText('1 medida correctiva pendiente')).toBeInTheDocument();
  });

  it('en matrícula solo consulta/muestra el comprador', async () => {
    mocks.runRnmc.mockResolvedValue([
      rnmcCheck('rnmc_comprador_medidas_correctivas', 'ok', 'Sin medidas correctivas registradas en el RNMC'),
    ]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" rnmcEnabled />);
    const section = await screen.findByRole('list', { name: 'Resultados RNMC por actor' });
    expect(within(section).getByText(/\(comprador\)/)).toBeInTheDocument();
    expect(within(section).queryByText(/\(vendedor\)/)).not.toBeInTheDocument();
  });
});

describe('FirmaFurStep — organismo de tránsito', () => {
  it('auto-abre el modal y sugiere el organismo del RUNT cuando no hay selección', async () => {
    mocks.getInstance.mockResolvedValue({ ...INSTANCE_DETAIL, fieldValues: [] });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    expect(
      await screen.findByRole('dialog', { name: 'Seleccionar organismo de tránsito' }),
    ).toBeInTheDocument();
  });

  it('lista solo los OT habilitados de la empresa y persiste el elegido (con id)', async () => {
    mocks.getInstance.mockResolvedValue({ ...INSTANCE_DETAIL, fieldValues: [] });
    // El catálogo del modal proviene del endpoint de OT habilitados de la empresa.
    mocks.listTransitOffices.mockResolvedValue([
      { id: 'aaaaaaaa-0001-4000-8000-000000000003', code: '76001', name: 'Cali — STTMP', cityCode: '76001' },
    ]);
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const dialog = await screen.findByRole('dialog', { name: 'Seleccionar organismo de tránsito' });
    await user.click(await within(dialog).findByRole('button', { name: /Cali/ }));

    // Feature #11066 también puede auto-persistir `fur_processing_date` (hoy); no exigir un solo call.
    await waitFor(() => {
      const otCall = mocks.patchFieldValues.mock.calls.find(([, items]) =>
        (items as { fieldKey: string }[]).some((i) => i.fieldKey === 'transit_office_id'),
      );
      expect(otCall).toBeTruthy();
      const byKey = Object.fromEntries(
        (otCall![1] as { fieldKey: string; valueText: string }[]).map((i) => [i.fieldKey, i.valueText]),
      );
      expect(byKey.transit_office_id).toBe('aaaaaaaa-0001-4000-8000-000000000003');
      expect(byKey.transit_office_code).toBe('76001');
      expect(byKey.transit_office_name).toBe('Cali — STTMP');
    });
  });
});

describe('FirmaFurStep — descarga de documentos', () => {
  it('abre un documento del expediente en nueva pestaña', async () => {
    mocks.getAttachments.mockResolvedValue([FUR_DOC]);
    mocks.downloadAttachment.mockResolvedValue({
      blob: new Blob(['x'], { type: 'text/plain' }),
      filename: 'fur.txt',
      mimetype: 'text/plain',
    });
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:mock');
    globalThis.URL.revokeObjectURL = vi.fn();
    const openSpy = vi.fn(() => ({ focus: vi.fn() }));
    vi.stubGlobal('open', openSpy);
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const docList = await screen.findByRole('list', { name: 'Documentos del expediente (visor)' });
    await user.click(within(docList).getByRole('button', { name: /Descargar fur\.txt/i }));
    await waitFor(() =>
      expect(mocks.downloadAttachment).toHaveBeenCalledWith(
        INSTANCE,
        'att-fur',
        undefined,
        'fur.txt',
      ),
    );
    expect(openSpy).toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
});

describe('FirmaFurStep — sin envío duplicado en el paso', () => {
  it('NO renderiza la sección de envío a tránsito (el submit vive en Finalizar)', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(screen.queryByRole('region', { name: 'Envío a tránsito' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Enviar a tránsito' })).not.toBeInTheDocument();
    expect(mocks.submitInstance).not.toHaveBeenCalled();
  });
});

describe('FirmaFurStep — resumen / expediente', () => {
  it('muestra el resumen del trámite con el estado de la instancia', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const resumen = await screen.findByRole('region', { name: 'Consolidado del trámite' });
    // N 03 — label desde la fuente única lib/tramites/estados.ts.
    expect(within(resumen).getByText('Borrador')).toBeInTheDocument();
  });

  it('en traspaso y matrícula el resumen se rotula "Consolidado del trámite"', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    expect(
      await screen.findByRole('region', { name: 'Consolidado del trámite' }),
    ).toBeInTheDocument();
  });

  it('en traspaso el resumen muestra al vendedor; en matrícula no', async () => {
    mocks.getInstance.mockResolvedValue({
      ...INSTANCE_DETAIL,
      actors: [
        { actorType: 'vendedor', fullName: 'Ana Vendedora', documentType: 'CC', documentNumber: '111' },
        { actorType: 'comprador', fullName: 'Beto Comprador', documentType: 'CC', documentNumber: '222' },
      ],
    });
    const { unmount } = render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const resumenTraspaso = await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(within(resumenTraspaso).getByText(/Ana Vendedora/)).toBeInTheDocument();
    unmount();

    mocks.getInstance.mockResolvedValue({
      ...INSTANCE_DETAIL,
      actors: [
        { actorType: 'comprador', fullName: 'Beto Comprador', documentType: 'CC', documentNumber: '222' },
      ],
    });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const resumenMat = await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(within(resumenMat).queryByText(/Ana Vendedora/)).toBeNull();
    expect(within(resumenMat).getByText(/Beto Comprador/)).toBeInTheDocument();
  });

  it('el resumen muestra transformaciones declaradas y la decisión de prenda', async () => {
    mocks.getInstance.mockResolvedValue({
      ...INSTANCE_DETAIL,
      fieldValues: [
        ...INSTANCE_DETAIL.fieldValues,
        { formFieldId: null, fieldKey: 'vehicle_color_runt', valueText: 'BLANCO', valueJson: null, source: 'runt' },
        { formFieldId: null, fieldKey: 'vehicle_color', valueText: 'NEGRO', valueJson: null, source: 'user' },
        { formFieldId: null, fieldKey: 'cambio_color', valueText: 'true', valueJson: null, source: 'user' },
        { formFieldId: null, fieldKey: 'vehicle_fuel_runt', valueText: 'GASOLINA', valueJson: null, source: 'runt' },
        { formFieldId: null, fieldKey: 'vehicle_fuel', valueText: 'DIESEL', valueJson: null, source: 'user' },
        { formFieldId: null, fieldKey: 'cambio_combustible', valueText: 'true', valueJson: null, source: 'user' },
      ],
    });
    mocks.getPrenda.mockResolvedValue({
      id: 'prenda-1',
      decision: 'registrar',
      estado: 'vigente',
      acreedorNombre: 'Banco Demo',
      acreedorDocumento: '900111222',
      createdAt: '2026-06-19T00:00:00Z',
    });

    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const resumen = await screen.findByRole('region', { name: 'Consolidado del trámite' });
    // Transformaciones/Prenda son desplegables CERRADOS por defecto (rediseño): hay que abrirlos.
    await user.click(within(resumen).getByRole('button', { name: 'Transformaciones' }));
    await user.click(within(resumen).getByRole('button', { name: 'Prenda / gravamen' }));
    const transformaciones = within(resumen).getByLabelText('Transformaciones declaradas');
    expect(within(transformaciones).getByText('Color')).toBeInTheDocument();
    expect(within(transformaciones).getByText('NEGRO')).toBeInTheDocument();
    expect(within(transformaciones).getByText('Combustible')).toBeInTheDocument();
    expect(within(transformaciones).getByText('DIESEL')).toBeInTheDocument();
    expect(within(resumen).getByLabelText('Prenda o gravamen')).toBeInTheDocument();
    expect(within(resumen).getByText('Registrar prenda')).toBeInTheDocument();
    expect(within(resumen).getByText('Banco Demo')).toBeInTheDocument();
    expect(within(resumen).getByText('900111222')).toBeInTheDocument();
    expect(within(resumen).getByText('Certificado / registro de prenda')).toBeInTheDocument();
    expect(within(resumen).getByText('Sin documento cargado')).toBeInTheDocument();
  });

  it('sin transformaciones ni prenda el resumen no muestra esos bloques', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const resumen = await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(within(resumen).queryByLabelText('Transformaciones declaradas')).toBeNull();
    expect(within(resumen).queryByLabelText('Prenda o gravamen')).toBeNull();
  });

  it('el expediente digital muestra documentos; el resumen tiene vehículo', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const resumen = await screen.findByRole('region', { name: 'Consolidado del trámite' });
    const visor = await screen.findByRole('region', { name: 'Expediente digital' });
    // Vehículo ya no es un acordeón (rediseño: tarjeta siempre abierta).
    expect(within(resumen).getByRole('region', { name: 'Vehículo' })).toBeInTheDocument();
    // «Documentos cargados» tampoco es un acordeón (rediseño): tarjeta siempre abierta, sin botón.
    expect(within(visor).getByRole('region', { name: 'Documentos cargados' })).toBeInTheDocument();
    expect(within(visor).queryByRole('button', { name: 'Documentos' })).toBeNull();
    expect(within(visor).getByText('No se han cargado documentos.')).toBeInTheDocument();
    expect(within(visor).queryByRole('button', { name: 'Vehículo' })).toBeNull();
    expect(within(visor).queryByRole('button', { name: 'Validación de identidad' })).toBeNull();
    expect(within(visor).queryByRole('tab')).toBeNull();
  });

  it('en traspaso el resumen tiene vendedor y comprador con biométrica embebida; el expediente no los duplica', async () => {
    mocks.getInstance.mockResolvedValue({
      ...INSTANCE_DETAIL,
      actors: [
        { actorType: 'vendedor', fullName: 'Ana Vendedora', documentType: 'CC', documentNumber: '111' },
        { actorType: 'comprador', fullName: 'Beto Comprador', documentType: 'CC', documentNumber: '222' },
      ],
    });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const resumen = await screen.findByRole('region', { name: 'Consolidado del trámite' });
    const visor = await screen.findByRole('region', { name: 'Expediente digital' });

    // Vendedor/Comprador ya no son acordeones (rediseño: tarjetas siempre abiertas en dos columnas).
    expect(within(resumen).getByRole('region', { name: 'Vendedor' })).toBeInTheDocument();
    expect(within(resumen).getByRole('region', { name: 'Comprador' })).toBeInTheDocument();
    expect(within(resumen).getByText('Ana Vendedora')).toBeInTheDocument();
    expect(within(resumen).getByText('Beto Comprador')).toBeInTheDocument();
    expect(await within(resumen).findByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
    expect(within(resumen).getByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(within(visor).queryByRole('button', { name: 'Vendedor' })).toBeNull();
    expect(within(visor).queryByRole('button', { name: 'Comprador' })).toBeNull();
    expect(within(visor).queryByRole('button', { name: 'Validación de identidad' })).toBeNull();
    expect(within(visor).queryByRole('tab')).toBeNull();
  });
});

describe('FirmaFurStep — OT fijado desde RUNT en traspaso (B11, HU #10659)', () => {
  // OT resuelto por el auto-bind del preflight: nombre + code + city + id.
  const TRASPASO_OT_BOUND = {
    ...INSTANCE_DETAIL,
    fieldValues: [
      { formFieldId: null, fieldKey: 'transit_office_id', valueText: 'aaaaaaaa-0001-4000-8000-000000000009', valueJson: null, source: 'consultation' },
      { formFieldId: null, fieldKey: 'transit_office_code', valueText: '11001', valueJson: null, source: 'consultation' },
      { formFieldId: null, fieldKey: 'transit_office_name', valueText: 'Secretaría Distrital de Movilidad de Bogotá', valueJson: null, source: 'consultation' },
      { formFieldId: null, fieldKey: 'transit_office_city', valueText: 'Bogotá D.C.', valueJson: null, source: 'consultation' },
    ],
  };

  it('traspaso con OT resuelto: no muestra organismo en el resumen ni abre el modal', async () => {
    mocks.getInstance.mockResolvedValue(TRASPASO_OT_BOUND);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    expect(await screen.findByLabelText('Consolidado del trámite')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Organismo de tránsito' })).not.toBeInTheDocument();
    expect(screen.queryByText(/no puede modificarse en un traspaso/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cambiar|Seleccionar/ })).not.toBeInTheDocument();
    expect(mocks.listTransitOffices).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog', { name: 'Seleccionar organismo de tránsito' })).not.toBeInTheDocument();
  });

  it('traspaso con OT del RUNT no habilitado: tampoco muestra organismo ni selector', async () => {
    mocks.getInstance.mockResolvedValue({
      ...INSTANCE_DETAIL,
      fieldValues: [
        { formFieldId: null, fieldKey: 'transit_office_name', valueText: 'OT NO HABILITADO', valueJson: null, source: 'consultation' },
      ],
    });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    expect(await screen.findByLabelText('Consolidado del trámite')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Organismo de tránsito' })).not.toBeInTheDocument();
    expect(screen.queryByText(/no está habilitado para tu empresa/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cambiar|Seleccionar/ })).not.toBeInTheDocument();
  });

  it('matrícula: el resumen tampoco muestra la sección de organismo', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    expect(await screen.findByLabelText('Consolidado del trámite')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Organismo de tránsito' })).not.toBeInTheDocument();
    expect(screen.queryByText(/no puede modificarse en un traspaso/)).not.toBeInTheDocument();
  });
});

describe('FirmaFurStep — la secretaría elegida en el paso 1 (HU #11199, AC4)', () => {
  /** Trámite creado con el organismo ya elegido en el primer paso: trae la marca del origen. */
  const MATRICULA_OT_PASO_1 = {
    ...INSTANCE_DETAIL,
    fieldValues: [
      { formFieldId: null, fieldKey: 'transit_office_id', valueText: 'aaaaaaaa-0001-4000-8000-000000000010', valueJson: null, source: 'user' },
      { formFieldId: null, fieldKey: 'transit_office_code', valueText: '05001000', valueJson: null, source: 'user' },
      { formFieldId: null, fieldKey: 'transit_office_name', valueText: 'Secretaría de Movilidad de Medellín', valueJson: null, source: 'user' },
      { formFieldId: null, fieldKey: 'transit_office_city', valueText: '05001', valueJson: null, source: 'user' },
      { formFieldId: null, fieldKey: 'transit_office_origen', valueText: 'paso_1', valueJson: null, source: 'user' },
    ],
  };

  it('AC4: el paso del FUR no vuelve a mostrar el organismo si ya se eligió en el paso 1', async () => {
    mocks.getInstance.mockResolvedValue(MATRICULA_OT_PASO_1);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    // Esperar a que el paso cargue (resumen / expediente).
    expect(await screen.findByLabelText('Consolidado del trámite')).toBeInTheDocument();
    expect(screen.queryByText(/Lo elegiste al comenzar el trámite/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cambiar|Seleccionar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Seleccionar organismo de tránsito' })).not.toBeInTheDocument();
    expect(mocks.listTransitOffices).not.toHaveBeenCalled();
  });

  it('D8: un borrador anterior al cambio (sin la marca) conserva el selector del FUR', async () => {
    // Convivencia: no hay migración de borradores. Sin `transit_office_origen` el paso se comporta
    // exactamente como antes, incluido el modal que se auto-abre cuando aún no hay organismo.
    mocks.getInstance.mockResolvedValue({ ...INSTANCE_DETAIL, fieldValues: [] });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(
      await screen.findByRole('dialog', { name: 'Seleccionar organismo de tránsito' }),
    ).toBeInTheDocument();
  });
});

describe('FirmaFurStep — firma no bloqueante en traspaso (B12, HU #10661)', () => {
  it('traspaso: el resumen no muestra firma de compraventa en Comprador/Vendedor', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const vendedor = await screen.findByRole('region', { name: 'Vendedor' });
    const comprador = await screen.findByRole('region', { name: 'Comprador' });
    expect(within(vendedor).queryByRole('group', { name: 'Firma compraventa Vendedor' })).toBeNull();
    expect(within(comprador).queryByRole('group', { name: 'Firma compraventa Comprador' })).toBeNull();
    expect(screen.queryByRole('region', { name: 'Firma de la compraventa' })).not.toBeInTheDocument();
  });

  it('traspaso: muestra teléfono, dirección y ciudad del actor', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const vendedor = await screen.findByRole('region', { name: 'Vendedor' });
    expect(within(vendedor).getByText('3001112233')).toBeInTheDocument();
    expect(within(vendedor).getByText('Calle 1 # 2-3')).toBeInTheDocument();
    expect(within(vendedor).getByText('Medellín')).toBeInTheDocument();
  });

  it('traspaso: no solicita firma de compraventa desde el resumen', async () => {
    mocks.listFirmas.mockResolvedValue([FIRMA_ENVIADA]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Consolidado del trámite' });
    expect(screen.queryByRole('button', { name: 'Solicitar firma' })).toBeNull();
    expect(screen.queryByLabelText('Enlace de firma Comprador')).toBeNull();
  });
});

// ── Bug #11145 — el aviso de generación del expediente ──────────────────────
//
// El aviso ("Generando documentos del expediente… No bloquea Preparar") se quedaba girando
// indefinidamente aunque los documentos ya estuvieran generados. FirmaFurStep no lo pinta: reporta
// su estado al shell vía `onPaqueteStatusChange`, y es ese estado el que se quedaba en 'loading'.
describe('FirmaFurStep — estado del paquete de documentos (Bug #11145)', () => {
  const FUR_ATT: ProcedureAttachment = {
    id: 'att-fur',
    tipo: 'fur',
    filename: 'fur.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 10,
    sha256: 'abc',
    uploadedAt: '2026-07-31T00:00:00Z',
    source: 'system',
  };

  it('termina en «listo» y no se queda en «generando»', async () => {
    const estados: string[] = [];
    render(
      <FirmaFurStep
        instanceId={INSTANCE}
        modalidad="traspaso"
        onPaqueteStatusChange={(s) => estados.push(s)}
      />,
    );

    await waitFor(() => expect(estados).toContain('ready'));
    expect(estados.at(-1)).toBe('ready');
  });

  it('con el FUR ya adjunto NUNCA reporta «generando»', async () => {
    // La red de seguridad que pidió el negocio: si el gestor ya ve los documentos, el aviso sobra.
    mocks.getAttachments.mockResolvedValue([FUR_ATT]);
    const estados: string[] = [];

    render(
      <FirmaFurStep
        instanceId={INSTANCE}
        modalidad="traspaso"
        onPaqueteStatusChange={(s) => estados.push(s)}
      />,
    );

    await waitFor(() => expect(estados).toContain('ready'));
    expect(estados).not.toContain('loading');
    // Y no se regenera lo que ya existe.
    expect(mocks.generarFur).not.toHaveBeenCalled();
  });

  it('con la generación aún en vuelo, el aviso se retira en cuanto el FUR aparece', async () => {
    // Causa raíz: el estado solo pasaba a «listo» cuando RESPONDÍA la generación del FUR, y esa
    // petición puede tardar. El documento se materializa antes, así que el gestor veía el FUR en el
    // expediente mientras el aviso seguía girando. Aquí la generación no resuelve nunca y el FUR
    // aparece en el expediente al segundo listado: el aviso debe retirarse igual.
    // La generación NUNCA responde: es el escenario que dejaba el aviso girando para siempre.
    mocks.generarFur.mockImplementation(() => new Promise(() => {}));
    // El expediente empieza vacío —para que la generación llegue a dispararse— y el FUR aparece al
    // cabo de un segundo, como haría el backend mientras la petición sigue abierta. Se controla por
    // TIEMPO y no por número de llamadas: varios componentes del paso listan adjuntos.
    let furEnElExpediente = false;
    mocks.getAttachments.mockImplementation(() =>
      Promise.resolve(furEnElExpediente ? [FUR_ATT] : []),
    );
    setTimeout(() => {
      furEnElExpediente = true;
    }, 1_000);
    const estados: string[] = [];

    render(
      <FirmaFurStep
        instanceId={INSTANCE}
        modalidad="traspaso"
        onPaqueteStatusChange={(s) => estados.push(s)}
      />,
    );

    await waitFor(() => expect(estados).toContain('loading'));
    await waitFor(() => expect(estados.at(-1)).toBe('ready'), { timeout: 10_000 });
    // Se llegó a disparar la generación y aun así el aviso se retiró sin esperar su respuesta.
    expect(mocks.generarFur).toHaveBeenCalled();
  }, 15_000);
});

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
  getAttachments: vi.fn(),
  getInstance: vi.fn(),
  listBiometric: vi.fn(),
  patchFieldValues: vi.fn(),
  submitInstance: vi.fn(),
  downloadAttachment: vi.fn(),
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
    getAttachments: mocks.getAttachments,
    getInstance: mocks.getInstance,
    listBiometric: mocks.listBiometric,
    patchFieldValues: mocks.patchFieldValues,
    submitInstance: mocks.submitInstance,
    downloadAttachment: mocks.downloadAttachment,
  },
}));

import { FirmaFurStep } from '@/components/operacion/FirmaFurStep';

const INSTANCE = 'inst-1';

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

// Detalle con organismo YA seleccionado: el modal no se auto-abre y no
// interfiere con las aserciones de las regiones/botones existentes.
const INSTANCE_DETAIL = {
  id: INSTANCE,
  referenceNumber: 'REF-1',
  status: 'draft' as const,
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
  actors: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.getAttachments.mockResolvedValue([]);
  mocks.getInstance.mockResolvedValue(INSTANCE_DETAIL);
  mocks.listBiometric.mockResolvedValue([]);
  mocks.patchFieldValues.mockResolvedValue(INSTANCE_DETAIL);
  mocks.submitInstance.mockResolvedValue({ id: INSTANCE, status: 'submitted' });
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
});

describe('FirmaFurStep — firma solo en traspaso', () => {
  it('matrícula NO muestra la sección de firma', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByRole('region', { name: 'Participantes del portal' });
    expect(screen.queryByRole('region', { name: 'Firma de la compraventa' })).not.toBeInTheDocument();
  });

  it('traspaso muestra firma de comprador y vendedor', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    expect(await screen.findByRole('group', { name: 'Firma Comprador' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Firma Vendedor' })).toBeInTheDocument();
  });
});

describe('FirmaFurStep — solicitar y simular firma', () => {
  it('solicita la firma de una parte', async () => {
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const card = await screen.findByRole('group', { name: 'Firma Comprador' });
    await user.click(within(card).getByRole('button', { name: 'Solicitar firma' }));
    await waitFor(() => expect(mocks.solicitarFirma).toHaveBeenCalledTimes(1));
    expect(mocks.solicitarFirma.mock.calls[0][1]).toMatchObject({ parte: 'comprador' });
  });

  it('muestra signUrl y permite simular cuando la firma está enviada', async () => {
    mocks.listFirmas.mockResolvedValue([FIRMA_ENVIADA]);
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const card = await screen.findByRole('group', { name: 'Firma Comprador' });
    expect(
      (within(card).getByLabelText('Enlace de firma Comprador') as HTMLInputElement).value,
    ).toContain('https://mock/sign/sig-1');
    await user.click(within(card).getByRole('button', { name: 'Simular firma (DEV)' }));
    await waitFor(() => expect(mocks.simularFirma).toHaveBeenCalledWith(INSTANCE, 'sig-1'));
  });
});

describe('FirmaFurStep — invitar participante', () => {
  it('invita y muestra el magic-link absoluto copiable', async () => {
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Participantes del portal' });
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana Comprador');
    await user.type(screen.getByLabelText('Correo electrónico'), 'ana@example.com');
    await user.click(screen.getByRole('button', { name: 'Invitar participante' }));

    await waitFor(() => expect(mocks.invitarParticipante).toHaveBeenCalledTimes(1));
    const link = (await screen.findByLabelText(
      'Enlace de portal del participante',
    )) as HTMLInputElement;
    expect(link.value).toContain('/portal/raw-token-xyz');
  });

  it('reinvitar muestra el error de cooldown 429', async () => {
    mocks.listParticipantes.mockResolvedValue([PARTICIPANT]);
    mocks.reinvitarParticipante.mockRejectedValue(new Error('429 Too Many Requests'));
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByText(/Ana Comprador/);
    await user.click(screen.getByRole('button', { name: 'Reinvitar' }));
    expect(await screen.findByText(/Espera 24h antes de reenviar/)).toBeInTheDocument();
  });
});

describe('FirmaFurStep — generar FUR', () => {
  it('genera el FUR y lista los documentos', async () => {
    // getAttachments lo consumen Expediente y FUR; tras generar devolvemos el doc.
    mocks.getAttachments.mockResolvedValue([]);
    const onRefresh = vi.fn();
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" onRefresh={onRefresh} />);
    await screen.findByRole('region', { name: 'Generación del FUR' });
    mocks.getAttachments.mockResolvedValue([FUR_DOC]);
    await user.click(screen.getByRole('button', { name: 'Generar FUR / certificado' }));
    await waitFor(() => expect(mocks.generarFur).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(/fur · fur.txt/)).toBeInTheDocument();
    expect(onRefresh).toHaveBeenCalled();
  });

  it('maneja el 409 biometria_gate con un mensaje explicativo', async () => {
    mocks.generarFur.mockRejectedValue(new Error('409 Conflict: biometria_gate'));
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Generación del FUR' });
    await user.click(screen.getByRole('button', { name: 'Generar FUR / certificado' }));
    expect(
      await screen.findByText(/Falta validar identidad/),
    ).toBeInTheDocument();
  });

  it('maneja el 409 organismo_requerido', async () => {
    mocks.generarFur.mockRejectedValue(new Error('409 Conflict: organismo_requerido'));
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Generación del FUR' });
    await user.click(screen.getByRole('button', { name: 'Generar FUR / certificado' }));
    expect(
      await screen.findByText(/Selecciona el organismo de tránsito/),
    ).toBeInTheDocument();
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

  it('persiste el organismo elegido vía patchFieldValues con las 3 keys', async () => {
    mocks.getInstance.mockResolvedValue({ ...INSTANCE_DETAIL, fieldValues: [] });
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const dialog = await screen.findByRole('dialog', { name: 'Seleccionar organismo de tránsito' });
    await user.click(within(dialog).getByRole('button', { name: /Secretaría de Movilidad de Cali/ }));
    await waitFor(() => expect(mocks.patchFieldValues).toHaveBeenCalledTimes(1));
    const items = mocks.patchFieldValues.mock.calls[0][1];
    const keys = items.map((i: { fieldKey: string }) => i.fieldKey);
    expect(keys).toEqual(
      expect.arrayContaining(['transit_office_code', 'transit_office_name', 'transit_office_city']),
    );
  });
});

describe('FirmaFurStep — descarga de documentos', () => {
  it('descarga un documento generado disparando el download del navegador', async () => {
    mocks.getAttachments.mockResolvedValue([FUR_DOC]);
    const clickSpy = vi.fn();
    const origCreate = document.createElement.bind(document);
    vi.spyOn(document, 'createElement').mockImplementation((tag: string) => {
      const el = origCreate(tag) as HTMLElement;
      if (tag === 'a') (el as HTMLAnchorElement).click = clickSpy;
      return el;
    });
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:mock');
    globalThis.URL.revokeObjectURL = vi.fn();
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const docList = await screen.findByRole('list', { name: 'Documentos generados' });
    await user.click(within(docList).getByRole('button', { name: /Descargar/ }));
    await waitFor(() => expect(mocks.downloadAttachment).toHaveBeenCalledWith(INSTANCE, 'att-fur'));
    expect(clickSpy).toHaveBeenCalled();
    vi.mocked(document.createElement).mockRestore();
  });
});

describe('FirmaFurStep — sin envío duplicado en el paso', () => {
  it('NO renderiza la sección de envío a tránsito (el submit vive en Finalizar)', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByRole('region', { name: 'Participantes del portal' });
    expect(screen.queryByRole('region', { name: 'Envío a tránsito' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Enviar a tránsito' })).not.toBeInTheDocument();
    expect(mocks.submitInstance).not.toHaveBeenCalled();
  });
});

describe('FirmaFurStep — resumen / expediente / línea de tiempo', () => {
  it('muestra el resumen de la matrícula con el estado de la instancia', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const resumen = await screen.findByRole('region', { name: 'Resumen de la matrícula' });
    expect(within(resumen).getByText('Borrador (en preparación)')).toBeInTheDocument();
  });

  it('en traspaso el resumen se rotula "Resumen del traspaso" (no matrícula)', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    expect(
      await screen.findByRole('region', { name: 'Resumen del traspaso' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('region', { name: 'Resumen de la matrícula' }),
    ).toBeNull();
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
    const resumen = await screen.findByRole('region', { name: 'Resumen del traspaso' });
    expect(within(resumen).getByText('Vendedor')).toBeInTheDocument();
    expect(within(resumen).getByText('Ana Vendedora')).toBeInTheDocument();
    expect(within(resumen).getByText('Beto Comprador')).toBeInTheDocument();

    // En matrícula (sin vendedor) no aparece la fila Vendedor.
    unmount();
    mocks.getInstance.mockResolvedValue(INSTANCE_DETAIL);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const resumenMat = await screen.findByRole('region', { name: 'Resumen de la matrícula' });
    expect(within(resumenMat).queryByText('Vendedor')).toBeNull();
  });

  it('el expediente digital alterna entre las pestañas Vehículo / Comprador / Documentos', async () => {
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const visor = await screen.findByRole('region', { name: 'Expediente digital' });
    // Pestaña por defecto: Vehículo.
    expect(within(visor).getByText('Especificaciones técnicas')).toBeInTheDocument();
    await user.click(within(visor).getByRole('tab', { name: 'Comprador' }));
    expect(within(visor).getByText('Datos del comprador')).toBeInTheDocument();
    await user.click(within(visor).getByRole('tab', { name: 'Documentos' }));
    expect(within(visor).getByText('No se han cargado documentos.')).toBeInTheDocument();
    // Matrícula tiene una sola parte: no hay pestaña Vendedor.
    expect(within(visor).queryByRole('tab', { name: 'Vendedor' })).toBeNull();
  });

  it('el expediente en traspaso agrega la pestaña Vendedor con sus datos', async () => {
    mocks.getInstance.mockResolvedValue({
      ...INSTANCE_DETAIL,
      actors: [
        { actorType: 'vendedor', fullName: 'Ana Vendedora', documentType: 'CC', documentNumber: '111' },
        { actorType: 'comprador', fullName: 'Beto Comprador', documentType: 'CC', documentNumber: '222' },
      ],
    });
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const visor = await screen.findByRole('region', { name: 'Expediente digital' });

    await user.click(within(visor).getByRole('tab', { name: 'Vendedor' }));
    expect(within(visor).getByText('Datos del vendedor')).toBeInTheDocument();
    expect(within(visor).getByText('Ana Vendedora')).toBeInTheDocument();

    // La pestaña Comprador sigue disponible y separada.
    await user.click(within(visor).getByRole('tab', { name: 'Comprador' }));
    expect(within(visor).getByText('Datos del comprador')).toBeInTheDocument();
    expect(within(visor).getByText('Beto Comprador')).toBeInTheDocument();
  });

  it('la línea de tiempo muestra el vacío cuando no hay historial de estado', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const timeline = await screen.findByRole('region', {
      name: 'Línea de tiempo del expediente',
    });
    expect(within(timeline).getByText('Sin eventos registrados todavía.')).toBeInTheDocument();
  });
});

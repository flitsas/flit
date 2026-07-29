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
  patchFieldValues: vi.fn(),
  submitInstance: vi.fn(),
  downloadAttachment: vi.fn(),
  listTransitOffices: vi.fn(),
  runRnmc: vi.fn(),
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
    patchFieldValues: mocks.patchFieldValues,
    submitInstance: mocks.submitInstance,
    downloadAttachment: mocks.downloadAttachment,
    listTransitOffices: mocks.listTransitOffices,
    runRnmc: mocks.runRnmc,
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
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.runRnmc.mockResolvedValue([]);
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
  // HU #11019 — el botón de solicitar la firma se retiró: el gate ya no la exige (ADR-0028) y pedirla
  // solo añadía un paso que no desbloquea nada. La tarjeta sigue mostrando el estado de la firma.
  it('no ofrece solicitar la firma de la compraventa', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const card = await screen.findByRole('group', { name: 'Firma Comprador' });

    expect(within(card).queryByRole('button', { name: 'Solicitar firma' })).toBeNull();
    expect(within(card).getByText('Firma no solicitada.')).toBeInTheDocument();
    expect(mocks.solicitarFirma).not.toHaveBeenCalled();
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

// HU #11052 — la generación paso a paso desaparece del paso FUR: el expediente consolidado es el
// ÚNICO disparador y produce en cascada lo que falte (FUR e impronta ya lo hacían por backend desde
// las HU #10860 y #11017). La impronta conserva solo su aviso informativo.
describe('FirmaFurStep — impronta (sin generación manual, HU #11052)', () => {
  it('sin adjunto de impronta informa que se genera con el consolidado y NO ofrece botón', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const seccion = await screen.findByRole('region', { name: 'Impronta de motor y chasis' });
    expect(seccion).toHaveTextContent(/se genera automáticamente al generar el expediente consolidado/i);
    expect(screen.queryByRole('button', { name: /Generar Improntas/i })).not.toBeInTheDocument();
  });

  it('con adjunto de impronta la sección no aparece', async () => {
    mocks.getAttachments.mockResolvedValue([IMPRONTA_DOC]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByRole('region', { name: 'Generación del FUR' });
    expect(
      screen.queryByRole('region', { name: 'Impronta de motor y chasis' }),
    ).not.toBeInTheDocument();
  });
});

describe('FirmaFurStep — consolidado como único disparador (HU #11052)', () => {
  it('no ofrece generar el FUR por separado', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Generación del FUR' });

    expect(screen.queryByRole('button', { name: /Generar FUR/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Re-generar FUR/i })).not.toBeInTheDocument();
    expect(
      await screen.findByRole('button', { name: 'Generar expediente consolidado' }),
    ).toBeInTheDocument();
  });

  it('sigue listando y permitiendo descargar los documentos ya generados', async () => {
    mocks.getAttachments.mockResolvedValue([FUR_DOC]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    // El nombre amigable ('FUR') y el filename van en nodos separados; se valida el <p> contenedor.
    const furFilename = await screen.findByText(/· fur\.txt/);
    expect(furFilename.closest('p')).toHaveTextContent('FUR · fur.txt');
  });

  // Hereda el guardado previo que hacía el botón del FUR (HU #10987/#10988): al ser el único
  // disparador, si no persistiera antes, la fecha y las observaciones escritas sin perder el foco no
  // llegarían al PDF.
  it('persiste fecha y observaciones ANTES de generar el consolidado', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Generación del FUR' });

    await user.click(screen.getByRole('button', { name: 'Generar expediente consolidado' }));

    await waitFor(() => expect(mocks.patchFieldValues).toHaveBeenCalled());
    await waitFor(() => expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE));
    const ordenGuardado = mocks.patchFieldValues.mock.invocationCallOrder[0]!;
    const ordenGenerado = mocks.generarConsolidado.mock.invocationCallOrder[0]!;
    expect(ordenGuardado).toBeLessThan(ordenGenerado);
  });

  // HU #11051 — el backend rechaza la regeneración del gestor en estado final; el mensaje lo explica.
  it('traduce el rechazo por estado final del backend', async () => {
    mocks.getAttachments.mockResolvedValue([]);
    mocks.generarConsolidado.mockRejectedValue(
      new Error('409 Conflict: generacion_bloqueada_estado_final'),
    );
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Generación del FUR' });

    await user.click(screen.getByRole('button', { name: 'Generar expediente consolidado' }));

    expect(
      await screen.findByText(/su documentación es definitiva y no se regenera/i),
    ).toBeInTheDocument();
  });

  // AC3 — trámite aprobado: ninguna acción de generación, solo consulta y descarga.
  it('en trámite aprobado no ofrece generar y lo explica', async () => {
    mocks.getAttachments.mockResolvedValue([FUR_DOC]);
    mocks.getInstance.mockResolvedValue({ ...INSTANCE_DETAIL, status: 'aprobado' });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Generación del FUR' });

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
    await screen.findByRole('region', { name: 'Generación del FUR' });
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

    await waitFor(() => expect(mocks.patchFieldValues).toHaveBeenCalledTimes(1));
    const items = mocks.patchFieldValues.mock.calls[0][1] as {
      fieldKey: string;
      valueText: string;
    }[];
    const byKey = Object.fromEntries(items.map((i) => [i.fieldKey, i.valueText]));
    // Persiste el id real del catálogo (no solo el texto), además de code/name/city.
    expect(byKey.transit_office_id).toBe('aaaaaaaa-0001-4000-8000-000000000003');
    expect(byKey.transit_office_code).toBe('76001');
    expect(byKey.transit_office_name).toBe('Cali — STTMP');
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
    // N 03 — label desde la fuente única lib/tramites/estados.ts.
    expect(within(resumen).getByText('Borrador')).toBeInTheDocument();
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

  it('traspaso con OT resuelto: solo lectura, sin botón Cambiar/Seleccionar y sin abrir el modal', async () => {
    mocks.getInstance.mockResolvedValue(TRASPASO_OT_BOUND);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const seccion = await screen.findByRole('region', { name: 'Organismo de tránsito' });
    expect(
      within(seccion).getByText(/El organismo proviene del RUNT y no puede modificarse en un traspaso\./),
    ).toBeInTheDocument();
    // No hay botón para seleccionar/cambiar el organismo.
    expect(screen.queryByRole('button', { name: /Cambiar|Seleccionar/ })).not.toBeInTheDocument();
    // En traspaso nunca se abre el modal → no se consulta el catálogo de OT.
    expect(mocks.listTransitOffices).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog', { name: 'Seleccionar organismo de tránsito' })).not.toBeInTheDocument();
  });

  it('traspaso con OT del RUNT no habilitado (nombre sin id): avisa, sin selector', async () => {
    mocks.getInstance.mockResolvedValue({
      ...INSTANCE_DETAIL,
      fieldValues: [
        { formFieldId: null, fieldKey: 'transit_office_name', valueText: 'OT NO HABILITADO', valueJson: null, source: 'consultation' },
      ],
    });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const seccion = await screen.findByRole('region', { name: 'Organismo de tránsito' });
    expect(within(seccion).getByText(/no está habilitado para tu empresa/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cambiar|Seleccionar/ })).not.toBeInTheDocument();
  });

  it('matrícula: sigue ofreciendo seleccionar/cambiar el organismo (sin cambios)', async () => {
    // Con OT elegido (nombre/código) en matrícula el botón dice "Cambiar".
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const seccion = await screen.findByRole('region', { name: 'Organismo de tránsito' });
    expect(within(seccion).getByRole('button', { name: /Cambiar/ })).toBeInTheDocument();
    // El texto de solo lectura de traspaso NO aparece en matrícula.
    expect(screen.queryByText(/no puede modificarse en un traspaso/)).not.toBeInTheDocument();
  });
});

describe('FirmaFurStep — firma no bloqueante en traspaso (B12, HU #10661)', () => {
  it('traspaso: la sección de firma es informativa y aclara que no bloquea el trámite', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const seccion = await screen.findByRole('region', { name: 'Firma de la compraventa' });
    // Copy alineado a ADR-0028: la firma no bloquea preparar/radicar.
    expect(within(seccion).getByText('no bloquea')).toBeInTheDocument();
    expect(
      within(seccion).getByText(/pendiente de definición de negocio/),
    ).toBeInTheDocument();
  });

  it('traspaso: la firma es informativa y ya no se solicita desde el paso (HU #11019)', async () => {
    // AC5 de ADR-0028 sigue vigente en el MODELO (endpoints y estado intactos), pero la UI ya no
    // ofrece la acción: la firma de compraventa no bloquea y pedirla no aportaba nada al gestor.
    mocks.listFirmas.mockResolvedValue([FIRMA_ENVIADA]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    const card = await screen.findByRole('group', { name: 'Firma Comprador' });

    // El estado de una firma ya existente se sigue mostrando…
    expect(
      (within(card).getByLabelText('Enlace de firma Comprador') as HTMLInputElement).value,
    ).toContain('https://mock/sign/sig-1');
    // …pero no hay forma de solicitar una nueva.
    expect(within(card).queryByRole('button', { name: 'Solicitar firma' })).toBeNull();
  });
});

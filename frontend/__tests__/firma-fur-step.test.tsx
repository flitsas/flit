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

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.getAttachments.mockResolvedValue([]);
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
    mocks.getAttachments.mockResolvedValueOnce([]).mockResolvedValueOnce([FUR_DOC]);
    const onRefresh = vi.fn();
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" onRefresh={onRefresh} />);
    await screen.findByRole('region', { name: 'Generación del FUR' });
    await user.click(screen.getByRole('button', { name: 'Generar FUR / contrato' }));
    await waitFor(() => expect(mocks.generarFur).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(/fur · fur.txt/)).toBeInTheDocument();
    expect(onRefresh).toHaveBeenCalled();
  });

  it('maneja el 409 biometria_gate con un mensaje explicativo', async () => {
    mocks.generarFur.mockRejectedValue(new Error('409 Conflict: biometria_gate'));
    const user = userEvent.setup();
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('region', { name: 'Generación del FUR' });
    await user.click(screen.getByRole('button', { name: 'Generar FUR / contrato' }));
    expect(
      await screen.findByText(/primero debe aprobarse la biométrica/),
    ).toBeInTheDocument();
  });
});

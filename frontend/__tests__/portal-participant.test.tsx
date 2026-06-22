import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { PortalView } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente público (sin red real) ─────────────────────────
const mocks = vi.hoisted(() => ({
  getByToken: vi.fn(),
  aceptarConsentimiento: vi.fn(),
  subirDocumento: vi.fn(),
  getFirma: vi.fn(),
  simularFirma: vi.fn(),
  finalizar: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  portalPublicClient: {
    getByToken: mocks.getByToken,
    aceptarConsentimiento: mocks.aceptarConsentimiento,
    subirDocumento: mocks.subirDocumento,
    getFirma: mocks.getFirma,
    simularFirma: mocks.simularFirma,
    finalizar: mocks.finalizar,
  },
}));

import { PortalParticipant } from '@/app/portal/[token]/PortalParticipant';

const TOKEN = 'tok-1';

function makeView(overrides: Partial<PortalView> = {}): PortalView {
  return {
    rol: 'comprador',
    nombre: 'Ana Comprador',
    consentDado: false,
    consentVersion: 'v1',
    consentText: 'Autorizo el tratamiento de mis datos.',
    expirado: false,
    completado: false,
    instancia: {
      referencia: 'REF-001',
      modalidadEntrada: 'traspaso',
      tipologiaCodigo: 'traspaso_standard',
      tipologiaNombre: 'Traspaso',
    },
    pasosPendientes: {
      consentDado: false,
      documentos: [
        { tipo: 'cedula', label: 'Cédula', obligatorio: true, subido: false },
      ],
      biometrica: { existe: false, estado: null, pendiente: true },
      firma: { aplica: false, existe: false, estado: null, firmada: false },
      completado: false,
    },
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getFirma.mockResolvedValue({
    aplica: true,
    signatureId: 'sig-1',
    estado: 'enviada',
    signUrl: 'https://mock/sign',
    firmada: false,
  });
  mocks.aceptarConsentimiento.mockResolvedValue({ consentVersion: 'v1', consent1581At: 'x' });
  mocks.subirDocumento.mockResolvedValue({ id: 'a1' });
  mocks.simularFirma.mockResolvedValue({ id: 'sig-1', estado: 'firmada', pdfPath: 'p', sha256: 's' });
  mocks.finalizar.mockResolvedValue({ completedAt: 'x' });
});

describe('PortalParticipant — pantallas terminales', () => {
  it('muestra error cuando el token no existe (404)', async () => {
    mocks.getByToken.mockRejectedValue(new Error('404 Not Found'));
    render(<PortalParticipant token={TOKEN} />);
    expect(
      await screen.findByText(/Enlace no válido, expirado o ya utilizado/),
    ).toBeInTheDocument();
  });

  it('muestra pantalla de expirado', async () => {
    mocks.getByToken.mockResolvedValue(makeView({ expirado: true }));
    render(<PortalParticipant token={TOKEN} />);
    expect(await screen.findByText('Enlace expirado')).toBeInTheDocument();
  });
});

describe('PortalParticipant — gate de consentimiento', () => {
  it('bloquea los pasos hasta aceptar el consentimiento', async () => {
    mocks.getByToken.mockResolvedValue(makeView());
    render(<PortalParticipant token={TOKEN} />);
    expect(await screen.findByText('Autorización de datos personales')).toBeInTheDocument();
    // Las tareas (documentos) no se muestran todavía.
    expect(screen.queryByRole('region', { name: 'Documentos' })).not.toBeInTheDocument();
  });

  it('al aceptar registra el consentimiento y re-consulta la vista', async () => {
    mocks.getByToken
      .mockResolvedValueOnce(makeView())
      .mockResolvedValue(makeView({ consentDado: true }));
    const user = userEvent.setup();
    render(<PortalParticipant token={TOKEN} />);
    await screen.findByText('Autorización de datos personales');

    const button = screen.getByRole('button', { name: 'Aceptar y continuar' });
    expect(button).toBeDisabled();
    await user.click(
      screen.getByLabelText('Acepto el tratamiento de mis datos personales'),
    );
    await user.click(button);

    await waitFor(() => expect(mocks.aceptarConsentimiento).toHaveBeenCalledWith(TOKEN));
    // Tras re-consultar, el gate desaparece y se ven las tareas.
    expect(await screen.findByRole('region', { name: 'Documentos' })).toBeInTheDocument();
  });
});

describe('PortalParticipant — pasos tras consentimiento', () => {
  it('sube un documento del checklist', async () => {
    mocks.getByToken.mockResolvedValue(makeView({ consentDado: true }));
    const user = userEvent.setup();
    render(<PortalParticipant token={TOKEN} />);
    const input = (await screen.findByLabelText('Subir Cédula')) as HTMLInputElement;
    const file = new File(['x'], 'cedula.pdf', { type: 'application/pdf' });
    await user.upload(input, file);
    await waitFor(() =>
      expect(mocks.subirDocumento).toHaveBeenCalledWith(TOKEN, 'cedula', file),
    );
  });

  it('finaliza la participación y muestra la pantalla de éxito', async () => {
    mocks.getByToken.mockResolvedValue(makeView({ consentDado: true }));
    const user = userEvent.setup();
    render(<PortalParticipant token={TOKEN} />);
    await user.click(await screen.findByRole('button', { name: 'Finalizar mi parte' }));
    await waitFor(() => expect(mocks.finalizar).toHaveBeenCalledWith(TOKEN));
    expect(await screen.findByText('¡Listo!')).toBeInTheDocument();
  });

  it('muestra la firma cuando aplica al rol', async () => {
    mocks.getByToken.mockResolvedValue(
      makeView({
        consentDado: true,
        pasosPendientes: {
          ...makeView().pasosPendientes,
          consentDado: true,
          firma: { aplica: true, existe: true, estado: 'enviada', firmada: false },
        },
      }),
    );
    const user = userEvent.setup();
    render(<PortalParticipant token={TOKEN} />);
    const simular = await screen.findByRole('button', { name: 'Simular firma (DEV)' });
    await user.click(simular);
    await waitFor(() => expect(mocks.simularFirma).toHaveBeenCalledWith(TOKEN));
  });
});

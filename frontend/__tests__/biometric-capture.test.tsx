import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { BiometriaPublicView } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente público (sin red real) ────────────────────────
const mocks = vi.hoisted(() => ({
  getByToken: vi.fn(),
  complete: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  biometricPublicClient: {
    getByToken: mocks.getByToken,
    complete: mocks.complete,
  },
}));

import { BiometricCapture } from '@/app/biometric/[token]/BiometricCapture';

const TOKEN = 'raw-token-abc';

const ACTIVA: BiometriaPublicView = {
  estado: 'enviado',
  parte: 'comprador',
  nombre: 'Ana Comprador',
  intentos: 0,
  maxIntentos: 3,
  expired: false,
};

function img(name = 'foto.png'): File {
  return new File(['x'], name, { type: 'image/png' });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getByToken.mockResolvedValue(ACTIVA);
  mocks.complete.mockResolvedValue({ estado: 'aprobado', score: 95, motivo: 'ok' });
  // jsdom no implementa createObjectURL; lo stubeamos para las vistas previas.
  if (!URL.createObjectURL) {
    Object.defineProperty(URL, 'createObjectURL', { value: vi.fn(() => 'blob:x'), writable: true });
  } else {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
  }
});

describe('BiometricCapture — terminal states', () => {
  it('muestra aprobado cuando la tarea ya está aprobada', async () => {
    mocks.getByToken.mockResolvedValue({ ...ACTIVA, estado: 'aprobado' });
    render(<BiometricCapture token={TOKEN} />);
    expect(await screen.findByText('¡Validación aprobada!')).toBeInTheDocument();
  });

  it('muestra expirado cuando la tarea expiró', async () => {
    mocks.getByToken.mockResolvedValue({ ...ACTIVA, estado: 'expirado', expired: true });
    render(<BiometricCapture token={TOKEN} />);
    expect(await screen.findByText('Enlace expirado')).toBeInTheDocument();
  });

  it('muestra error de no encontrado en 404', async () => {
    mocks.getByToken.mockRejectedValue(new Error('404 Not Found'));
    render(<BiometricCapture token={TOKEN} />);
    expect(await screen.findByText(/Enlace no válido o no encontrado/)).toBeInTheDocument();
  });
});

describe('BiometricCapture — happy path', () => {
  it('sube las 3 fotos y muestra la aprobación', async () => {
    const user = userEvent.setup();
    render(<BiometricCapture token={TOKEN} />);

    await screen.findByText(/Hola Ana Comprador/);
    await user.upload(screen.getByLabelText('Selfie (rostro)'), img('selfie.png'));
    await user.upload(screen.getByLabelText('Cédula — frente'), img('frente.png'));
    await user.upload(screen.getByLabelText('Cédula — reverso'), img('reverso.png'));

    await user.click(screen.getByRole('button', { name: 'Enviar y validar' }));

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledTimes(1));
    const [token, photos] = mocks.complete.mock.calls[0];
    expect(token).toBe(TOKEN);
    expect(photos.rostro).toBeInstanceOf(File);
    expect(photos.cedulaFrontal).toBeInstanceOf(File);
    expect(photos.cedulaReverso).toBeInstanceOf(File);
  });

  it('mantiene deshabilitado el envío hasta tener las 3 fotos', async () => {
    const user = userEvent.setup();
    render(<BiometricCapture token={TOKEN} />);
    await screen.findByText(/Hola Ana Comprador/);
    const submit = screen.getByRole('button', { name: 'Enviar y validar' });
    expect(submit).toBeDisabled();
    await user.upload(screen.getByLabelText('Selfie (rostro)'), img('selfie.png'));
    expect(submit).toBeDisabled();
  });
});

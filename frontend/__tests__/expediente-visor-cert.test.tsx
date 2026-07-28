/**
 * HU #10861 — ExpedienteVisor: visor perezoso del certificado de identidad en la pestaña Comprador/Vendedor.
 */
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import ExpedienteVisor from '@/components/operacion/ExpedienteVisor';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { Actor, BiometricValidation } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    downloadBiometricCertificado: vi.fn(() =>
      Promise.resolve({
        blob: new Blob(['%PDF-cert'], { type: 'application/pdf' }),
        filename: 'certificado_identidad.pdf',
        mimetype: 'application/pdf',
      }),
    ),
  },
}));

beforeAll(() => {
  global.URL.createObjectURL = vi.fn(() => 'blob:mock-cert');
  global.URL.revokeObjectURL = vi.fn();
});

beforeEach(() => {
  vi.clearAllMocks();
});

const comprador = {
  fullName: 'Juan Pérez',
  documentType: 'CC',
  documentNumber: '123',
} as unknown as Actor;

function bio(status: BiometricValidation['status']): BiometricValidation {
  return {
    id: 'val-1',
    partyRole: 'comprador',
    name: 'Juan Pérez',
    documentType: 'CC',
    documentNumber: '123',
    email: 'j@x.com',
    status,
    intentos: 1,
    maxIntentos: 3,
    score: 95,
    expiresAt: '',
    validatedAt: null,
    expired: false,
    provider: 'kyverum',
    captureUrl: null,
  };
}

function renderVisor(b: BiometricValidation) {
  return render(
    <ExpedienteVisor
      instanceId="inst-1"
      fieldValues={[]}
      comprador={comprador}
      vendedor={null}
      vin="VIN123"
      attachments={[]}
      biometric={[b]}
      orgTransito={{}}
    />,
  );
}

describe('ExpedienteVisor — certificado de identidad (HU #10861)', () => {
  it('carga y muestra el certificado en un iframe cuando la validación está aprobada', async () => {
    renderVisor(bio('aprobado'));
    fireEvent.click(screen.getByRole('tab', { name: /Comprador/ }));

    const iframe = await screen.findByTestId('cert-iframe');
    expect(iframe).toHaveAttribute('src', 'blob:mock-cert');
    expect(tramitesClient.downloadBiometricCertificado).toHaveBeenCalledWith('inst-1', 'val-1');
  });

  it('no descarga ni muestra el visor si la validación no está aprobada', () => {
    renderVisor(bio('enviado'));
    fireEvent.click(screen.getByRole('tab', { name: /Comprador/ }));

    expect(screen.queryByTestId('cert-iframe')).not.toBeInTheDocument();
    expect(screen.getByText(/estará disponible cuando la validación sea aprobada/i)).toBeInTheDocument();
    expect(tramitesClient.downloadBiometricCertificado).not.toHaveBeenCalled();
  });
});

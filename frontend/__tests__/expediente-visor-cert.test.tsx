/**
 * HU #10861 — certificados de identidad en el resumen (botón, abre pestaña).
 */
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import MatriculaResumen from '@/components/operacion/MatriculaResumen';
import ExpedienteVisor from '@/components/operacion/ExpedienteVisor';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    downloadBiometricCertificado: vi.fn(() =>
      Promise.resolve({
        blob: new Blob(['%PDF-cert'], { type: 'application/pdf' }),
        filename: 'certificado_identidad.pdf',
        mimetype: 'application/pdf',
      }),
    ),
    downloadAttachment: vi.fn(),
    getBiometricState: vi.fn(() =>
      Promise.resolve({ validations: [], provider: 'mock', firmaBaulPartes: [] }),
    ),
    getFirmaPosterior: vi.fn(() =>
      Promise.resolve({ aplica: false, marcado: false }),
    ),
  },
}));

beforeAll(() => {
  global.URL.createObjectURL = vi.fn(() => 'blob:mock-cert');
  global.URL.revokeObjectURL = vi.fn();
  vi.stubGlobal('open', vi.fn());
});

beforeEach(() => {
  vi.clearAllMocks();
});

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

function renderResumen(b: BiometricValidation | null, firmaBaul = false) {
  return render(
    <MatriculaResumen
      modalidad="matricula_inicial"
      status="borrador"
      placa="ABC123"
      vehiculo="YAMAHA 2026"
      vin="VIN123"
      comprador={{ nombre: 'Juan Pérez', documento: '123', tipoDoc: 'CC', email: 'actor@empresa.co' }}
      vendedor={null}
      archivosCount={0}
      identidadAprobada={b?.status === 'aprobado'}
      firmaBaulPartes={firmaBaul ? ['comprador'] : []}
      instanceId="inst-1"
      compradorBio={b}
    />,
  );
}

describe('MatriculaResumen — certificado de identidad (HU #10861)', () => {
  it('al pulsar el botón abre el certificado (descarga + nueva pestaña) si está aprobado', async () => {
    renderResumen(bio('aprobado'));
    fireEvent.click(screen.getByRole('button', { name: /Certificado ID · Comprador/i }));

    await waitFor(() =>
      expect(tramitesClient.downloadBiometricCertificado).toHaveBeenCalledWith('inst-1', 'val-1'),
    );
    expect(window.open).toHaveBeenCalled();
  });

  it('con validación pendiente embebe la biométrica y no muestra el certificado', async () => {
    renderResumen(bio('en_proceso'));
    expect(screen.queryByRole('button', { name: /Certificado ID · Comprador/i })).not.toBeInTheDocument();
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
  });

  it('con firma del baúl no ofrece certificado', () => {
    renderResumen(bio('aprobado'), true);
    expect(screen.getByText(/Firma electrónica \(baúl\)/i)).toBeInTheDocument();
    expect(screen.getByTestId('identidad-firma-baul')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Certificado ID · Comprador/i })).not.toBeInTheDocument();
    expect(tramitesClient.downloadBiometricCertificado).not.toHaveBeenCalled();
  });

  it('con identidad aprobada muestra el banner de verificada en el actor', () => {
    renderResumen(bio('aprobado'));
    expect(screen.getByTestId('identidad-verificada')).toBeInTheDocument();
    // Copy Flit 2.0 · S4: el sello ya no lleva el puntaje.
    expect(screen.getByText(/Identidad verificada\./i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Certificado ID · Comprador/i })).toBeInTheDocument();
  });

  it('sin validación propia embebe la biométrica del comprador y muestra el correo del actor', async () => {
    renderResumen(null);
    expect(screen.queryByText('Sin validación')).not.toBeInTheDocument();
    expect(screen.getByText('actor@empresa.co')).toBeInTheDocument();
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
  });
});

describe('ExpedienteVisor — documentos (sin repetir vehículo/actores)', () => {
  it('muestra documentos; no muestra Vehículo ni Validación de identidad', () => {
    render(
      <ExpedienteVisor
        instanceId="inst-1"
        attachments={[]}
      />,
    );
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    // «Documentos cargados» ya no es un acordeón (rediseño): tarjeta siempre abierta, sin botón.
    expect(screen.getByRole('region', { name: 'Documentos cargados' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Documentos' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Vehículo' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Especificaciones técnicas' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Validación de identidad' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Organismo de tránsito' })).not.toBeInTheDocument();
  });

  it('sin consolidado muestra el CTA Ver expediente consolidado (genera + abre en pestaña)', () => {
    render(
      <ExpedienteVisor
        instanceId="inst-1"
        attachments={[]}
      />,
    );
    expect(
      screen.getByRole('button', { name: 'Ver expediente consolidado (PDF)' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Generar expediente consolidado/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Re-generar expediente consolidado/i }),
    ).not.toBeInTheDocument();
  });

  it('con consolidado muestra Re-generar y el CTA Ver expediente consolidado', () => {
    render(
      <ExpedienteVisor
        instanceId="inst-1"
        attachments={[
          {
            id: 'att-c',
            tipo: 'consolidado',
            filename: 'consolidado_TRM.pdf',
            mimetype: 'application/pdf',
            sizeBytes: 10,
            sha256: 'abc',
            source: 'system',
            uploadedAt: '2026-06-19T00:00:00Z',
          },
        ]}
      />,
    );
    expect(
      screen.getByRole('button', { name: 'Re-generar expediente consolidado' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Ver expediente consolidado (PDF)' }),
    ).toBeInTheDocument();
  });
});

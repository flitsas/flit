// ADR-0053 (Múltiple Propietario) — el resumen del trámite pasa de "una tarjeta por PARTE" a "una
// tarjeta por ACTOR", reutilizando exactamente la misma presentación que ya tenía la parte con un
// solo actor (`ActorBlock` + biométrica embebida). Cubre lo pedido por el encargo:
//  1. Regresión cero con 1 solo actor por lado (incluido el ancho del vehículo).
//  2. 2+ copropietarios: una sección por actor, cada una con su propio estado.
//  3. El vehículo pasa a línea completa cuando un lado tiene 2+ copropietarios (no solo con 2 partes).
//  4. Las validaciones/acciones (iniciar/simular) corresponden al copropietario CONCRETO, no siempre
//     al principal — mismo criterio que ya se verificó para `BiometricStep.tsx` standalone.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import MatriculaResumen from '@/components/operacion/MatriculaResumen';
import type { BiometricValidation, ProcedureActor } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    downloadBiometricCertificado: vi.fn(),
    downloadAttachment: vi.fn(),
    getBiometricState: vi.fn(),
    getFirmaPosterior: vi.fn(() => Promise.resolve({ aplica: false, marcado: false })),
    getActors: vi.fn(),
    ensureIdentity: vi.fn(() => Promise.resolve({ outcome: 'requiere_validacion' })),
    getInstance: vi.fn(() => Promise.resolve({ fieldValues: [] })),
    iniciarBiometric: vi.fn(),
    simulateBiometric: vi.fn(),
  },
}));

import { tramitesClient } from '@/lib/api/tramites-client';

const COMPRADOR_1: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'CC',
  numeroDocumento: '1000',
  nombreCompleto: 'Ana Uno',
  email: 'ana@example.com',
  ordinal: 1,
  porcentaje: 60,
};

const COMPRADOR_2: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'CC',
  numeroDocumento: '2000',
  nombreCompleto: 'Beto Dos',
  email: 'beto@example.com',
  ordinal: 2,
  porcentaje: 40,
};

const VALIDACION_APROBADA_1: BiometricValidation = {
  id: 'val-1',
  partyRole: 'comprador',
  name: 'Ana Uno',
  documentType: 'CC',
  documentNumber: '1000',
  email: 'ana@example.com',
  status: 'aprobado',
  intentos: 1,
  maxIntentos: 3,
  score: 95,
  expiresAt: '2030-01-01T00:00:00Z',
  validatedAt: '2026-01-01T00:00:00Z',
  expired: false,
  provider: 'mock',
  captureUrl: null,
  ordinal: 1,
};

function vehiculoRegion() {
  return screen.getByRole('region', { name: 'Vehículo' });
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
    validations: [],
    provider: 'mock',
    firmaBaulPartes: [],
  } as never);
  vi.mocked(tramitesClient.getActors).mockResolvedValue([]);
});

describe('MatriculaResumen — Múltiple Propietario, regresión cero con 1 solo actor', () => {
  it('sin vendedorActores/compradorActores: la tarjeta única de siempre, vehículo a una columna', async () => {
    render(
      <MatriculaResumen
        modalidad="matricula_inicial"
        status="borrador"
        placa="ABC123"
        vehiculo="YAMAHA 2026"
        vin="VIN123"
        vendedor={null}
        comprador={{ nombre: 'Ana Uno', documento: '1000', tipoDoc: 'CC', email: 'ana@example.com' }}
        archivosCount={0}
        identidadAprobada={false}
        firmaBaulPartes={[]}
        instanceId="inst-1"
        compradorBio={null}
        vendedorBio={null}
      />,
    );

    // Vehículo NO ocupa las dos columnas: un solo actor, el criterio previo (dosPartes) manda igual.
    expect(vehiculoRegion().className).not.toContain('lg:col-span-2');

    // Una sola tarjeta "Comprador", sin sufijo de ordinal.
    expect(await screen.findByRole('region', { name: 'Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Comprador 1' })).toBeNull();
  });

  it('con vendedorActores/compradorActores de longitud 1: mismo camino de siempre (aditivo, sin efecto)', async () => {
    render(
      <MatriculaResumen
        modalidad="matricula_inicial"
        status="borrador"
        placa="ABC123"
        vehiculo="YAMAHA 2026"
        vin="VIN123"
        vendedor={null}
        comprador={{ nombre: 'Ana Uno', documento: '1000', tipoDoc: 'CC', email: 'ana@example.com' }}
        compradorActores={[COMPRADOR_1]}
        archivosCount={0}
        identidadAprobada={false}
        firmaBaulPartes={[]}
        instanceId="inst-1"
        compradorBio={null}
        vendedorBio={null}
      />,
    );

    expect(vehiculoRegion().className).not.toContain('lg:col-span-2');
    expect(await screen.findByRole('region', { name: 'Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Comprador 1' })).toBeNull();
  });
});

describe('MatriculaResumen — Múltiple Propietario, 2+ copropietarios', () => {
  it('una ResumenCard por copropietario, el vehículo pasa a línea completa, estados distintos', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([COMPRADOR_1, COMPRADOR_2]);
    vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
      validations: [VALIDACION_APROBADA_1],
      provider: 'mock',
      firmaBaulPartes: [],
    } as never);

    render(
      <MatriculaResumen
        modalidad="matricula_inicial"
        status="borrador"
        placa="ABC123"
        vehiculo="YAMAHA 2026"
        vin="VIN123"
        vendedor={null}
        comprador={{ nombre: 'Ana Uno', documento: '1000', tipoDoc: 'CC', email: 'ana@example.com' }}
        compradorActores={[COMPRADOR_1, COMPRADOR_2]}
        biometric={[VALIDACION_APROBADA_1]}
        archivosCount={0}
        identidadAprobada={false}
        firmaBaulPartes={[]}
        instanceId="inst-1"
        compradorBio={null}
        vendedorBio={null}
      />,
    );

    // El vehículo generaliza a línea completa: no hay dos PARTES (no hay vendedor), pero el
    // comprador tiene 2+ copropietarios.
    expect(vehiculoRegion().className).toContain('lg:col-span-2');

    const tarjetaUno = await screen.findByRole('region', { name: 'Comprador 1' });
    const tarjetaDos = screen.getByRole('region', { name: 'Comprador 2' });

    // Cada tarjeta trae los datos de SU actor, no del otro (puede repetirse dentro de la misma
    // tarjeta — nombre en el grid de datos y en el bloque de validación/trazabilidad).
    expect(within(tarjetaUno).getAllByText('Ana Uno').length).toBeGreaterThan(0);
    expect(within(tarjetaUno).queryByText('Beto Dos')).not.toBeInTheDocument();
    expect(within(tarjetaDos).getAllByText('Beto Dos').length).toBeGreaterThan(0);
    expect(within(tarjetaDos).queryByText('Ana Uno')).not.toBeInTheDocument();
    // El porcentaje de propiedad de cada uno, dato propio del copropietario.
    expect(within(tarjetaUno).getByText('60%')).toBeInTheDocument();
    expect(within(tarjetaDos).getByText('40%')).toBeInTheDocument();

    // Estados distintos: Ana (ordinal 1) ya validó — su tarjeta lo dice y NO ofrece iniciar/simular
    // de nuevo.
    expect(
      await within(tarjetaUno).findByText('Identidad verificada.'),
    ).toBeInTheDocument();
    expect(
      within(tarjetaUno).queryByRole('button', { name: /simular validación de identidad/i }),
    ).not.toBeInTheDocument();

    // Beto (ordinal 2) sigue pendiente — su tarjeta SÍ ofrece iniciar/simular.
    expect(
      await within(tarjetaDos).findByRole('button', { name: /simular validación de identidad/i }),
    ).toBeInTheDocument();
  });

  it('iniciar/simular desde la tarjeta del copropietario pendiente apunta a SU documento, no al del principal', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([COMPRADOR_1, COMPRADOR_2]);
    vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
      validations: [VALIDACION_APROBADA_1],
      provider: 'mock',
      firmaBaulPartes: [],
    } as never);

    render(
      <MatriculaResumen
        modalidad="matricula_inicial"
        status="borrador"
        placa="ABC123"
        vehiculo="YAMAHA 2026"
        vin="VIN123"
        vendedor={null}
        comprador={{ nombre: 'Ana Uno', documento: '1000', tipoDoc: 'CC', email: 'ana@example.com' }}
        compradorActores={[COMPRADOR_1, COMPRADOR_2]}
        biometric={[VALIDACION_APROBADA_1]}
        archivosCount={0}
        identidadAprobada={false}
        firmaBaulPartes={[]}
        instanceId="inst-1"
        compradorBio={null}
        vendedorBio={null}
      />,
    );

    const tarjetaDos = await screen.findByRole('region', { name: 'Comprador 2' });
    const user = userEvent.setup();
    await user.click(
      within(tarjetaDos).getByRole('button', { name: /simular validación de identidad/i }),
    );
    await user.click(within(tarjetaDos).getByRole('button', { name: /confirmar y enviar/i }));

    expect(tramitesClient.simulateBiometric).toHaveBeenCalledWith('inst-1', {
      parte: 'comprador',
      documento: '2000',
    });
  });
});

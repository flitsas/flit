// ADR-0053 (Múltiple Propietario) — BiometricStep debe mostrar una tarjeta por CADA copropietario
// del lado (no una sola por parte, arbitrariamente resuelta al ordinal=1) y las acciones de
// iniciar/reenviar/simular deben apuntar al copropietario CONCRETO, nunca siempre al principal.
//
// Cubre, tal como lo pidió el encargo:
//  1. Regresión cero con 1 solo actor por lado (caso mayoritario): mismo título, mismo
//     `aria-label`, y el llamado a iniciar/simular sigue siendo `{ parte }` a secas, sin
//     `documento`/`nombre`/`tipoDoc`/`email` — el backend seguía resolviendo bien sin ellos.
//  2. 2+ copropietarios con estados DISTINTOS (uno aprobado, otro pendiente): cada uno con su
//     propia tarjeta/badge — el "gate" (cuántas tarjetas hay) cuenta actores, no partes.
//  3. Cobertura de baúl por actor (`firmaBaulActores`), nunca aproximada por lado.
//  4. Iniciar/simular sobre el copropietario pendiente manda SU documento, no el del principal.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BiometricStep } from '../BiometricStep';
import { WizardReadOnlyProvider } from '../WizardReadOnlyContext';
import type { BiometricValidation, ProcedureActor } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getBiometricState: vi.fn(),
    getActors: vi.fn(),
    iniciarBiometric: vi.fn(),
    simulateBiometric: vi.fn(),
  },
  getIdentitySendConflict: () => null,
}));

vi.mock('@/lib/api/client', () => ({ getToken: () => null }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => null,
  isSuperAdmin: () => false,
}));

import { tramitesClient } from '@/lib/api/tramites-client';

const ACTOR_1: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'CC',
  numeroDocumento: '1000',
  nombreCompleto: 'Ana Uno',
  email: 'ana@example.com',
  ordinal: 1,
  porcentaje: 60,
};

const ACTOR_2: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'CC',
  numeroDocumento: '2000',
  nombreCompleto: 'Beto Dos',
  email: 'beto@example.com',
  ordinal: 2,
  porcentaje: 40,
};

const VALIDACION_APROBADA_ACTOR_1: BiometricValidation = {
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

function renderStep(
  actors: ProcedureActor[],
  validations: BiometricValidation[],
  extra: Partial<{ firmaBaulActores: { parte: string; documentNumber: string; ordinal: number }[] }> = {},
) {
  vi.mocked(tramitesClient.getActors).mockResolvedValue(actors);
  vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
    validations,
    provider: 'mock',
    ...extra,
  } as never);
  return render(
    <WizardReadOnlyProvider readOnly={false}>
      <BiometricStep instanceId="inst-1" modalidad="matricula_inicial" />
    </WizardReadOnlyProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('BiometricStep — Múltiple Propietario (ADR-0053)', () => {
  it('regresión cero — 1 solo actor por lado: mismo título/aria-label y llamado sin overrides', async () => {
    renderStep([ACTOR_1], []);

    // Mismo `aria-label` de siempre, sin sufijo de ordinal.
    const card = await screen.findByRole('group', { name: /Biométrica Comprador/i });
    // Mismo título de siempre: "Validación del Comprador", NUNCA "Validación del Comprador 1".
    expect(within(card).getByText('Validación del Comprador')).toBeInTheDocument();
    expect(within(card).queryByText('Validación del Comprador 1')).not.toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(
      within(card).getByRole('button', { name: /Simular validación de identidad/i }),
    );
    await user.click(within(card).getByRole('button', { name: /Confirmar y enviar/i }));

    expect(tramitesClient.simulateBiometric).toHaveBeenCalledWith('inst-1', {
      parte: 'comprador',
      documento: undefined,
    });
  });

  it('2+ copropietarios con estados distintos: una tarjeta por actor, no una por parte', async () => {
    renderStep([ACTOR_1, ACTOR_2], [VALIDACION_APROBADA_ACTOR_1]);

    // El "gate"/conteo de tarjetas refleja ACTORES (2), no partes (1: "comprador").
    const cardUno = await screen.findByRole('heading', { name: /Validación del Comprador 1/i });
    const cardDos = await screen.findByRole('heading', { name: /Validación del Comprador 2/i });
    expect(cardUno).toBeInTheDocument();
    expect(cardDos).toBeInTheDocument();

    const grupo = screen.getByRole('group', { name: /Biométrica Comprador/i });
    // Ana (ordinal 1) aprobada, Beto (ordinal 2) sin ninguna validación → estados distintos, cada
    // uno en su propia tarjeta.
    expect(within(grupo).getByText(/Validada el/i)).toBeInTheDocument();
    expect(
      within(grupo).getByRole('button', { name: /Simular validación de identidad/i }),
    ).toBeInTheDocument();
  });

  it('cobertura de baúl por actor: solo el copropietario cubierto la muestra, nunca aproximada por lado', async () => {
    renderStep([ACTOR_1, ACTOR_2], [], {
      firmaBaulActores: [{ parte: 'comprador', documentNumber: '2000', ordinal: 2 }],
    });

    await screen.findByRole('heading', { name: /Validación del Comprador 1/i });

    // Ordinal 1 (Ana): sin cobertura de baúl, sigue en biométrica normal (pendiente).
    const tarjetaUno = screen
      .getByRole('heading', { name: /Validación del Comprador 1/i })
      .closest('[class*="rounded-2xl"]') as HTMLElement;
    expect(within(tarjetaUno).queryByText('Firma electrónica (baúl)')).not.toBeInTheDocument();

    // Ordinal 2 (Beto): cubierto por el baúl.
    const tarjetaDos = screen
      .getByRole('heading', { name: /Validación del Comprador 2/i })
      .closest('[class*="rounded-2xl"]') as HTMLElement;
    expect(within(tarjetaDos).getByText('Firma electrónica (baúl)')).toBeInTheDocument();
  });

  it('iniciar/simular apunta al copropietario CONCRETO: el pendiente (ordinal 2), no al principal', async () => {
    renderStep([ACTOR_1, ACTOR_2], [VALIDACION_APROBADA_ACTOR_1]);

    const user = userEvent.setup();
    const tarjetaDos = (
      await screen.findByRole('heading', { name: /Validación del Comprador 2/i })
    ).closest('[class*="rounded-2xl"]') as HTMLElement;

    await user.click(
      within(tarjetaDos).getByRole('button', { name: /Simular validación de identidad/i }),
    );
    await user.click(within(tarjetaDos).getByRole('button', { name: /Confirmar y enviar/i }));

    // El documento enviado es el de Beto (2000), NUNCA el de Ana (1000, el principal).
    expect(tramitesClient.simulateBiometric).toHaveBeenCalledWith('inst-1', {
      parte: 'comprador',
      documento: '2000',
    });
    expect(tramitesClient.simulateBiometric).not.toHaveBeenCalledWith(
      'inst-1',
      expect.objectContaining({ documento: '1000' }),
    );
  });

  it('kyverum — iniciar manda nombre/tipoDoc/documento/email del copropietario concreto', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_1, ACTOR_2]);
    vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
      validations: [VALIDACION_APROBADA_ACTOR_1],
      provider: 'kyverum',
    } as never);
    render(
      <WizardReadOnlyProvider readOnly={false}>
        <BiometricStep instanceId="inst-1" modalidad="matricula_inicial" />
      </WizardReadOnlyProvider>,
    );

    const user = userEvent.setup();
    const tarjetaDos = (
      await screen.findByRole('heading', { name: /Validación del Comprador 2/i })
    ).closest('[class*="rounded-2xl"]') as HTMLElement;

    await user.click(within(tarjetaDos).getByRole('button', { name: /Validar identidad/i }));
    await user.click(within(tarjetaDos).getByRole('button', { name: /Confirmar y enviar/i }));

    expect(tramitesClient.iniciarBiometric).toHaveBeenCalledWith('inst-1', {
      parte: 'comprador',
      nombre: 'Beto Dos',
      tipoDoc: 'CC',
      documento: '2000',
      email: 'beto@example.com',
    });
  });
});

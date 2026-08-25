// Un solo método visible por parte: baúl → firma electrónica; identidad → biométrica + trazabilidad.
// Ya no coexisten las dos columnas del mockup Step4 (override de producto).
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { BiometricStep } from '../BiometricStep';
import { WizardReadOnlyProvider } from '../WizardReadOnlyContext';
import type { ProcedureActor } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getBiometricState: vi.fn(),
    getActors: vi.fn().mockResolvedValue([]),
  },
}));

import { tramitesClient } from '@/lib/api/tramites-client';

vi.mock('@/lib/api/client', () => ({ getToken: () => null }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => null,
  isSuperAdmin: () => false,
}));

const ACTOR_JURIDICA_IDENTIDAD: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'NIT',
  numeroDocumento: '900123456',
  nombreCompleto: 'Comercializadora del Valle SAS',
  email: 'contacto@delvalle.com',
  personType: 'juridical',
  representanteLegal: {
    tipoDocumento: 'CC',
    numeroDocumento: '71654328',
    nombreCompleto: 'Héctor Copete Andrade',
    mecanismoFirma: 'identidad',
  },
};

const ACTOR_NATURAL_SIN_MECANISMO: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'CC',
  numeroDocumento: '1020304050',
  nombreCompleto: 'Ana Compradora',
  email: 'ana@example.com',
};

function renderStep(props: Partial<Parameters<typeof BiometricStep>[0]> = {}) {
  return render(
    <WizardReadOnlyProvider readOnly={false}>
      <BiometricStep instanceId="inst-1" modalidad="traspaso" {...props} />
    </WizardReadOnlyProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
    validations: [],
    provider: 'mock',
  });
  vi.mocked(tramitesClient.getActors).mockResolvedValue([]);
});

describe('BiometricStep — un solo método visible por parte', () => {
  it('sin baúl: muestra biométrica y NO el canvas de firma electrónica', async () => {
    renderStep();
    await screen.findByRole('group', { name: 'Biométrica Vendedor' });

    expect(screen.getAllByText('Estado Biométrico')).toHaveLength(2);
    expect(screen.queryByText('Firma electrónica')).toBeNull();
    expect(screen.queryByText('Método de Firma')).toBeNull();
  });

  it('cubierta por el baúl: solo firma electrónica activa (sin columna biométrica)', async () => {
    renderStep({ modalidad: 'matricula_inicial', vaultCoveredPartes: ['comprador'] });

    const compradorGrupo = await screen.findByRole('group', { name: 'Biométrica Comprador' });
    expect(within(compradorGrupo).getByText('Método de Firma')).toBeInTheDocument();
    expect(within(compradorGrupo).getByText('Firma electrónica')).toBeInTheDocument();
    expect(within(compradorGrupo).getByText('Firma electrónica activa')).toBeInTheDocument();
    expect(within(compradorGrupo).getByText('Firma electrónica (baúl)')).toBeInTheDocument();
    expect(within(compradorGrupo).queryByText('Estado Biométrico')).toBeNull();
    expect(
      within(compradorGrupo).queryByRole('button', { name: /Ver trazabilidad de validación/ }),
    ).toBeNull();
  });

  it('mecanismoFirma identidad sin baúl: panel biométrico, sin canvas de firma electrónica', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_JURIDICA_IDENTIDAD]);
    renderStep({ modalidad: 'matricula_inicial' });
    await screen.findByRole('group', { name: 'Biométrica Comprador' });

    expect(screen.getByText('Estado Biométrico')).toBeInTheDocument();
    expect(screen.queryByText('Firma electrónica')).toBeNull();
  });

  it('sin baúl y sin mecanismo: biométrica pendiente, sin canvas de firma', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_NATURAL_SIN_MECANISMO]);
    renderStep({ modalidad: 'matricula_inicial' });
    await screen.findByRole('group', { name: 'Biométrica Comprador' });

    expect(screen.getByText('Pendiente de validación')).toBeInTheDocument();
    expect(screen.queryByText('Firma electrónica')).toBeNull();
  });
});

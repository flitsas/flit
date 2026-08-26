// Sin baúl: layout dual (biométrica + firma electrónica). Con baúl: solo firma del baúl.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { BiometricStep } from '../BiometricStep';
import { WizardReadOnlyProvider } from '../WizardReadOnlyContext';
import type { BiometricValidation, ProcedureActor } from '@/lib/api/types/procedure-runtime';

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

const APROBADA_CON_HASH: BiometricValidation = {
  id: 'val-1',
  partyRole: 'comprador',
  name: 'Héctor Copete Andrade',
  documentType: 'CC',
  documentNumber: '71654328',
  email: 'hector@example.com',
  status: 'aprobado',
  intentos: 1,
  maxIntentos: 3,
  score: 95,
  expiresAt: new Date(Date.now() + 86400000).toISOString(),
  validatedAt: new Date().toISOString(),
  expired: false,
  provider: 'kyverum',
  captureUrl: null,
  certificateHash: 'sha256:abc123certificado',
};

const APROBADA_SIN_HASH: BiometricValidation = {
  ...APROBADA_CON_HASH,
  id: 'val-2',
  certificateHash: null,
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

describe('BiometricStep — firma electrónica vs baúl', () => {
  it('sin baúl: muestra Método de Firma y canvas de firma electrónica', async () => {
    renderStep();
    await screen.findByRole('group', { name: 'Biométrica Vendedor' });

    expect(screen.getAllByText('Estado Biométrico')).toHaveLength(2);
    expect(screen.getAllByText('Método de Firma')).toHaveLength(2);
    expect(screen.getAllByText('Firma electrónica')).toHaveLength(2);
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

  it('mecanismoFirma identidad sin baúl: panel biométrico Y canvas de firma electrónica', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_JURIDICA_IDENTIDAD]);
    renderStep({ modalidad: 'matricula_inicial' });
    const grupo = await screen.findByRole('group', { name: 'Biométrica Comprador' });

    expect(within(grupo).getByText('Estado Biométrico')).toBeInTheDocument();
    expect(within(grupo).getByText('Método de Firma')).toBeInTheDocument();
    expect(within(grupo).getByText('Firma electrónica')).toBeInTheDocument();
    expect(within(grupo).getByText('Firmará con validación de identidad')).toBeInTheDocument();
  });

  it('sin baúl y sin mecanismo: biométrica pendiente y canvas de firma sin hash inventado', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_NATURAL_SIN_MECANISMO]);
    renderStep({ modalidad: 'matricula_inicial' });
    const grupo = await screen.findByRole('group', { name: 'Biométrica Comprador' });

    expect(within(grupo).getByText('Pendiente de validación')).toBeInTheDocument();
    expect(within(grupo).getByText('Firma electrónica')).toBeInTheDocument();
    expect(within(grupo).queryByText(/^Hash:/)).toBeNull();
  });

  it('con validation aprobada + certificateHash: muestra el hash', async () => {
    vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
      validations: [APROBADA_CON_HASH],
      provider: 'kyverum',
    });
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_JURIDICA_IDENTIDAD]);
    renderStep({ modalidad: 'matricula_inicial' });
    const grupo = await screen.findByRole('group', { name: 'Biométrica Comprador' });

    expect(within(grupo).getByText('Hash: sha256:abc123certificado')).toBeInTheDocument();
  });

  it('aprobada sin certificateHash: muestra guión, no inventa hash', async () => {
    vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
      validations: [APROBADA_SIN_HASH],
      provider: 'kyverum',
    });
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_JURIDICA_IDENTIDAD]);
    renderStep({ modalidad: 'matricula_inicial' });
    const grupo = await screen.findByRole('group', { name: 'Biométrica Comprador' });

    expect(within(grupo).getByText('Hash: —')).toBeInTheDocument();
    expect(within(grupo).queryByText(/sha256:/)).toBeNull();
  });
});

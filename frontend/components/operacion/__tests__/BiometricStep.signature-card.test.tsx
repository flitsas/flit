// Composición de dos tarjetas por parte (paridad con la referencia del diseño: `MatriculaInicial`
// Step4, líneas 904-950). La tarjeta de firma electrónica es INFORMACIÓN (¿esta parte ya tiene firma
// vigente?) y la biométrica es ACCIÓN (¿hace falta pedirla?); no son excluyentes entre sí — lo
// excluyente es si la biométrica hace falta, no si ambas tarjetas se muestran.
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

/** Localiza la tarjeta de firma ("Firma electrónica") ancestra al título dado, sin depender de un
 *  `data-testid` nuevo: la card es la propia `WIZARD_CARD` (`rounded-2xl`) más cercana, igual patrón
 *  que ya usan otras suites del repo (`tramite-wizard.test.tsx`). */
function signatureCardOf(heading: HTMLElement): HTMLElement {
  const card = heading.closest('div.rounded-2xl');
  if (!card) throw new Error('No se encontró la tarjeta de firma electrónica.');
  return card as HTMLElement;
}

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

describe('BiometricStep — tarjeta de firma electrónica por parte', () => {
  it('aparece una tarjeta "Firma electrónica" por cada parte (traspaso: vendedor y comprador)', async () => {
    renderStep();
    await screen.findByRole('group', { name: 'Biométrica Vendedor' });

    const firmas = screen.getAllByText('Firma electrónica');
    expect(firmas).toHaveLength(2);
  });

  it('cubierta por el baúl: badge "Firma electrónica activa" (tono éxito)', async () => {
    renderStep({ vaultCoveredPartes: ['comprador'] });
    const compradorGrupo = await screen.findByRole('group', { name: 'Biométrica Comprador' });
    void compradorGrupo;

    const [firmaComprador] = screen.getAllByText('Firma electrónica').slice(-1);
    const card = signatureCardOf(firmaComprador);
    expect(within(card).getByText('Firma electrónica activa')).toBeInTheDocument();
    expect(
      within(card).getByRole('status', { name: 'Estado: Firma electrónica activa' }),
    ).toBeInTheDocument();
  });

  it('mecanismoFirma "identidad": badge que dice que firmará con el sello de la validación de identidad (tono info)', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_JURIDICA_IDENTIDAD]);
    renderStep({ modalidad: 'matricula_inicial' });
    await screen.findByRole('group', { name: 'Biométrica Comprador' });

    const firma = screen.getByText('Firma electrónica');
    const card = signatureCardOf(firma);
    expect(within(card).getByText('Firmará con validación de identidad')).toBeInTheDocument();
    expect(
      within(card).getByText(/firmará con el sello de la validación de identidad/i),
    ).toBeInTheDocument();
  });

  it('sin dato de firma: badge "Sin firma registrada" (tono neutral)', async () => {
    vi.mocked(tramitesClient.getActors).mockResolvedValue([ACTOR_NATURAL_SIN_MECANISMO]);
    renderStep({ modalidad: 'matricula_inicial' });
    await screen.findByRole('group', { name: 'Biométrica Comprador' });

    const firma = screen.getByText('Firma electrónica');
    const card = signatureCardOf(firma);
    expect(within(card).getByText('Sin firma registrada')).toBeInTheDocument();
  });

  it('con cobertura por baúl: el grupo muestra el aviso de baúl (izquierda) y la firma activa (derecha)', async () => {
    renderStep({ modalidad: 'matricula_inicial', vaultCoveredPartes: ['comprador'] });

    const compradorGrupo = await screen.findByRole('group', { name: 'Biométrica Comprador' });
    // Izquierda (columna de identidad y acción): VaultCoveredView explica que no hace falta biométrica.
    expect(within(compradorGrupo).getByText('Firma electrónica (baúl)')).toBeInTheDocument();
    expect(
      within(compradorGrupo).getByText(/No requiere validación biométrica\./),
    ).toBeInTheDocument();

    // Derecha (columna de firma): badge "Firma electrónica activa" dentro del canvas de firma.
    const firma = screen.getByText('Firma electrónica');
    const signatureCard = signatureCardOf(firma);
    expect(within(signatureCard).getByText('Firma electrónica activa')).toBeInTheDocument();
  });
});

// HU #10646 — estado "cubierto por el baúl": un actor jurídico (NIT) con firma electrónica vigente en
// el baúl deja su identidad satisfecha server-side; BiometricStep debe presentarlo como "Firma
// electrónica (baúl)" y omitir toda la biométrica, sin afectar a las demás partes.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import { BiometricStep } from '../BiometricStep';
import { WizardReadOnlyProvider } from '../WizardReadOnlyContext';

// El estado biométrico expone la cobertura por baúl en `firmaBaulPartes` (HU #11014). Por defecto se
// devuelve vacío para ejercitar la señal optimista de la prop `vaultCoveredPartes`; los casos
// server-driven lo sobrescriben.
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getBiometricState: vi.fn(),
    // El paso consulta además los actores para poder identificar a la persona en la cabecera de
    // cada tarjeta. Es un refuerzo visual y su fallo no rompe nada en producción, pero sin el mock
    // el propio import revienta con "getActors is not a function".
    getActors: vi.fn().mockResolvedValue([]),
  },
}));

import { tramitesClient } from '@/lib/api/tramites-client';

vi.mock('@/lib/api/client', () => ({ getToken: () => null }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => null,
  isSuperAdmin: () => false,
}));

function renderStep(vaultCoveredPartes: Array<'comprador' | 'vendedor'>) {
  return render(
    <WizardReadOnlyProvider readOnly={false}>
      <BiometricStep
        instanceId="inst-1"
        modalidad="traspaso"
        vaultCoveredPartes={vaultCoveredPartes}
      />
    </WizardReadOnlyProvider>,
  );
}

describe('BiometricStep — cobertura por baúl (HU #10646)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
      validations: [],
      provider: 'mock',
    });
  });

  it('presenta la parte NIT como "Firma electrónica (baúl)" sin botones de biométrica', async () => {
    renderStep(['comprador']);

    const compradorCard = await screen.findByRole('group', { name: /Biométrica Comprador/i });
    expect(within(compradorCard).getByText('Firma electrónica (baúl)')).toBeInTheDocument();
    // No se ofrece iniciar/simular la validación para la parte cubierta por el baúl.
    expect(
      within(compradorCard).queryByRole('button', { name: /validación de identidad/i }),
    ).not.toBeInTheDocument();
  });

  it('no altera a las partes no cubiertas: el vendedor sigue con su acción de validación', async () => {
    renderStep(['comprador']);

    const vendedorCard = await screen.findByRole('group', { name: /Biométrica Vendedor/i });
    // La parte natural conserva el flujo normal (estado vacío → acción de iniciar).
    expect(within(vendedorCard).queryByText('Firma electrónica (baúl)')).not.toBeInTheDocument();
    await waitFor(() =>
      expect(within(vendedorCard).getByText(/Aún no se ha iniciado la validación/i)).toBeInTheDocument(),
    );
  });

  it('sin partes cubiertas, ninguna tarjeta muestra el estado del baúl', async () => {
    renderStep([]);
    await screen.findByRole('group', { name: /Biométrica Comprador/i });
    expect(screen.queryByText('Firma electrónica (baúl)')).not.toBeInTheDocument();
  });

  // Al reabrir el trámite desde el listado no hubo `ensureIdentity`, así que la prop llega vacía: si
  // el paso no leyera `firmaBaulPartes` del backend, la parte se rotularía como «Identidad
  // verificada» pese a haber firmado desde el baúl.
  it('al reabrir el trámite la cobertura se toma del backend, sin la señal del registro', async () => {
    vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
      validations: [
        {
          id: 'val-1',
          partyRole: 'comprador',
          status: 'aprobado',
          score: 98,
          name: 'Comercializadora del Valle SAS',
        },
      ],
      provider: 'mock',
      firmaBaulPartes: ['comprador'],
    } as never);

    renderStep([]);

    const compradorCard = await screen.findByRole('group', { name: /Biométrica Comprador/i });
    expect(within(compradorCard).getByText('Firma electrónica (baúl)')).toBeInTheDocument();
    // La validación referenciada existe, pero no es lo que se plasma: no debe rotularse como verificada.
    expect(within(compradorCard).queryByText(/Identidad verificada/i)).not.toBeInTheDocument();
  });
});

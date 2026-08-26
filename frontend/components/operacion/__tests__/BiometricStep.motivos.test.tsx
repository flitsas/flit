// HU #11666 — la tarjeta de la parte explica por qué NO se envió la validación de identidad y qué
// hacer al respecto. El motivo lo tipifica el backend (HU #11665, campo `motivosNoEnvio`); aquí se
// verifica cómo lo presenta el paso: bloqueo con acción, bloqueo de ambiente sin acción,
// información neutra, los 4 estados de UI y que sin motivo la tarjeta no cambia.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BiometricStep } from '../BiometricStep';
import { WizardReadOnlyProvider } from '../WizardReadOnlyContext';
import type { EnvioValidacionMotivo } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getBiometricState: vi.fn(),
    getActors: vi.fn(),
  },
  getIdentitySendConflict: () => null,
}));

vi.mock('@/lib/api/client', () => ({ getToken: () => null }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => null,
  isSuperAdmin: () => false,
}));

import { tramitesClient } from '@/lib/api/tramites-client';

// Uso de ejemplo: renderStep([{ parte: 'comprador', codigo: 'rl_sin_correo', informativo: false }])
function renderStep(
  motivosNoEnvio: EnvioValidacionMotivo[],
  onIrAActores?: (parte: 'comprador' | 'vendedor') => void,
) {
  vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
    validations: [],
    provider: 'mock',
    motivosNoEnvio,
  });
  return render(
    <WizardReadOnlyProvider readOnly={false}>
      <BiometricStep instanceId="inst-1" modalidad="traspaso" onIrAActores={onIrAActores} />
    </WizardReadOnlyProvider>,
  );
}

const cardComprador = () => screen.findByRole('group', { name: /Biométrica Comprador/i });

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(tramitesClient.getActors).mockResolvedValue([]);
});

describe('BiometricStep — motivo de no envío (HU #11666)', () => {
  it('AC1 — motivo bloqueante corregible: muestra el texto y ofrece llevar al paso de actores', async () => {
    const user = userEvent.setup();
    const onIrAActores = vi.fn();
    renderStep([{ parte: 'comprador', codigo: 'rl_sin_correo', informativo: false }], onIrAActores);

    const card = await cardComprador();
    expect(within(card).getByText('Falta el correo del representante legal')).toBeInTheDocument();

    const accion = within(card).getByRole('button', {
      name: /completar datos del representante legal/i,
    });
    await user.click(accion);
    expect(onIrAActores).toHaveBeenCalledWith('comprador');
  });

  it('AC1 — el motivo se pinta solo en la tarjeta de la parte que lo reporta', async () => {
    renderStep([{ parte: 'comprador', codigo: 'rl_sin_documento', informativo: false }]);

    await cardComprador();
    const vendedor = screen.getByRole('group', { name: /Biométrica Vendedor/i });
    expect(within(vendedor).queryByText(/representante legal/i)).not.toBeInTheDocument();
  });

  it('AC2 — motivo de ambiente: lo explica y NO ofrece acción correctiva', async () => {
    renderStep([{ parte: 'comprador', codigo: 'proveedor_no_envia', informativo: false }], vi.fn());

    const card = await cardComprador();
    expect(within(card).getByText(/no emite envíos/i)).toBeInTheDocument();
    expect(
      within(card).queryByRole('button', { name: /completar datos del representante legal/i }),
    ).not.toBeInTheDocument();
  });

  it('AC3 — motivo informativo: tono neutro (role="status"), no alerta ni corrección', async () => {
    renderStep([{ parte: 'comprador', codigo: 'cubierto_por_baul', informativo: true }], vi.fn());

    const card = await cardComprador();
    const aviso = within(card).getByText('No hace falta enviar la validación').closest('[role]');
    expect(aviso).toHaveAttribute('role', 'status');
    expect(aviso).toHaveAttribute('aria-live', 'polite');
    expect(
      within(card).queryByRole('button', { name: /completar datos del representante legal/i }),
    ).not.toBeInTheDocument();
  });

  it('AC3 — representante_utilizable también es información, no error', async () => {
    renderStep([{ parte: 'vendedor', codigo: 'representante_utilizable', informativo: true }]);

    const card = await screen.findByRole('group', { name: /Biométrica Vendedor/i });
    expect(within(card).getByText(/aprobada y vigente/i)).toBeInTheDocument();
    expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('AC4 — estado cargando: esqueleto con aria-busy antes de la primera respuesta', async () => {
    let resolve: (v: unknown) => void = () => {};
    vi.mocked(tramitesClient.getBiometricState).mockReturnValue(
      new Promise((res) => (resolve = res)) as never,
    );

    render(
      <WizardReadOnlyProvider readOnly={false}>
        <BiometricStep instanceId="inst-1" modalidad="traspaso" />
      </WizardReadOnlyProvider>,
    );

    const skeleton = screen.getByRole('status', { name: /cargando validaciones de identidad/i });
    expect(skeleton).toHaveAttribute('aria-busy', 'true');

    resolve({ validations: [], provider: 'mock' });
    await waitFor(() =>
      expect(
        screen.queryByRole('status', { name: /cargando validaciones de identidad/i }),
      ).not.toBeInTheDocument(),
    );
  });

  it('AC4 — estado vacío: sin validación ni motivo, la parte queda "Sin iniciar" sin avisos', async () => {
    renderStep([]);

    const card = await cardComprador();
    expect(within(card).getByText('Sin iniciar')).toBeInTheDocument();
    expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('AC4 — estado error: mensaje y acción de reintentar', async () => {
    vi.mocked(tramitesClient.getBiometricState).mockRejectedValue(new Error('Falla de red'));

    render(
      <WizardReadOnlyProvider readOnly={false}>
        <BiometricStep instanceId="inst-1" modalidad="traspaso" heading="Identidad" />
      </WizardReadOnlyProvider>,
    );

    expect(await screen.findByText('Falla de red')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /actualizar estado biométrico/i }),
    ).toBeInTheDocument();
  });

  it('AC5 — el aviso bloqueante se anuncia con role="alert" y aria-live="polite"', async () => {
    renderStep([{ parte: 'comprador', codigo: 'rl_sin_documento', informativo: false }]);

    const card = await cardComprador();
    const aviso = within(card).getByRole('alert');
    expect(aviso).toHaveAttribute('aria-live', 'polite');
    // El significado no depende solo del color: el título nombra la situación.
    expect(aviso).toHaveTextContent('Falta el documento del representante legal');
  });

  it('AC6 (negativo) — sin motivos la tarjeta no agrega ningún bloque', async () => {
    renderStep([]);

    const card = await cardComprador();
    expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(card).queryByText(/representante legal/i)).not.toBeInTheDocument();
  });

  it('contrato — respuesta sin `motivosNoEnvio` (campo opcional) no rompe el paso', async () => {
    vi.mocked(tramitesClient.getBiometricState).mockResolvedValue({
      validations: [],
      provider: 'mock',
    });

    render(
      <WizardReadOnlyProvider readOnly={false}>
        <BiometricStep instanceId="inst-1" modalidad="traspaso" />
      </WizardReadOnlyProvider>,
    );

    const card = await cardComprador();
    expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('contrato — el flag `informativo` del backend manda sobre un código desconocido', async () => {
    renderStep([{ parte: 'comprador', codigo: 'motivo_futuro', informativo: true }]);

    const card = await cardComprador();
    expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(card).getByText(/motivo_futuro/)).toBeInTheDocument();
  });
});

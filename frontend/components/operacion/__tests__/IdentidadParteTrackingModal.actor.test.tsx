// ADR-0053 — tracking de identidad por copropietario: desde el desplegable de la tabla, el modal
// muestra solo las validaciones de ESA persona (por ordinal; las antiguas sin ordinal, por
// documento). Sin actor, el modal sigue mostrando todo el lado, como antes.
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

const mocks = vi.hoisted(() => ({ listBiometricExpediente: vi.fn() }));

vi.mock('@/lib/api/tramites-client', () => ({ tramitesClient: mocks }));
vi.mock('@/components/operacion/IdentidadLecturaHumana', () => ({
  IdentidadLecturaHumana: ({ validation }: { validation: { id: string } }) => (
    <p data-testid="lectura">{validation.id}</p>
  ),
}));

import { IdentidadParteTrackingModal } from '@/components/operacion/IdentidadParteTrackingModal';

function validacion(id: string, ordinal: number | null, documentNumber: string) {
  return { id, provider: 'kyverum', partyRole: 'comprador', ordinal, documentNumber, status: 'pendiente' };
}

function preparar() {
  mocks.listBiometricExpediente.mockResolvedValue({
    validations: [
      validacion('val-ana', 1, '1001'),
      validacion('val-luis', 2, '1002'),
      // Validación antigua sin ordinal: se empareja por documento.
      validacion('val-luis-antigua', null, '1002'),
    ],
    firmaBaulPartes: [],
    firmaBaulActores: [],
  });
}

async function idsPintados() {
  const filas = await screen.findAllByTestId('lectura');
  return filas.map((f) => f.textContent);
}

describe('IdentidadParteTrackingModal — por copropietario', () => {
  it('con actor muestra solo las validaciones de esa persona', async () => {
    preparar();
    render(
      <IdentidadParteTrackingModal
        open
        instanceId="inst-1"
        parte="comprador"
        rotulo="Comprador 2"
        actor={{ ordinal: 2, numeroDocumento: '1002', nombre: 'Luis Dos' }}
        onClose={() => {}}
      />,
    );

    expect(await idsPintados()).toEqual(['val-luis', 'val-luis-antigua']);
    expect(screen.getByText('Validación de identidad · Comprador 2 · Luis Dos')).toBeTruthy();
  });

  it('sin actor muestra todas las validaciones de la parte', async () => {
    preparar();
    render(
      <IdentidadParteTrackingModal
        open
        instanceId="inst-1"
        parte="comprador"
        rotulo="Comprador"
        onClose={() => {}}
      />,
    );

    expect(await idsPintados()).toEqual(['val-ana', 'val-luis', 'val-luis-antigua']);
  });
});

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FirmaPosteriorSection } from '@/components/operacion/FirmaPosteriorSection';

/**
 * HU #11197 — opción de firmar a posteriori en el registro. Se monta la sección real y se afirma sobre
 * lo que el gestor ve y puede hacer, no sobre el estado interno del componente.
 */

const getFirmaPosterior = vi.fn();
const marcarFirmaPosterior = vi.fn();

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getFirmaPosterior: (...args: unknown[]) => getFirmaPosterior(...args),
    marcarFirmaPosterior: (...args: unknown[]) => marcarFirmaPosterior(...args),
  },
}));

/** Respuesta del backend para una parte. */
const estado = (over: Partial<{ aplica: boolean; marcado: boolean; representanteNombre: string | null; marcadoAt: string | null }> = {}) => ({
  aplica: false,
  marcado: false,
  representanteNombre: null,
  marcadoAt: null,
  ...over,
});

/** Configura qué responde el backend por parte (vendedor/comprador). */
function backend(porParte: Record<string, ReturnType<typeof estado>>) {
  getFirmaPosterior.mockImplementation((_id: string, parte: string) =>
    Promise.resolve(porParte[parte] ?? estado()),
  );
}

describe('HU #11197 — firma a posteriori en el registro', () => {
  beforeEach(() => {
    getFirmaPosterior.mockReset();
    marcarFirmaPosterior.mockReset();
  });

  it('AC1 con identidad y firma vencidas se ofrece la opcion de firmar a posteriori', async () => {
    backend({ comprador: estado({ aplica: true, representanteNombre: 'Ana Representante' }) });
    render(<FirmaPosteriorSection instanceId="i-1" />);

    expect(await screen.findByRole('button', { name: /Firmar más adelante/i })).toBeInTheDocument();
    expect(screen.getByText(/Ana Representante/)).toBeInTheDocument();
  });

  it('AC2 con firma o identidad utilizable no se ofrece la opcion', async () => {
    backend({});
    const { container } = render(<FirmaPosteriorSection instanceId="i-1" />);

    await waitFor(() => expect(getFirmaPosterior).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole('button', { name: /Firmar más adelante/i })).not.toBeInTheDocument();
    // Sin partes aplicables la sección no se pinta: una sección vacía solo añade ruido al paso.
    expect(container.querySelector('[data-testid="firma-posterior"]')).toBeNull();
  });

  it('AC3 se informa el metodo y que se aplicara cuando el representante la complete', async () => {
    backend({ comprador: estado({ aplica: true }) });
    render(<FirmaPosteriorSection instanceId="i-1" />);

    expect(await screen.findByText(/validación de identidad del representante legal/i)).toBeInTheDocument();
    expect(screen.getByText(/se aplicará sola cuando él la complete/i)).toBeInTheDocument();
  });

  it('AC4 tras marcar, el tramite se ve como pendiente de firma', async () => {
    const user = userEvent.setup();
    backend({ comprador: estado({ aplica: true }) });
    marcarFirmaPosterior.mockImplementation(() => {
      backend({ comprador: estado({ aplica: true, marcado: true, marcadoAt: '2026-08-01' }) });
      return Promise.resolve(estado({ aplica: true, marcado: true }));
    });
    render(<FirmaPosteriorSection instanceId="i-1" />);

    await user.click(await screen.findByRole('button', { name: /Firmar más adelante/i }));

    expect(await screen.findByText('Pendiente de firma')).toBeInTheDocument();
    expect(marcarFirmaPosterior).toHaveBeenCalledWith('i-1', 'comprador', undefined);
    // Ya marcado, el botón desaparece: volver a pulsarlo no aporta nada.
    expect(screen.queryByRole('button', { name: /Firmar más adelante/i })).not.toBeInTheDocument();
  });

  it('AC4 un tramite ya marcado se ve pendiente al volver, sin volver a marcarlo', async () => {
    backend({ vendedor: estado({ aplica: false, marcado: true, marcadoAt: '2026-08-01' }) });
    render(<FirmaPosteriorSection instanceId="i-1" />);

    expect(await screen.findByText('Pendiente de firma')).toBeInTheDocument();
    expect(marcarFirmaPosterior).not.toHaveBeenCalled();
  });

  it('en solo lectura la opcion se ve pero no se puede marcar', async () => {
    backend({ comprador: estado({ aplica: true }) });
    render(<FirmaPosteriorSection instanceId="i-1" readOnly />);

    expect(await screen.findByRole('button', { name: /Firmar más adelante/i })).toBeDisabled();
  });

  it('una parte que aun no existe no rompe la seccion', async () => {
    // El backend responde 404 para el vendedor todavía no registrado; el comprador sí aplica.
    getFirmaPosterior.mockImplementation((_id: string, parte: string) =>
      parte === 'vendedor'
        ? Promise.reject(new Error('404'))
        : Promise.resolve(estado({ aplica: true })),
    );
    render(<FirmaPosteriorSection instanceId="i-1" />);

    expect(await screen.findByRole('button', { name: /Firmar más adelante/i })).toBeInTheDocument();
  });
});

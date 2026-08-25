import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  listPublishedProcedureTypes: vi.fn(),
  getConsultationConfig: vi.fn(),
  push: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listPublishedProcedureTypes: mocks.listPublishedProcedureTypes,
    getConsultationConfig: mocks.getConsultationConfig,
  },
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mocks.push, replace: vi.fn(), prefetch: vi.fn() }),
}));

// El listado entero no aporta a lo que se prueba aquí —que el disparador abre un diálogo en vez de
// navegar— y arrastra tabla, KPIs y sus propias llamadas. Se sustituye por el botón.
vi.mock('@/components/operacion/OperacionView', () => ({
  OperacionView: ({ onNewTramite }: { onNewTramite: () => void }) => (
    <button type="button" onClick={onNewTramite}>
      Nuevo trámite
    </button>
  ),
}));

import TramitesPage from '@/app/tramites/page';

/**
 * «Nuevo trámite» abre la elección EN MODAL sobre el listado, no navegando a otra pantalla.
 *
 * Es el patrón de FLIT para lo que se lanza desde un listado, y sobre todo evita que cancelar cueste
 * el estado de la vista: al navegar y volver se perdían filtros, página y scroll.
 */
describe('/tramites — elección del trámite en modal', () => {
  beforeEach(() => {
    mocks.listPublishedProcedureTypes.mockReset();
    mocks.getConsultationConfig.mockReset();
    mocks.push.mockReset();
    mocks.getConsultationConfig.mockResolvedValue({ blockProcedureFamily: null });
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      {
        id: 'BLINDAJE',
        code: 'BLINDAJE',
        name: 'Blindaje',
        family: 'OTROS',
        publicationStatus: 'published',
        isActive: true,
        wizardEnabled: true,
        publishedAt: null,
      },
    ]);
  });

  it('el disparador abre un diálogo y NO navega', async () => {
    const user = userEvent.setup();
    render(<TramitesPage />);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Nuevo trámite' }));

    const dialogo = await screen.findByRole('dialog');
    expect(dialogo).toBeInTheDocument();
    // Lo que separa el modal de la ruta: el listado sigue montado detrás.
    expect(screen.getByRole('button', { name: 'Nuevo trámite' })).toBeInTheDocument();
    expect(mocks.push).not.toHaveBeenCalled();
  });

  it('el diálogo se anuncia con el título de la elección', async () => {
    const user = userEvent.setup();
    render(<TramitesPage />);
    await user.click(screen.getByRole('button', { name: 'Nuevo trámite' }));

    expect(await screen.findByRole('dialog', { name: /Selecciona el tipo de trámite/ }))
      .toBeInTheDocument();
  });

  it('cancelar cierra el diálogo sin salir del listado', async () => {
    const user = userEvent.setup();
    render(<TramitesPage />);
    await user.click(screen.getByRole('button', { name: 'Nuevo trámite' }));
    await screen.findByRole('dialog');

    await user.click(await screen.findByRole('button', { name: 'Cancelar' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(mocks.push).not.toHaveBeenCalled();
  });

  it('elegir un trámite sí navega al asistente con el code del tipo', async () => {
    const user = userEvent.setup();
    render(<TramitesPage />);
    await user.click(screen.getByRole('button', { name: 'Nuevo trámite' }));

    await user.click(await screen.findByRole('button', { name: /Otros trámites/ }));
    await user.click(await screen.findByRole('button', { name: 'Blindaje' }));

    expect(mocks.push).toHaveBeenCalledWith('/tramites/nuevo/BLINDAJE');
  });
});

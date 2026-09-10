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

vi.mock('@/components/operacion/OperacionView', () => ({
  OperacionView: ({ onNewTramite }: { onNewTramite: () => void }) => (
    <button type="button" onClick={onNewTramite}>
      Nuevo trámite
    </button>
  ),
}));

import TramitesPage from '@/app/tramites/page';

/**
 * «Nuevo trámite» abre el selector mockup EN MODAL sobre el listado.
 */
describe('/tramites — modal Nuevo trámite (mockup)', () => {
  beforeEach(() => {
    mocks.listPublishedProcedureTypes.mockReset();
    mocks.getConsultationConfig.mockReset();
    mocks.push.mockReset();
    mocks.getConsultationConfig.mockResolvedValue({ blockProcedureFamily: null });
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      {
        id: 'MATRICULA_NUEVA',
        code: 'MATRICULA_NUEVA',
        name: 'Matrícula inicial',
        family: 'MATRICULAS',
        publicationStatus: 'published',
        isActive: true,
        wizardEnabled: true,
        publishedAt: null,
      },
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
      {
        id: 'TRASPASO_STANDARD',
        code: 'TRASPASO_STANDARD',
        name: 'Traspaso',
        family: 'TRASPASO',
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
    expect(screen.getByRole('button', { name: 'Nuevo trámite' })).toBeInTheDocument();
    expect(mocks.push).not.toHaveBeenCalled();
  });

  it('el diálogo se anuncia con el título Nuevo trámite', async () => {
    const user = userEvent.setup();
    render(<TramitesPage />);
    await user.click(screen.getByRole('button', { name: 'Nuevo trámite' }));

    expect(await screen.findByRole('dialog', { name: /Nuevo trámite/ })).toBeInTheDocument();
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

  it('iniciar con matrícula navega al code del catálogo', async () => {
    const user = userEvent.setup();
    render(<TramitesPage />);
    await user.click(screen.getByRole('button', { name: 'Nuevo trámite' }));

    await user.click(await screen.findByRole('button', { name: /Matrícula Inicial/ }));
    await user.click(screen.getByRole('button', { name: 'Iniciar trámite' }));

    expect(mocks.push).toHaveBeenCalledWith('/tramites/nuevo/MATRICULA_NUEVA');
  });

  it('Otros exige subtipo antes de iniciar y luego navega', async () => {
    const user = userEvent.setup();
    render(<TramitesPage />);
    await user.click(screen.getByRole('button', { name: 'Nuevo trámite' }));

    await user.click(await screen.findByRole('button', { name: /Otros Trámites/ }));
    expect(screen.getByRole('button', { name: 'Iniciar trámite' })).toBeDisabled();

    await user.selectOptions(screen.getByRole('combobox'), 'BLINDAJE');
    await user.click(screen.getByRole('button', { name: 'Iniciar trámite' }));

    expect(mocks.push).toHaveBeenCalledWith('/tramites/nuevo/BLINDAJE');
  });
});

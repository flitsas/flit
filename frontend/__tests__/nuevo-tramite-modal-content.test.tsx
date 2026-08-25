import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { NuevoTramiteModalContent } from '@/components/operacion/NuevoTramiteModalContent';

const mocks = vi.hoisted(() => ({ listPublishedProcedureTypes: vi.fn() }));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: { listPublishedProcedureTypes: mocks.listPublishedProcedureTypes },
}));

function tipo(
  code: string,
  name: string,
  family: ProcedureTypeSummary['family'],
  wizardEnabled = true,
): ProcedureTypeSummary {
  return {
    id: code,
    code,
    name,
    family,
    publicationStatus: 'published',
    isActive: true,
    wizardEnabled,
    publishedAt: null,
  };
}

describe('NuevoTramiteModalContent', () => {
  beforeEach(() => {
    mocks.listPublishedProcedureTypes.mockReset();
  });

  it('muestra las tres tarjetas del mockup cuando hay tipos en cada familia', async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS'),
      tipo('TRASPASO_STANDARD', 'Traspaso', 'TRASPASO'),
      tipo('BLINDAJE', 'Blindaje', 'OTROS'),
    ]);

    render(<NuevoTramiteModalContent onElegir={vi.fn()} tituloEnContenedor />);

    expect(await screen.findByRole('button', { name: /Matrícula Inicial/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Traspaso/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Otros Trámites/ })).toBeInTheDocument();
  });

  it('deshabilita la tarjeta bloqueada por compañía', async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo('BLINDAJE', 'Blindaje', 'OTROS'),
    ]);

    render(
      <NuevoTramiteModalContent
        onElegir={vi.fn()}
        bloqueadas={{ otros: true }}
        tituloEnContenedor
      />,
    );

    const boton = await screen.findByRole('button', { name: /Otros Trámites/ });
    expect(boton).toBeDisabled();
    expect(boton).toHaveTextContent(/no habilitado para tu compañía/);
  });

  it('al vacío de catálogo muestra mensaje, no rejilla vacía', async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo('BLINDAJE', 'Blindaje', 'OTROS', false),
    ]);

    render(<NuevoTramiteModalContent onElegir={vi.fn()} tituloEnContenedor />);

    await waitFor(() =>
      expect(screen.getByText(/No hay tipos de trámite habilitados/)).toBeInTheDocument(),
    );
  });

  it('Iniciar emite el code resuelto', async () => {
    const onElegir = vi.fn();
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS'),
      tipo('TRASPASO_STANDARD', 'Traspaso', 'TRASPASO'),
      tipo('BLINDAJE', 'Blindaje', 'OTROS'),
    ]);
    const user = userEvent.setup();

    render(
      <NuevoTramiteModalContent onElegir={onElegir} onCancelar={vi.fn()} tituloEnContenedor />,
    );

    await user.click(await screen.findByRole('button', { name: /Matrícula Inicial/ }));
    await user.click(screen.getByRole('button', { name: 'Iniciar trámite' }));

    expect(onElegir).toHaveBeenCalledWith('MATRICULA_NUEVA');
  });

  it('Cancelar invoca onCancelar', async () => {
    const onCancelar = vi.fn();
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS'),
    ]);
    const user = userEvent.setup();

    render(
      <NuevoTramiteModalContent onElegir={vi.fn()} onCancelar={onCancelar} tituloEnContenedor />,
    );

    await user.click(await screen.findByRole('button', { name: 'Cancelar' }));
    expect(onCancelar).toHaveBeenCalledOnce();
  });
});

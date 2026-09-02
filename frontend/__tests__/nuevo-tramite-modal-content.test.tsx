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

    const desplegable = await screen.findByRole('button', { name: /Otros Trámites/ });
    expect(desplegable).toBeDisabled();
    expect(screen.getByText(/No habilitado para tu compañía/i)).toBeInTheDocument();
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
    await user.click(screen.getByRole('option', { name: 'Matrícula Tradicional' }));
    await user.click(screen.getByRole('button', { name: 'Iniciar trámite' }));

    expect(onElegir).toHaveBeenCalledWith('MATRICULA_NUEVA');
  });

  // La franja informativa se reserva SIEMPRE para que el modal no salte 56px al elegir; lo que
  // cambia es si tiene texto. Por eso se comprueba el texto, no la presencia del bloque.
  it('la franja informativa explica la configuración elegida y cambia con ella', async () => {
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS'),
    ]);
    const user = userEvent.setup();

    render(<NuevoTramiteModalContent onElegir={vi.fn()} tituloEnContenedor />);

    // En reposo la franja está, pero muda.
    expect(screen.queryByText(/Matrícula tradicional:/)).not.toBeInTheDocument();

    await user.click(await screen.findByRole('button', { name: /Matrícula Inicial/ }));
    await user.click(screen.getByRole('option', { name: 'Matrícula Tradicional' }));
    expect(screen.getByText(/Matrícula tradicional:/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Matrícula Inicial/ }));
    await user.click(screen.getByRole('option', { name: 'Matrícula Leasing' }));
    expect(screen.getByText(/Matrícula tipo Leasing:/)).toBeInTheDocument();
    expect(screen.queryByText(/Matrícula tradicional:/)).not.toBeInTheDocument();
  });

  // Configurar una tarjeta tiene que LIMPIAR la de las otras: si no, un leasing marcado antes
  // viajaba al resolver junto a un traspaso que es lo que en realidad se está creando.
  it('elegir en otra tarjeta reemplaza la selección anterior', async () => {
    const onElegir = vi.fn();
    mocks.listPublishedProcedureTypes.mockResolvedValue([
      tipo('MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS'),
      tipo('TRASPASO_STANDARD', 'Traspaso', 'TRASPASO'),
    ]);
    const user = userEvent.setup();

    render(<NuevoTramiteModalContent onElegir={onElegir} tituloEnContenedor />);

    await user.click(await screen.findByRole('button', { name: /Matrícula Inicial/ }));
    await user.click(screen.getByRole('option', { name: 'Matrícula Leasing' }));

    await user.click(screen.getByRole('button', { name: /^Traspaso/ }));
    await user.click(screen.getByRole('option', { name: 'Traspaso Bilateral' }));

    // La matrícula vuelve a su placeholder: ya no hay nada elegido en esa tarjeta.
    expect(screen.getByRole('button', { name: /Matrícula Inicial/ })).toHaveTextContent(
      'Selecciona tipo',
    );

    await user.click(screen.getByRole('button', { name: 'Iniciar trámite' }));
    expect(onElegir).toHaveBeenCalledWith('TRASPASO_STANDARD');
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

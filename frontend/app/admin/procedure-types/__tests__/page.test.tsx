import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import AdminProcedureTypesPage from '../page';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

// FEATURE-08 / HU-FE-07 (#10842) — la página del Configurador monta el listado y alterna al
// asistente. Se moquea el hook de datos y se stubea el ParametrizationWizard (probado aparte)
// para verificar la lógica del contenedor: listar → Nuevo tipo → asistente → salir → listado.

const draft: ProcedureTypeSummary = {
  id: 'pt-1',
  code: 'TRASPASO_SIMPLE',
  name: 'Traspaso Simple',
  family: 'TRASPASO',
  publicationStatus: 'draft',
  isActive: true,
  publishedAt: null,
};

const hookApi = vi.hoisted(() => ({
  reload: vi.fn(),
  publish: vi.fn(),
}));

vi.mock('@/hooks/useProcedureTypes', () => ({
  useProcedureTypes: () => ({
    items: [draft],
    status: 'success' as const,
    error: null,
    reload: hookApi.reload,
    publish: hookApi.publish,
  }),
}));

// Stub del asistente: expone el editingId y un botón para simular la salida.
vi.mock('@/components/superadmin/ParametrizationWizard', () => ({
  ParametrizationWizard: ({
    editingId,
    onExit,
  }: {
    editingId?: string | null;
    onExit: (saved?: boolean) => void;
  }) => (
    <div>
      <span>WIZARD_STUB:{editingId ?? 'new'}</span>
      <button type="button" onClick={() => onExit(true)}>
        salir-guardando
      </button>
    </div>
  ),
}));

describe('AdminProcedureTypesPage (FE-07)', () => {
  it('renderiza el título y el listado de tipos', () => {
    render(<AdminProcedureTypesPage />);
    expect(
      screen.getByRole('heading', { name: /parametrización de trámites/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('TRASPASO_SIMPLE')).toBeInTheDocument();
  });

  it('al pulsar "Nuevo tipo" abre el asistente en modo nuevo', async () => {
    const user = userEvent.setup();
    render(<AdminProcedureTypesPage />);
    await user.click(screen.getByRole('button', { name: /crear un nuevo tipo de trámite/i }));
    expect(screen.getByText('WIZARD_STUB:new')).toBeInTheDocument();
  });

  it('al pulsar "Editar" en un borrador abre el asistente con su id', async () => {
    const user = userEvent.setup();
    render(<AdminProcedureTypesPage />);
    await user.click(screen.getByRole('button', { name: /editar traspaso simple/i }));
    expect(screen.getByText('WIZARD_STUB:pt-1')).toBeInTheDocument();
  });

  it('al salir del asistente guardando vuelve al listado y recarga', async () => {
    const user = userEvent.setup();
    render(<AdminProcedureTypesPage />);
    await user.click(screen.getByRole('button', { name: /crear un nuevo tipo de trámite/i }));
    await user.click(screen.getByRole('button', { name: /salir-guardando/i }));
    // Vuelve al listado (encabezado visible) y recarga los datos.
    expect(screen.getByRole('heading', { name: /parametrización de trámites/i })).toBeInTheDocument();
    expect(hookApi.reload).toHaveBeenCalled();
  });
});

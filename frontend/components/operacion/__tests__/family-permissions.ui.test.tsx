// HU #13665 (Feature #13636 F-F): la UI de trámites respeta Ver / Crear por familia.
//  AC1 — Ver sin Crear: se listan los traspasos y el asistente no ofrece crear traspaso.
//  AC2 — Sin Ver: la pestaña de la familia no aparece.
//  AC3 — Con ambos: todas las pestañas y todas las tarjetas de creación.
import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FamiliaTabs } from '../FamiliaTabs';
import { NuevoTramiteModalContent } from '../NuevoTramiteModalContent';
import { useFamilyAccess } from '@/hooks/useFamilyAccess';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

let token: string | null = null;
vi.mock('@/lib/api/client', () => ({ getToken: () => token }));

const tipo = (code: string, family: string) =>
  ({ id: code, code, name: code, family, wizardEnabled: true, publicationStatus: 'published' }) as unknown as ProcedureTypeSummary;
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listPublishedProcedureTypes: () =>
      Promise.resolve([tipo('MATRICULA_NUEVA', 'MATRICULAS'), tipo('TRASPASO_BILATERAL', 'TRASPASO'), tipo('DUPLICADO', 'OTROS')]),
    getCurrentProcedureTerms: () => Promise.resolve({ url: 'https://x.test/terminos' }),
    acceptProcedureTerms: vi.fn(),
  },
}));

function jwt(permissions: string[]) {
  const body = btoa(JSON.stringify({ sub: 'u1', roles: [], permissions }));
  return `h.${body}.s`;
}

function Tabs() {
  const { readable } = useFamilyAccess();
  return <FamiliaTabs value="" onChange={vi.fn()} familias={readable} />;
}

const ALL = [
  'matriculas.read', 'matriculas.create', 'traspasos.read', 'traspasos.create', 'otros-tramites.read', 'otros-tramites.create',
];

describe('Trámites por familia (HU #13665)', () => {
  beforeEach(() => {
    token = null;
  });

  it('AC1: Ver traspasos sin Crear → pestaña Traspaso visible y sin tarjeta «Traspaso» en el asistente', async () => {
    token = jwt(['tramites.read', 'tramites.create', 'traspasos.read', 'matriculas.read', 'matriculas.create']);
    const { unmount } = render(<Tabs />);
    expect(screen.getByRole('tab', { name: 'Traspaso' })).toBeInTheDocument();
    unmount();

    render(<NuevoTramiteModalContent onElegir={vi.fn()} tituloEnContenedor />);
    expect(await screen.findByText('Matrícula Inicial')).toBeInTheDocument();
    expect(screen.queryByText('Traspaso')).not.toBeInTheDocument();
    expect(screen.queryByText('Otros Trámites')).not.toBeInTheDocument();
  });

  it('AC2: sin matriculas.read no hay pestaña Matrículas', () => {
    token = jwt(['tramites.read', 'traspasos.read', 'otros-tramites.read']);
    render(<Tabs />);
    expect(screen.queryByRole('tab', { name: 'Matrículas' })).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Todos' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Traspaso' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Otros trámites' })).toBeInTheDocument();
  });

  it('AC2: sin Ver en ninguna familia no queda ninguna pestaña', () => {
    token = jwt(['tramites.read', 'matriculas.create']);
    render(<Tabs />);
    expect(screen.queryAllByRole('tab')).toHaveLength(0);
  });

  it('AC3: con Ver y Crear de las tres familias, todas las pestañas y las tres tarjetas', async () => {
    token = jwt(ALL);
    const { unmount } = render(<Tabs />);
    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Todos', 'Matrículas', 'Traspaso', 'Otros trámites']);
    unmount();

    render(<NuevoTramiteModalContent onElegir={vi.fn()} tituloEnContenedor />);
    await waitFor(() => expect(screen.getByText('Matrícula Inicial')).toBeInTheDocument());
    expect(screen.getByText('Traspaso')).toBeInTheDocument();
    expect(screen.getByText('Otros Trámites')).toBeInTheDocument();
  });

  it('compatibilidad: token anterior a la migración (solo tramites.read/create) muestra y crea las tres familias', async () => {
    token = jwt(['tramites.read', 'tramites.create']);
    render(<NuevoTramiteModalContent onElegir={vi.fn()} tituloEnContenedor />);
    await waitFor(() => expect(screen.getByText('Matrícula Inicial')).toBeInTheDocument());
    expect(screen.getByText('Traspaso')).toBeInTheDocument();
    expect(screen.getByText('Otros Trámites')).toBeInTheDocument();
  });
});

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CargaMasivaResultados } from '@/components/operacion/CargaMasivaResultados';
import type {
  BulkTramitesBatchDetail,
  BulkTramitesBatchSummary,
} from '@/lib/api/bulk-tramites-client';

// HU #13919 — la tabla de resultados del lote lleva la paginación estándar dentro del modal.
const push = vi.fn();
vi.mock('next/navigation', () => ({ useRouter: () => ({ push }) }));

vi.mock('@/lib/api/bulk-tramites-client', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api/bulk-tramites-client')>(
    '@/lib/api/bulk-tramites-client',
  );
  return {
    ...actual,
    bulkTramitesClient: { listarLotes: vi.fn(), detalleLote: vi.fn() },
  };
});

import { bulkTramitesClient } from '@/lib/api/bulk-tramites-client';

const LOTE: BulkTramitesBatchSummary = {
  id: 'b-120',
  templateType: 'matricula',
  sourceFilename: 'grande.xlsx',
  status: 'completed',
  totalRows: 120,
  createdAt: '2026-09-11T10:00:00Z',
  completedAt: '2026-09-11T10:02:00Z',
  counts: { created: 120, createdPending: 0, notCreated: 0, pendientes: 0 },
};

const DETALLE: BulkTramitesBatchDetail = {
  batch: LOTE,
  rows: Array.from({ length: 120 }, (_, i) => ({
    rowNumber: i + 1,
    identificador: `PLA${String(i + 1).padStart(3, '0')}`,
    outcome: 'created' as const,
    motivo: null,
    procedureInstanceId: `inst-${i + 1}`,
  })),
};

describe('CargaMasivaResultados — paginación estándar (HU #13919)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    vi.mocked(bulkTramitesClient.listarLotes).mockResolvedValue([LOTE]);
    vi.mocked(bulkTramitesClient.detalleLote).mockResolvedValue(DETALLE);
  });

  it('con 120 filas muestra 10 por página y ofrece 10/20/30/50', async () => {
    const user = userEvent.setup();
    render(<CargaMasivaResultados />);

    await screen.findByTestId('carga-masiva-resultados');
    expect(screen.getByTestId('carga-masiva-fila-1')).toBeInTheDocument();
    expect(screen.getByTestId('carga-masiva-fila-10')).toBeInTheDocument();
    expect(screen.queryByTestId('carga-masiva-fila-11')).not.toBeInTheDocument();
    expect(screen.getAllByTestId(/^carga-masiva-fila-\d+$/)).toHaveLength(10);
    expect(screen.getByText(/Mostrando 1–10 de 120/)).toBeInTheDocument();

    const selector = screen.getByRole('combobox', { name: 'Filas por página' });
    const opciones = within(selector).getAllByRole('option').map((o) => Number(o.textContent));
    expect(opciones).toEqual([10, 20, 30, 50]);

    await user.selectOptions(selector, '50');
    expect(screen.getAllByTestId(/^carga-masiva-fila-\d+$/)).toHaveLength(50);
  });
});

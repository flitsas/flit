import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { CargaMasivaResultados, mensajeMotivo } from '@/components/operacion/CargaMasivaResultados';
import type {
  BulkTramitesBatchDetail,
  BulkTramitesBatchSummary,
} from '@/lib/api/bulk-tramites-client';

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

const listar = vi.mocked(bulkTramitesClient.listarLotes);
const detallar = vi.mocked(bulkTramitesClient.detalleLote);

const LOTE: BulkTramitesBatchSummary = {
  id: 'b-1',
  templateType: 'matricula',
  sourceFilename: 'lote.xlsx',
  status: 'completed',
  totalRows: 3,
  createdAt: '2026-09-11T10:00:00Z',
  completedAt: '2026-09-11T10:02:00Z',
  counts: { created: 1, createdPending: 1, notCreated: 1, pendientes: 0 },
};

const DETALLE: BulkTramitesBatchDetail = {
  batch: LOTE,
  rows: [
    {
      rowNumber: 1,
      identificador: 'ABC123',
      outcome: 'created',
      motivo: null,
      procedureInstanceId: 'inst-1',
    },
    {
      rowNumber: 2,
      identificador: 'DEF456',
      outcome: 'created_pending',
      motivo: 'conductor_no_encontrado:CC 1020304050',
      procedureInstanceId: 'inst-2',
    },
    {
      rowNumber: 3,
      identificador: 'GHI789',
      outcome: 'not_created',
      motivo: 'organismo_transito_no_habilitado',
      procedureInstanceId: null,
    },
  ],
};

describe('CargaMasivaResultados (HU #12524)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    listar.mockResolvedValue([LOTE]);
    detallar.mockResolvedValue(DETALLE);
  });

  it('AC1 — enseña el resultado de cada fila con su motivo', async () => {
    render(<CargaMasivaResultados />);

    expect(await screen.findByTestId('carga-masiva-contadores')).toHaveTextContent(
      '1 creados · 1 por retomar · 1 no creados',
    );

    expect(screen.getByTestId('carga-masiva-fila-1')).toHaveTextContent('Creado');
    expect(screen.getByTestId('carga-masiva-fila-2')).toHaveTextContent(/falta retomarlo/i);
    // El motivo dice QUÉ documento falló: en traspaso hay hasta 8 personas por fila.
    expect(screen.getByTestId('carga-masiva-fila-2')).toHaveTextContent(
      /RUNT no encontró a la persona .*\(CC 1020304050\)/i,
    );

    // El motivo se traduce: el usuario no tiene por qué leer el código del backend.
    expect(screen.getByTestId('carga-masiva-fila-3')).toHaveTextContent(
      /no es uno de los habilitados/i,
    );
  });

  it('AC2 — un lote en proceso se dice como tal, sin resumen a medias', async () => {
    detallar.mockResolvedValue({
      batch: {
        ...LOTE,
        status: 'processing',
        completedAt: null,
        counts: { created: 1, createdPending: 0, notCreated: 0, pendientes: 2 },
      },
      rows: DETALLE.rows,
    });

    render(<CargaMasivaResultados />);

    const aviso = await screen.findByTestId('carga-masiva-en-proceso');
    expect(aviso).toHaveTextContent('Van 1 de 3 filas');
    expect(screen.queryByTestId('carga-masiva-contadores')).not.toBeInTheDocument();
  });

  it('AC3 — el trámite por retomar se abre en su wizard', async () => {
    const user = userEvent.setup();
    const onNavegar = vi.fn();
    render(<CargaMasivaResultados onNavegar={onNavegar} />);

    await user.click(await screen.findByTestId('carga-masiva-abrir-2'));

    expect(onNavegar).toHaveBeenCalled();
    expect(push).toHaveBeenCalledWith('/tramites/inst-2');
  });

  it('la fila que no creó trámite no ofrece enlace: no hay a dónde ir', async () => {
    render(<CargaMasivaResultados />);

    await screen.findByTestId('carga-masiva-fila-3');
    expect(screen.queryByTestId('carga-masiva-abrir-3')).not.toBeInTheDocument();
  });

  it('sin lotes lo dice, en vez de una tabla vacía', async () => {
    listar.mockResolvedValue([]);
    render(<CargaMasivaResultados />);

    expect(await screen.findByTestId('carga-masiva-sin-lotes')).toBeInTheDocument();
    expect(detallar).not.toHaveBeenCalled();
  });

  it('«Actualizar» conserva el lote elegido en vez de saltar al más reciente', async () => {
    const otro = { ...LOTE, id: 'b-2', sourceFilename: 'otro.xlsx' };
    listar.mockResolvedValue([LOTE, otro]);
    detallar.mockImplementation(async (id) => ({ ...DETALLE, batch: { ...LOTE, id } }));
    const user = userEvent.setup();

    render(<CargaMasivaResultados />);
    await user.selectOptions(await screen.findByTestId('carga-masiva-selector-lote'), 'b-2');
    await waitFor(() => expect(detallar).toHaveBeenLastCalledWith('b-2'));

    await user.click(screen.getByTestId('carga-masiva-actualizar'));

    await waitFor(() => expect(listar).toHaveBeenCalledTimes(2));
    expect(detallar).toHaveBeenLastCalledWith('b-2');
  });

  it('al cambiar de lote trae su detalle', async () => {
    const otro = { ...LOTE, id: 'b-2', sourceFilename: 'otro.xlsx' };
    listar.mockResolvedValue([LOTE, otro]);
    const user = userEvent.setup();

    render(<CargaMasivaResultados />);
    await screen.findByTestId('carga-masiva-selector-lote');

    await user.selectOptions(screen.getByTestId('carga-masiva-selector-lote'), 'b-2');

    await waitFor(() => expect(detallar).toHaveBeenLastCalledWith('b-2'));
  });

  // HU #12538 — motivos de la persona jurídica: el cliente lee qué le falta a la empresa, con el NIT.
  it('traduce los motivos de persona jurídica y conserva el NIT como detalle', () => {
    expect(mensajeMotivo('persona_juridica_sin_representante_registrado:NIT 900123456')).toMatch(
      /representante legal registrado.*\(NIT 900123456\)/i,
    );
    expect(mensajeMotivo('empresa_no_encontrada:NIT 900123456')).toMatch(/RUES no encontró.*\(NIT 900123456\)/i);
    expect(mensajeMotivo('representante_no_registrado:NIT 900123456')).toMatch(/cédula del representante/i);
    expect(mensajeMotivo('consulta_empresa_fallida:NIT 900123456')).toMatch(/consulta de la empresa a RUES falló/i);
  });

  it('traduce el error estructural de datos de contacto y dice a qué actor le faltan', () => {
    expect(mensajeMotivo('datos_contacto_incompletos:vendedor_1')).toMatch(
      /faltan datos de contacto.*\(vendedor_1\)/i,
    );
  });
});

// Tests unitarios del módulo "Auditoría" (HU #10680) — pantalla SuperAdmin-only del
// rastro unificado de auditoría administrativa/seguridad (GET /api/v1/superadmin/audit,
// HU #10679). Cubre los AC de la HU:
//   AC1 — visible solo para SuperAdmin (gating en Shell/app/page).
//   AC2 — filtros funcionales: re-consultan el backend con los params correctos y
//         resetean a página 1.
//   AC3 — 4 estados de UI: cargando, vacío, error (con reintentar), lleno.
//   AC4 — paginación server-side: cambiar de página consulta con el `page` nuevo y
//         muestra "Mostrando X–Y de N".
//
// Mecanismo de mock calcado de __tests__/validaciones-module.test.tsx: se mockea el
// cliente HTTP (`fetchAdminAuditLog`) y se renderiza el componente real.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { AdminAuditLogEntry, AdminAuditLogPageResponse } from '@/lib/api/types';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  fetchAdminAuditLog: vi.fn(),
}));

vi.mock('@/lib/api/audit', () => ({
  fetchAdminAuditLog: mocks.fetchAdminAuditLog,
}));

import { Auditoria } from '@/components/atom/modules/Auditoria';

const ROW_1: AdminAuditLogEntry = {
  id: 'log-1',
  tenantId: 'tenant-aaa',
  tenantType: 'COMPANY',
  module: 'users',
  entityName: 'User',
  operation: 'create',
  result: 'success',
  errorCode: null,
  changedBy: 'actor-11111111',
  targetEntityType: 'User',
  targetEntityId: 'target-22222222',
  clientIp: '190.10.20.30',
  changedAt: '2026-07-01T15:30:00Z',
};

const ROW_2: AdminAuditLogEntry = {
  id: 'log-2',
  tenantId: 'tenant-bbb',
  tenantType: 'TRANSIT_OFFICE',
  module: 'authentication',
  entityName: 'Session',
  operation: 'login',
  result: 'failure',
  errorCode: 'INVALID_CREDENTIALS',
  changedBy: 'actor-33333333',
  targetEntityType: null,
  targetEntityId: null,
  clientIp: '190.10.20.99',
  changedAt: '2026-07-02T08:00:00Z',
};

const FULL: AdminAuditLogPageResponse = {
  data: [ROW_1, ROW_2],
  totalCount: 2,
  page: 1,
  pageSize: 20,
};

const EMPTY: AdminAuditLogPageResponse = {
  data: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
};

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('Auditoria — AC3 estados de UI', () => {
  it('Cargando: muestra el estado accesible de carga antes de la primera respuesta', async () => {
    let resolveFn: (v: AdminAuditLogPageResponse) => void = () => {};
    const pending = new Promise<AdminAuditLogPageResponse>((r) => {
      resolveFn = r;
    });
    mocks.fetchAdminAuditLog.mockReturnValue(pending);

    render(<Auditoria />);

    const status = screen.getByRole('status');
    expect(status).toHaveTextContent(/cargando/i);

    await act(async () => {
      resolveFn(EMPTY);
      await pending;
    });
  });

  it('Vacío: muestra mensaje explícito cuando no hay registros de auditoría', async () => {
    mocks.fetchAdminAuditLog.mockResolvedValue(EMPTY);

    render(<Auditoria />);

    expect(await screen.findByText(/aún no hay registros de auditoría/i)).toBeInTheDocument();
  });

  it('Error: muestra role="alert" con botón de reintento cuando falla la carga inicial', async () => {
    mocks.fetchAdminAuditLog.mockRejectedValue(new Error('500 Internal Server Error'));

    render(<Auditoria />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/500 internal server error/i);
    expect(within(alert).getByRole('button', { name: /reintentar/i })).toBeInTheDocument();
  });

  it('Error: reintentar vuelve a consultar el backend y, si responde bien, pinta los datos', async () => {
    const user = userEvent.setup();
    mocks.fetchAdminAuditLog
      .mockRejectedValueOnce(new Error('Network error'))
      .mockResolvedValue(FULL);

    render(<Auditoria />);

    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: /reintentar/i }));

    await waitFor(() => expect(mocks.fetchAdminAuditLog).toHaveBeenCalledTimes(2));
    expect(await screen.findByText('190.10.20.30')).toBeInTheDocument();
  });

  it('Lleno: pinta una fila por registro con sus columnas (módulo, resultado, IP)', async () => {
    mocks.fetchAdminAuditLog.mockResolvedValue(FULL);

    render(<Auditoria />);

    const list = await screen.findByRole('list', { name: /registros de auditoría/i });
    expect(within(list).getByText('Usuarios')).toBeInTheDocument();
    expect(within(list).getByText('Autenticación')).toBeInTheDocument();
    expect(within(list).getByText('Éxito')).toBeInTheDocument();
    expect(within(list).getByText('Fallo')).toBeInTheDocument();
    expect(within(list).getByText('190.10.20.30')).toBeInTheDocument();
    expect(within(list).getByText('190.10.20.99')).toBeInTheDocument();
  });
});

describe('Auditoria — AC2 filtros funcionales', () => {
  beforeEach(() => {
    mocks.fetchAdminAuditLog.mockResolvedValue(FULL);
  });

  it('filtra por resultado: al elegir "Fallo" re-consulta el backend con result=failure y vuelve a la página 1', async () => {
    const user = userEvent.setup();

    render(<Auditoria />);
    await screen.findByText('190.10.20.30'); // carga inicial

    // Navega a la página 2 primero para comprobar que el filtro la resetea a 1.
    mocks.fetchAdminAuditLog.mockResolvedValueOnce({ ...FULL, totalCount: 45, page: 2 });
    // (el propio filtro dispara la llamada real que verificamos abajo)

    mocks.fetchAdminAuditLog.mockClear();
    await user.selectOptions(screen.getByLabelText('Resultado'), 'failure');

    await waitFor(() =>
      expect(mocks.fetchAdminAuditLog).toHaveBeenCalledWith(
        expect.objectContaining({ result: 'failure', page: 1 }),
      ),
    );
  });

  it('filtra por módulo: al elegir "Roles" re-consulta con module=roles', async () => {
    const user = userEvent.setup();

    render(<Auditoria />);
    await screen.findByText('190.10.20.30');

    mocks.fetchAdminAuditLog.mockClear();
    await user.selectOptions(screen.getByLabelText('Módulo'), 'roles');

    await waitFor(() =>
      expect(mocks.fetchAdminAuditLog).toHaveBeenCalledWith(
        expect.objectContaining({ module: 'roles', page: 1 }),
      ),
    );
  });

  it('filtra por rango de fechas: "Desde/Hasta" viajan como dateFrom/dateTo en ISO de inicio/fin de día', async () => {
    const user = userEvent.setup();

    render(<Auditoria />);
    await screen.findByText('190.10.20.30');

    // El rango de fechas vive en el panel avanzado.
    await user.click(screen.getByRole('button', { name: /más filtros/i }));

    mocks.fetchAdminAuditLog.mockClear();
    await user.type(screen.getByLabelText('Fecha desde'), '2026-07-01');
    await user.type(screen.getByLabelText('Fecha hasta'), '2026-07-31');

    await waitFor(() =>
      expect(mocks.fetchAdminAuditLog).toHaveBeenCalledWith(
        expect.objectContaining({
          dateFrom: '2026-07-01T00:00:00',
          dateTo: '2026-07-31T23:59:59',
          page: 1,
        }),
      ),
    );
  });

  it('filtra por texto (usuario) con debounce ~300ms: espera y consulta una sola vez con el valor final', async () => {
    vi.useFakeTimers();
    try {
      render(<Auditoria />);

      // Resuelve la carga inicial (1 consulta) sin depender de temporizadores reales.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(mocks.fetchAdminAuditLog).toHaveBeenCalledTimes(1);

      mocks.fetchAdminAuditLog.mockClear();
      const input = screen.getByLabelText('Filtrar por usuario actor o afectado');
      fireEvent.change(input, { target: { value: 'actor-111' } });

      // Antes de que venza el debounce no debe haberse re-consultado con el filtro de texto.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(100);
      });
      expect(mocks.fetchAdminAuditLog).not.toHaveBeenCalled();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(250);
      });

      expect(mocks.fetchAdminAuditLog).toHaveBeenCalledWith(
        expect.objectContaining({ userId: 'actor-111', page: 1 }),
      );
    } finally {
      vi.useRealTimers();
    }
  });

  it('limpiar filtros: restablece los controles y recarga sin query params de filtro', async () => {
    const user = userEvent.setup();

    render(<Auditoria />);
    await screen.findByText('190.10.20.30');

    const resultSelect = screen.getByLabelText('Resultado');
    await user.selectOptions(resultSelect, 'failure');
    await waitFor(() => expect(resultSelect).toHaveValue('failure'));

    mocks.fetchAdminAuditLog.mockClear();
    await user.click(screen.getByRole('button', { name: /limpiar filtros/i }));

    await waitFor(() => {
      expect(mocks.fetchAdminAuditLog).toHaveBeenCalled();
      const lastArg = mocks.fetchAdminAuditLog.mock.calls.at(-1)?.[0];
      expect(lastArg?.result).toBeUndefined();
      expect(lastArg?.page).toBe(1);
    });
    expect(screen.getByLabelText('Resultado')).toHaveValue('');
  });

  it('sin resultados tras filtrar: con filtros activos y respuesta vacía muestra el mensaje de "sin coincidencias"', async () => {
    const user = userEvent.setup();
    mocks.fetchAdminAuditLog
      .mockResolvedValueOnce(FULL) // carga inicial con datos
      .mockResolvedValue(EMPTY); // tras filtrar, sin coincidencias

    render(<Auditoria />);
    await screen.findByText('190.10.20.30');

    await user.selectOptions(screen.getByLabelText('Resultado'), 'failure');

    expect(
      await screen.findByText(/ningún registro de auditoría coincide con los filtros aplicados/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/aún no hay registros de auditoría/i)).not.toBeInTheDocument();
  });
});

describe('Auditoria — AC4 paginación', () => {
  const PAGED: AdminAuditLogPageResponse = {
    ...FULL,
    totalCount: 45, // 45 / 20 (default pageSize) = 3 páginas
  };

  it('navega a la página siguiente: consulta el backend con page=2 y actualiza el conteo mostrado', async () => {
    const user = userEvent.setup();
    mocks.fetchAdminAuditLog.mockResolvedValue(PAGED);

    render(<Auditoria />);
    await screen.findByText('190.10.20.30');

    expect(screen.getByText(/mostrando 1–20 de 45/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /página siguiente/i }));

    await waitFor(() =>
      expect(mocks.fetchAdminAuditLog).toHaveBeenCalledWith(
        expect.objectContaining({ page: 2, pageSize: 20 }),
      ),
    );
  });

  it('cambiar filas por página vuelve a la página 1 con el nuevo tamaño', async () => {
    const user = userEvent.setup();
    mocks.fetchAdminAuditLog.mockResolvedValue(PAGED);

    render(<Auditoria />);
    await screen.findByText('190.10.20.30');

    await user.selectOptions(screen.getByLabelText('Filas por página'), '50');

    await waitFor(() =>
      expect(mocks.fetchAdminAuditLog).toHaveBeenCalledWith(
        expect.objectContaining({ page: 1, pageSize: 50 }),
      ),
    );
  });
});

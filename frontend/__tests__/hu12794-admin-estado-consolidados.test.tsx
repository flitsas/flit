/**
 * HU #12794 (Épica #12760) — SuperAdmin: vigencia y origen de los DOS consolidados en el modal
 * «Gestionar consolidado» (acciones administrativas del trámite).
 *
 * Uso de ejemplo: el SuperAdmin abre «Gestionar consolidado» → el modal lee
 * `GET /api/v1/tramites/instances/{id}` y pinta el consolidado del wizard y el maestro con su
 * vigencia, sello de tiempo y, si aplica, la marca «Cargado manualmente».
 */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ToastProvider } from '@/components/admin/Toast';
import type { ConsolidadoVigencia, InstanceSummary } from '@/lib/api/types/procedure-runtime';

const permissionsState = { permissions: [] as string[], isSuperAdmin: true };
vi.mock('@/hooks/usePermissions', () => ({ usePermissions: () => permissionsState }));

const mocks = vi.hoisted(() => ({
  getInstance: vi.fn(),
  adminLimpiarConsolidado: vi.fn(),
  adminCargarConsolidado: vi.fn(),
}));
vi.mock('@/lib/api/tramites-client', () => ({ tramitesClient: mocks }));

import { useAdminTramiteAcciones } from '@/components/operacion/AdminTramiteAcciones';
import { EstadoConsolidadosAdmin } from '@/components/operacion/EstadoConsolidadosAdmin';

function Harness({ item, onChanged }: { item: InstanceSummary; onChanged: () => void }) {
  const { items, modals } = useAdminTramiteAcciones({ item, isAdmin: true, onChanged });
  return (
    <div>
      {items.map((i) => (
        <button key={i.key} type="button" disabled={i.disabled} onClick={i.onSelect}>
          {i.label}
        </button>
      ))}
      {modals}
    </div>
  );
}

const item = {
  id: 'inst-12794',
  referenceNumber: 'TR-12794',
  placa: 'ABC123',
  tenantId: 'tenant-b',
  estado: 'en_revision',
} as unknown as InstanceSummary;

// 15:05 UTC = 10:05 hora Colombia; 20:30 UTC = 15:30.
const WIZARD_VIGENTE: ConsolidadoVigencia = {
  estado: 'vigente',
  generadoEn: '2026-09-23T15:05:00Z',
  origen: 'system',
  definitivo: false,
  modo: null,
};
const MAESTRO_DESACTUALIZADO: ConsolidadoVigencia = {
  estado: 'desactualizado',
  generadoEn: '2026-09-22T20:30:00Z',
  origen: 'system',
  definitivo: false,
  modo: null,
};
const MANUAL: ConsolidadoVigencia = {
  estado: 'vigente',
  generadoEn: '2026-09-23T15:05:00Z',
  origen: 'user',
  definitivo: false,
  modo: 'cargado_por_usuario',
};
const INEXISTENTE: ConsolidadoVigencia = {
  estado: 'inexistente',
  generadoEn: null,
  origen: null,
  definitivo: false,
  modo: null,
};

async function abrirModal() {
  render(
    <ToastProvider>
      <Harness item={item} onChanged={vi.fn()} />
    </ToastProvider>,
  );
  await userEvent.click(screen.getByRole('button', { name: 'Gestionar consolidado' }));
  return screen.findByRole('dialog', { name: /Gestionar consolidado/ });
}

beforeEach(() => {
  vi.clearAllMocks();
  // `clearAllMocks` no vacía la cola de `mockResolvedValueOnce`: se resetea explícitamente.
  mocks.getInstance.mockReset();
  mocks.adminLimpiarConsolidado.mockReset();
  mocks.adminCargarConsolidado.mockReset();
  permissionsState.permissions = [];
  permissionsState.isSuperAdmin = true;
});

describe('HU #12794 AC1 — estado y fecha de los dos consolidados', () => {
  it('happy path: muestra wizard y maestro con su vigencia y sello de tiempo, leyendo el detalle con el tenant de la fila', async () => {
    mocks.getInstance.mockResolvedValue({
      consolidadoWizard: WIZARD_VIGENTE,
      consolidadoMaestro: MAESTRO_DESACTUALIZADO,
    });
    const dialog = await abrirModal();

    const wizard = await within(dialog).findByTestId('estado-consolidado-wizard');
    const maestro = within(dialog).getByTestId('estado-consolidado-maestro');
    expect(mocks.getInstance).toHaveBeenCalledWith('inst-12794', 'tenant-b');

    expect(within(wizard).getByText('Consolidado del wizard')).toBeInTheDocument();
    expect(within(wizard).getByText('Consolidado: Vigente')).toBeInTheDocument();
    expect(within(wizard).getByText(/Generado el 23\/09\/2026 10:05/)).toBeInTheDocument();

    expect(within(maestro).getByText('Consolidado maestro', { selector: 'p' })).toBeInTheDocument();
    expect(within(maestro).getByText('Consolidado maestro: Desactualizado')).toBeInTheDocument();
    expect(within(maestro).getByText(/Última generación: 22\/09\/2026 15:30/)).toBeInTheDocument();
  });

  it('edge: un documento sin dato de vigencia muestra su estado vacío sin ocultar el otro', async () => {
    mocks.getInstance.mockResolvedValue({ consolidadoWizard: WIZARD_VIGENTE, consolidadoMaestro: null });
    const dialog = await abrirModal();
    const maestro = await within(dialog).findByTestId('estado-consolidado-maestro');
    expect(within(maestro).getByText(/Sin información de vigencia/)).toBeInTheDocument();
    expect(within(dialog).getByText('Consolidado: Vigente')).toBeInTheDocument();
  });

  it('edge: backend sin ninguna vigencia ⇒ estado vacío de la sección', async () => {
    mocks.getInstance.mockResolvedValue({});
    const dialog = await abrirModal();
    expect(
      await within(dialog).findByText('Este trámite no informa la vigencia de sus consolidados.'),
    ).toBeInTheDocument();
  });

  it('edge: error al leer el detalle ⇒ alerta con reintento que vuelve a consultar', async () => {
    // Security B2 (Épica #12760): el `message` crudo del backend no se pinta; copy amigable.
    mocks.getInstance.mockRejectedValueOnce(new Error('internal_error: NullReferenceException'));
    mocks.getInstance.mockResolvedValueOnce({ consolidadoWizard: WIZARD_VIGENTE, consolidadoMaestro: INEXISTENTE });
    const dialog = await abrirModal();
    expect(
      await within(dialog).findByText(/No se pudo consultar el estado de los consolidados/),
    ).toBeInTheDocument();
    expect(dialog.textContent).not.toMatch(/internal_error|NullReferenceException/);
    await userEvent.click(
      within(dialog).getByRole('button', { name: 'Reintentar el estado de los consolidados' }),
    );
    expect(await within(dialog).findByText('Consolidado: Vigente')).toBeInTheDocument();
    expect(mocks.getInstance).toHaveBeenCalledTimes(2);
  });

  it('contrato/accesibilidad: sección etiquetada y cada indicador con nombre accesible propio', async () => {
    mocks.getInstance.mockResolvedValue({
      consolidadoWizard: WIZARD_VIGENTE,
      consolidadoMaestro: MAESTRO_DESACTUALIZADO,
    });
    const dialog = await abrirModal();
    await within(dialog).findByTestId('estado-consolidado-wizard');
    expect(within(dialog).getByRole('region', { name: 'Estado actual de los consolidados' })).toBeInTheDocument();
    expect(within(dialog).getByRole('group', { name: /^Consolidado: vigente, generado el 23\/09\/2026 10:05/ })).toBeInTheDocument();
    expect(within(dialog).getByRole('group', { name: /^Consolidado maestro: desactualizado/ })).toBeInTheDocument();
  });
});

describe('HU #12794 AC2 — origen manual', () => {
  it('happy path: consolidado cargado a mano ⇒ marca «Cargado manualmente» + advertencia', async () => {
    mocks.getInstance.mockResolvedValue({ consolidadoWizard: MANUAL, consolidadoMaestro: WIZARD_VIGENTE });
    const dialog = await abrirModal();
    const wizard = await within(dialog).findByTestId('estado-consolidado-wizard');
    expect(wizard).toHaveAttribute('data-manual', 'true');
    expect(within(wizard).getByText('Cargado manualmente')).toBeInTheDocument();
    expect(within(wizard).getByText(/Una regeneración automática no lo sobrescribirá/)).toBeInTheDocument();
    // El maestro, generado por el sistema, no lleva la marca.
    const maestro = within(dialog).getByTestId('estado-consolidado-maestro');
    expect(maestro).toHaveAttribute('data-manual', 'false');
    expect(within(maestro).queryByText('Cargado manualmente')).not.toBeInTheDocument();
  });

  it('edge: solo `modo: cargado_por_usuario` (sin origen) también se marca como manual', async () => {
    mocks.getInstance.mockResolvedValue({
      consolidadoWizard: { ...MANUAL, origen: null },
      consolidadoMaestro: null,
    });
    const dialog = await abrirModal();
    const wizard = await within(dialog).findByTestId('estado-consolidado-wizard');
    expect(within(wizard).getByText('Cargado manualmente')).toBeInTheDocument();
  });

  it('contrato: la advertencia es un aviso informativo (role=status), no una alerta', async () => {
    mocks.getInstance.mockResolvedValue({ consolidadoWizard: MANUAL, consolidadoMaestro: null });
    const dialog = await abrirModal();
    const wizard = await within(dialog).findByTestId('estado-consolidado-wizard');
    const aviso = within(wizard).getByText('Cargado manualmente').closest('[role]');
    expect(aviso).toHaveAttribute('role', 'status');
  });
});

describe('HU #12794 AC3 — tras limpiar, se relee el detalle', () => {
  it('happy path: limpiar ⇒ el modal sigue abierto, relee el detalle y el sello desaparece', async () => {
    mocks.getInstance
      .mockResolvedValueOnce({ consolidadoWizard: MANUAL, consolidadoMaestro: WIZARD_VIGENTE })
      .mockResolvedValueOnce({ consolidadoWizard: INEXISTENTE, consolidadoMaestro: INEXISTENTE });
    mocks.adminLimpiarConsolidado.mockResolvedValue({ id: 'att-1' });
    const dialog = await abrirModal();
    expect(await within(dialog).findAllByText(/Generado el 23\/09\/2026 10:05/)).toHaveLength(2);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Regenerar consolidado' }));
    expect(mocks.adminLimpiarConsolidado).toHaveBeenCalledWith('inst-12794', 'tenant-b');

    await waitFor(() => expect(mocks.getInstance).toHaveBeenCalledTimes(2));
    const wizard = await within(dialog).findByText('Consolidado: Sin generar');
    expect(wizard).toBeInTheDocument();
    expect(within(dialog).getByText('Consolidado maestro: Sin generar')).toBeInTheDocument();
    expect(within(dialog).queryByText(/Generado el/)).not.toBeInTheDocument();
    expect(within(dialog).queryByText(/Última generación/)).not.toBeInTheDocument();
    expect(within(dialog).queryByText('Cargado manualmente')).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: /Gestionar consolidado/ })).toBeInTheDocument();
  });

  it('cargar también relee el detalle y pasa a mostrar el origen manual', async () => {
    mocks.getInstance
      .mockResolvedValueOnce({ consolidadoWizard: WIZARD_VIGENTE, consolidadoMaestro: null })
      .mockResolvedValueOnce({ consolidadoWizard: MANUAL, consolidadoMaestro: null });
    mocks.adminCargarConsolidado.mockResolvedValue({ id: 'att-2' });
    const dialog = await abrirModal();
    await within(dialog).findByText('Consolidado: Vigente');
    const input = dialog.querySelector('input[type="file"]') as HTMLInputElement;
    await userEvent.upload(input, new File(['%PDF-1.4'], 'c.pdf', { type: 'application/pdf' }));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cargar PDF' }));
    expect(await within(dialog).findByText('Cargado manualmente')).toBeInTheDocument();
    expect(mocks.getInstance).toHaveBeenCalledTimes(2);
  });

  it('edge: si limpiar falla, NO se relee el detalle y el estado previo se conserva', async () => {
    mocks.getInstance.mockResolvedValue({ consolidadoWizard: WIZARD_VIGENTE, consolidadoMaestro: null });
    mocks.adminLimpiarConsolidado.mockRejectedValue(new Error('No hay adjuntos para consolidar.'));
    const dialog = await abrirModal();
    await within(dialog).findByText('Consolidado: Vigente');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Regenerar consolidado' }));
    expect((await within(dialog).findAllByText('No hay adjuntos para consolidar.')).length).toBeGreaterThan(0);
    expect(mocks.getInstance).toHaveBeenCalledTimes(1);
    expect(within(dialog).getByText('Consolidado: Vigente')).toBeInTheDocument();
  });

  it('contrato: el componente relee cuando cambia `recarga`', async () => {
    mocks.getInstance
      .mockResolvedValueOnce({ consolidadoWizard: WIZARD_VIGENTE, consolidadoMaestro: null })
      .mockResolvedValueOnce({ consolidadoWizard: INEXISTENTE, consolidadoMaestro: null });
    const { rerender } = render(<EstadoConsolidadosAdmin instanceId="i-1" recarga={0} />);
    expect(await screen.findByText('Consolidado: Vigente')).toBeInTheDocument();
    rerender(<EstadoConsolidadosAdmin instanceId="i-1" recarga={1} />);
    expect(await screen.findByText('Consolidado: Sin generar')).toBeInTheDocument();
    expect(mocks.getInstance).toHaveBeenLastCalledWith('i-1', undefined);
  });
});

// HU #14547 — «Regenerar mandato y consolidado» en el modal «Gestionar consolidado»: solo SuperAdmin
// (no por permiso), oculto en modo consulta, deshabilitado con motivo visible en los estados finales,
// con confirmación previa, avisos del backend al terminar y errores en texto de negocio. Las secciones
// Limpiar y Cargar no cambian (R6).
//
// Uso de ejemplo: `useAdminTramiteAcciones({ item, isAdmin: true, onChanged })` → ítem «Gestionar
// consolidado» → sección «Regenerar mandato y consolidado» → «Regenerar mandato» → «Sí, regenerar».
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ToastProvider } from '@/components/admin/Toast';
import { useAdminTramiteAcciones } from '../AdminTramiteAcciones';
import { TramitesApiError } from '@/lib/api/tramites-client';
import { ADMIN_TRAMITE_PERMISSIONS } from '@/lib/tramites/admin-tramite-permissions';
import { estadoLabel } from '@/lib/tramites/estados';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

const permissionsState = { permissions: [] as string[], isSuperAdmin: false };
vi.mock('@/hooks/usePermissions', () => ({ usePermissions: () => permissionsState }));

const regenerar = vi.fn();
const limpiar = vi.fn();
vi.mock('@/lib/api/tramites-client', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api/tramites-client')>('@/lib/api/tramites-client');
  return {
    ...actual,
    tramitesClient: {
      ...actual.tramitesClient,
      regenerarMandatoYConsolidado: (...a: unknown[]) => regenerar(...a),
      adminLimpiarConsolidado: (...a: unknown[]) => limpiar(...a),
    },
  };
});

function Harness({
  item,
  onChanged,
  consultaMode = false,
}: {
  item: InstanceSummary;
  onChanged: () => void;
  consultaMode?: boolean;
}) {
  const { items, modals } = useAdminTramiteAcciones({ item, isAdmin: true, onChanged, consultaMode });
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

const base = {
  id: 'inst-8',
  referenceNumber: 'FT1-0008',
  placa: 'FUP906',
  tenantId: 't-1',
  estado: 'entregado',
} as unknown as InstanceSummary;

const TITULO = 'Regenerar mandato y consolidado';
const DESCRIPCION =
  'Vuelve a generar el contrato de mandato con el mandatario que indique hoy la configuración de Mandatos y arma de nuevo los consolidados del gestor y del organismo de tránsito. El FUR y los demás documentos no cambian. No está disponible en trámites aprobados, anulados o revocados.';

const okResponse = {
  mandato: { attachmentId: 'm-1', firmanteAnteriorId: 'f-0', firmanteNuevoId: 'f-1', sinFirmar: false },
  consolidado: { attachmentId: 'c-1', generadoEn: '2026-11-11T10:00:00Z' },
  maestro: { estado: 'radicado_conservado', attachmentId: 'x-1', generadoEn: null },
  avisos: [
    { codigo: 'maestro_radicado_conservado', mensaje: 'El OT conserva el expediente radicado.' },
    { codigo: 'mandato_sin_firmar', mensaje: 'El mandato quedó sin firmar.' },
  ],
};

function renderHarness(item: InstanceSummary = base, onChanged = vi.fn(), consultaMode = false) {
  render(
    <ToastProvider>
      <Harness item={item} onChanged={onChanged} consultaMode={consultaMode} />
    </ToastProvider>,
  );
  return onChanged;
}

async function abrirConsolidado() {
  await userEvent.click(screen.getByRole('button', { name: 'Gestionar consolidado' }));
  return screen.findByRole('dialog', { name: /Gestionar consolidado/ });
}

describe('HU #14547 — Regenerar mandato y consolidado', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    permissionsState.permissions = [];
    permissionsState.isSuperAdmin = true;
  });

  it('visible para SuperAdmin con título y descripción', async () => {
    renderHarness();
    const dialog = await abrirConsolidado();
    expect(within(dialog).getByRole('heading', { name: TITULO })).toBeInTheDocument();
    expect(within(dialog).getByText(DESCRIPCION)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Regenerar mandato' })).toBeEnabled();
  });

  it('oculto para un admin con todos los permisos que no es SuperAdmin', async () => {
    permissionsState.isSuperAdmin = false;
    permissionsState.permissions = Object.values(ADMIN_TRAMITE_PERMISSIONS);
    renderHarness();
    const dialog = await abrirConsolidado();
    expect(within(dialog).queryByText(TITULO)).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Regenerar mandato' })).not.toBeInTheDocument();
    // Limpiar y Cargar siguen disponibles por permiso (R6).
    expect(within(dialog).getByRole('button', { name: 'Regenerar consolidado' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cargar PDF' })).toBeInTheDocument();
  });

  it('oculto en modo consulta aunque quien opera sea SuperAdmin', () => {
    renderHarness(base, vi.fn(), true);
    expect(screen.queryByRole('button', { name: 'Gestionar consolidado' })).not.toBeInTheDocument();
    expect(screen.queryByText(TITULO)).not.toBeInTheDocument();
  });

  it.each(['aprobado', 'anulado', 'revocado'])(
    'en estado %s el botón queda deshabilitado con el motivo visible y enlazado',
    async (estado) => {
      renderHarness({ ...base, estado } as InstanceSummary);
      const dialog = await abrirConsolidado();
      const boton = within(dialog).getByRole('button', { name: 'Regenerar mandato' });
      expect(boton).toBeDisabled();
      const motivo = within(dialog).getByText(
        `No disponible: el trámite está ${estadoLabel(estado).toLowerCase()}.`,
      );
      expect(motivo).toBeVisible();
      expect(boton).toHaveAttribute('aria-describedby', expect.stringContaining(motivo.id));
      // Limpiar sigue habilitado (R6: sin cambios, aunque el trámite esté en estado final).
      expect(within(dialog).getByRole('button', { name: 'Regenerar consolidado' })).toBeEnabled();
    },
  );

  it('pide confirmación advirtiendo que el mandatario puede cambiar; cancelar no llama al backend', async () => {
    renderHarness();
    const dialog = await abrirConsolidado();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Regenerar mandato' }));
    const confirm = await screen.findByRole('dialog', { name: /Confirmar regeneración del mandato/ });
    expect(
      within(confirm).getByText('El mandatario puede cambiar según la configuración actual de Mandatos.'),
    ).toBeInTheDocument();
    expect(regenerar).not.toHaveBeenCalled();
    await userEvent.click(within(confirm).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: /Confirmar regeneración del mandato/ })).not.toBeInTheDocument(),
    );
    expect(regenerar).not.toHaveBeenCalled();
    // El modal padre sigue abierto.
    expect(screen.getByRole('dialog', { name: /Gestionar consolidado/ })).toBeInTheDocument();
  });

  it('éxito: carga con botones deshabilitados, toast, avisos del backend y relectura del detalle', async () => {
    let resolver: (v: unknown) => void = () => {};
    regenerar.mockReturnValue(new Promise((r) => (resolver = r)));
    const onChanged = renderHarness();
    const dialog = await abrirConsolidado();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Regenerar mandato' }));
    const confirm = await screen.findByRole('dialog', { name: /Confirmar regeneración del mandato/ });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Sí, regenerar' }));

    expect(regenerar).toHaveBeenCalledWith('inst-8', 't-1');
    expect(await within(confirm).findByRole('button', { name: 'Regenerando…' })).toBeDisabled();
    expect(within(confirm).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Regenerar consolidado' })).toBeDisabled();

    resolver(okResponse);

    expect(await screen.findByText('Mandato y consolidado regenerados.')).toBeInTheDocument();
    const avisos = await within(dialog).findByRole('list', { name: 'Avisos de la regeneración' });
    expect(within(avisos).getByText('El OT conserva el expediente radicado.')).toBeInTheDocument();
    expect(within(avisos).getByText('El mandato quedó sin firmar.')).toBeInTheDocument();
    expect(onChanged).toHaveBeenCalled();
    expect(screen.queryByRole('dialog', { name: /Confirmar regeneración del mandato/ })).not.toBeInTheDocument();
  });

  it('error: muestra el texto de negocio (sin código crudo) y no relee el detalle', async () => {
    regenerar.mockRejectedValue(
      new TramitesApiError(
        409,
        'estado_no_permite_regenerar_mandato',
        { title: 'Conflict', detail: 'estado_no_permite_regenerar_mandato', status: 409 },
      ),
    );
    const onChanged = renderHarness();
    const dialog = await abrirConsolidado();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Regenerar mandato' }));
    const confirm = await screen.findByRole('dialog', { name: /Confirmar regeneración del mandato/ });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Sí, regenerar' }));

    const alertas = await within(dialog).findAllByText(/aprobado, anulado o revocado: ya no se puede regenerar/);
    expect(alertas.length).toBeGreaterThan(0);
    expect(screen.queryByText(/estado_no_permite_regenerar_mandato/)).not.toBeInTheDocument();
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('un 403 se muestra como texto de negocio', async () => {
    regenerar.mockRejectedValue(new TramitesApiError(403, '403 Forbidden', null));
    renderHarness();
    const dialog = await abrirConsolidado();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Regenerar mandato' }));
    const confirm = await screen.findByRole('dialog', { name: /Confirmar regeneración del mandato/ });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Sí, regenerar' }));
    expect((await within(dialog).findAllByText(/Solo un SuperAdmin/)).length).toBeGreaterThan(0);
  });

  it('Limpiar sigue llamando a su propio endpoint, sin confirmación (R6)', async () => {
    limpiar.mockResolvedValue({ id: 'att-1' });
    renderHarness();
    const dialog = await abrirConsolidado();
    expect(within(dialog).getByText('Limpiar consolidado')).toBeInTheDocument();
    expect(within(dialog).getByText('Cargar consolidado')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Regenerar consolidado' }));
    await waitFor(() => expect(limpiar).toHaveBeenCalledWith('inst-8', 't-1'));
    expect(regenerar).not.toHaveBeenCalled();
    expect(await screen.findByText('Consolidado regenerado.')).toBeInTheDocument();
  });
});

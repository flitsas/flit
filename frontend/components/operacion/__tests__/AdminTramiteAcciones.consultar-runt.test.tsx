// Feature #12276 — «Consultar ahora en RUNT» en el menú de acciones del admin: gateado por el
// permiso runt_confirmation.history.read, solo sobre aprobados sin confirmar, con confirmación
// previa y refresco de la fila al terminar.
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ToastProvider } from '@/components/admin/Toast';
import { useAdminTramiteAcciones } from '../AdminTramiteAcciones';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

const permissionsState = { permissions: [] as string[], isSuperAdmin: false };
vi.mock('@/hooks/usePermissions', () => ({ usePermissions: () => permissionsState }));

const consultRuntNow = vi.fn();
vi.mock('@/lib/api/admin-runt-confirmation', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api/admin-runt-confirmation')>('@/lib/api/admin-runt-confirmation');
  return { ...actual, consultRuntNow: (...a: unknown[]) => consultRuntNow(...a) };
});

function Harness({ item, onChanged }: { item: InstanceSummary; onChanged: () => void }) {
  const { items, modals } = useAdminTramiteAcciones({ item, isAdmin: true, onChanged });
  return (
    <div>
      {items.map((i) => (
        <button key={i.key} type="button" disabled={i.disabled} title={i.disabledReason} onClick={i.onSelect}>
          {i.label}
        </button>
      ))}
      {modals}
    </div>
  );
}

const base = { id: 'inst-8', referenceNumber: '8', placa: 'FUP906', tenantId: 't-1', estado: 'aprobado', runtConfirmed: 'no' } as unknown as InstanceSummary;

describe('Consultar ahora en RUNT (acciones del admin)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    permissionsState.permissions = ['runt_confirmation.history.read'];
    permissionsState.isSuperAdmin = false;
  });

  it('no aparece sin el permiso', () => {
    permissionsState.permissions = [];
    render(<ToastProvider><Harness item={base} onChanged={vi.fn()} /></ToastProvider>);
    expect(screen.queryByRole('button', { name: 'Consultar ahora en RUNT' })).not.toBeInTheDocument();
  });

  it('se deshabilita si el trámite no está aprobado o ya está confirmado', () => {
    const { rerender } = render(<ToastProvider><Harness item={{ ...base, estado: 'entregado' } as InstanceSummary} onChanged={vi.fn()} /></ToastProvider>);
    expect(screen.getByRole('button', { name: 'Consultar ahora en RUNT' })).toBeDisabled();

    rerender(<ToastProvider><Harness item={{ ...base, runtConfirmed: 'yes' } as InstanceSummary} onChanged={vi.fn()} /></ToastProvider>);
    const boton = screen.getByRole('button', { name: 'Consultar ahora en RUNT' });
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute('title', 'Este trámite ya está confirmado en el RUNT.');
  });

  it('pide confirmación, llama al endpoint, avisa con el veredicto y refresca la fila', async () => {
    consultRuntNow.mockResolvedValue({ attempt: { verdict: 'pending' }, run: {} });
    const onChanged = vi.fn();
    render(<ToastProvider><Harness item={base} onChanged={onChanged} /></ToastProvider>);
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'Consultar ahora en RUNT' }));
    expect(consultRuntNow).not.toHaveBeenCalled();
    await user.click(await screen.findByRole('button', { name: 'Sí, consultar' }));

    await waitFor(() => expect(consultRuntNow).toHaveBeenCalledWith('inst-8'));
    expect(await screen.findByText('Consulta al RUNT realizada: Pendiente.')).toBeInTheDocument();
    expect(onChanged).toHaveBeenCalled();
  });

  it('un 409 deja el motivo en el modal sin refrescar', async () => {
    const { ConsultNowConflictError } = await import('@/lib/api/admin-runt-confirmation');
    consultRuntNow.mockRejectedValue(new ConsultNowConflictError('ya_confirmado'));
    const onChanged = vi.fn();
    render(<ToastProvider><Harness item={base} onChanged={onChanged} /></ToastProvider>);
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'Consultar ahora en RUNT' }));
    await user.click(await screen.findByRole('button', { name: 'Sí, consultar' }));

    expect(await screen.findByText('El trámite ya está confirmado en el RUNT.')).toBeInTheDocument();
    expect(onChanged).not.toHaveBeenCalled();
  });
});

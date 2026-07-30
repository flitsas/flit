// HU #10874 — panel de subsanación: motivo + checklist (AC1) y Re-radicar (AC2).
// Feature #11066 — Re-radicar solo tras canReradicar; Cancelar sale del flag.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { StatusHistory } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  submitInstance: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
}));

import { SubsanacionPanel } from '../SubsanacionPanel';

const HISTORY_WITH_ITEMS: StatusHistory[] = [
  { fromStatus: 'borrador', toStatus: 'preparado', changedAt: '2026-07-01T10:00:00Z', reason: null },
  { fromStatus: 'preparado', toStatus: 'entregado', changedAt: '2026-07-01T10:05:00Z', reason: null },
  {
    fromStatus: 'entregado',
    toStatus: 'subsanacion',
    changedAt: '2026-07-02T09:00:00Z',
    reason: 'Faltan documentos y el valor comercial es inconsistente.',
    metadata: JSON.stringify({
      motivo: 'Faltan documentos y el valor comercial es inconsistente.',
      items: [
        { campo: 'Documento de identidad', detalle: 'La cédula cargada está borrosa; vuelve a subirla.' },
        { campo: 'Valor comercial', detalle: 'El valor declarado no coincide con el FUR.' },
      ],
      fieldSnapshot: { valor_venta: '50000000' },
    }),
  },
];

const HISTORY_WITHOUT_METADATA: StatusHistory[] = [
  {
    fromStatus: 'entregado',
    toStatus: 'subsanacion',
    changedAt: '2026-07-02T09:00:00Z',
    reason: 'Corrige el documento de identidad del comprador.',
  },
];

const HISTORY_OPERATOR_DRIVEN: StatusHistory[] = [
  { fromStatus: 'preparado', toStatus: 'entregado', changedAt: '2026-07-01T10:05:00Z', reason: null },
  {
    fromStatus: 'entregado',
    toStatus: 'rechazado',
    changedAt: '2026-07-02T08:00:00Z',
    reason: 'Documentos ilegibles; vuelve a cargar la cédula del comprador.',
  },
  {
    fromStatus: 'rechazado',
    toStatus: 'rechazado',
    changedAt: '2026-07-02T09:00:00Z',
    reason: 'Subsanación iniciada por el operador',
    metadata: JSON.stringify({
      motivo: 'Subsanación iniciada por el operador',
      items: [],
      fieldSnapshot: { vin: 'ABC' },
    }),
  },
];

beforeEach(() => {
  vi.clearAllMocks();
  mocks.submitInstance.mockResolvedValue({ id: 'inst-1', status: 'entregado' });
});

describe('SubsanacionPanel — estados de carga/error', () => {
  it('estado cargando: muestra indicador accesible', () => {
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={[]}
        loading
        error={null}
        onReradicado={vi.fn()}
      />,
    );
    expect(screen.getByRole('status')).toHaveTextContent(/cargando/i);
  });

  it('estado error: muestra el mensaje de fallo del fetch', () => {
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={[]}
        loading={false}
        error="Error de red"
        onReradicado={vi.fn()}
      />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/Error de red/);
  });
});

describe('SubsanacionPanel — AC1: motivo y checklist', () => {
  it('con metadata estructurada: pinta el motivo y cada ítem del checklist como checkbox editable', () => {
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITH_ITEMS}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
        canReradicar
      />,
    );

    expect(
      screen.getByText('Faltan documentos y el valor comercial es inconsistente.'),
    ).toBeInTheDocument();

    const checkboxes = screen.getAllByRole('checkbox');
    expect(checkboxes).toHaveLength(2);
    expect(screen.getByText(/Documento de identidad/)).toBeInTheDocument();
    expect(screen.getByText(/La cédula cargada está borrosa/)).toBeInTheDocument();
    expect(screen.getByText(/Valor comercial/)).toBeInTheDocument();
  });

  it('sin metadata estructurada (gap de backend): degrada al motivo plano (`reason`) sin checklist', () => {
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
      />,
    );

    expect(
      screen.getByText('Corrige el documento de identidad del comprador.'),
    ).toBeInTheDocument();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
  });

  it('subsanación iniciada por el operador: muestra el motivo del rechazo del OT como guía', () => {
    const { container } = render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_OPERATOR_DRIVEN}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
      />,
    );

    expect(container).toHaveTextContent(
      'Motivo del rechazo: Documentos ilegibles; vuelve a cargar la cédula del comprador.',
    );
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
  });
});

describe('SubsanacionPanel — AC2: Re-radicar', () => {
  it('sin canReradicar: Re-radicar permanece deshabilitado aunque el checklist esté completo', async () => {
    const user = userEvent.setup();
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITH_ITEMS}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
        canReradicar={false}
      />,
    );

    const boton = screen.getByRole('button', { name: /re-radicar/i });
    const checkboxes = screen.getAllByRole('checkbox');
    await user.click(checkboxes[0]);
    await user.click(checkboxes[1]);
    expect(boton).toBeDisabled();
  });

  it('con checklist + canReradicar: Re-radicar se habilita al marcar todos y dispara submit', async () => {
    const user = userEvent.setup();
    const onReradicado = vi.fn();
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITH_ITEMS}
        loading={false}
        error={null}
        onReradicado={onReradicado}
        canReradicar
      />,
    );

    const boton = screen.getByRole('button', { name: /re-radicar/i });
    expect(boton).toBeDisabled();

    const checkboxes = screen.getAllByRole('checkbox');
    await user.click(checkboxes[0]);
    expect(boton).toBeDisabled();

    await user.click(checkboxes[1]);
    expect(boton).toBeEnabled();

    await user.click(boton);

    await waitFor(() => expect(mocks.submitInstance).toHaveBeenCalledWith('inst-1'));
    await waitFor(() => expect(onReradicado).toHaveBeenCalled());
  });

  it('sin checklist: Re-radicar solo se habilita con canReradicar', () => {
    const { rerender } = render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
        canReradicar={false}
      />,
    );
    expect(screen.getByRole('button', { name: /re-radicar/i })).toBeDisabled();

    rerender(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
        canReradicar
      />,
    );
    expect(screen.getByRole('button', { name: /re-radicar/i })).toBeEnabled();
  });

  it('hasUnsavedChanges: Re-radicar deshabilitado aunque canReradicar', () => {
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
        canReradicar
        hasUnsavedChanges
      />,
    );
    expect(screen.getByRole('button', { name: /re-radicar/i })).toBeDisabled();
  });

  it('Cancelar: invoca onCancelSubsanacion cuando showCancel', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn().mockResolvedValue(undefined);
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_OPERATOR_DRIVEN}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
        showCancel
        onCancelSubsanacion={onCancel}
      />,
    );

    await user.click(screen.getByRole('button', { name: /^cancelar$/i }));
    await waitFor(() => expect(onCancel).toHaveBeenCalled());
  });
});

// HU #10874 — panel de subsanación: motivo + checklist (AC1) y Re-radicar (AC2).
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
});

describe('SubsanacionPanel — AC2: Re-radicar', () => {
  it('con checklist: Re-radicar queda deshabilitado hasta marcar todos los ítems, luego dispara submitInstance', async () => {
    const user = userEvent.setup();
    const onReradicado = vi.fn();
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITH_ITEMS}
        loading={false}
        error={null}
        onReradicado={onReradicado}
      />,
    );

    const boton = screen.getByRole('button', { name: /re-radicar/i });
    expect(boton).toBeDisabled();

    const checkboxes = screen.getAllByRole('checkbox');
    await user.click(checkboxes[0]);
    expect(boton).toBeDisabled(); // aún falta el segundo ítem

    await user.click(checkboxes[1]);
    expect(boton).toBeEnabled();

    await user.click(boton);

    await waitFor(() => expect(mocks.submitInstance).toHaveBeenCalledWith('inst-1'));
    await waitFor(() => expect(onReradicado).toHaveBeenCalled());
  });

  it('sin checklist estructurado: Re-radicar está habilitado de entrada', async () => {
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        onReradicado={vi.fn()}
      />,
    );
    expect(screen.getByRole('button', { name: /re-radicar/i })).toBeEnabled();
  });

  it('gate de re-radicación (422/409) del submit: muestra el mensaje del backend y no navega', async () => {
    mocks.submitInstance.mockRejectedValue(new Error('Faltan documentos obligatorios para radicar.'));
    const user = userEvent.setup();
    const onReradicado = vi.fn();
    render(
      <SubsanacionPanel
        instanceId="inst-1"
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        onReradicado={onReradicado}
      />,
    );

    await user.click(screen.getByRole('button', { name: /re-radicar/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Faltan documentos obligatorios para radicar.',
    );
    expect(onReradicado).not.toHaveBeenCalled();
  });
});

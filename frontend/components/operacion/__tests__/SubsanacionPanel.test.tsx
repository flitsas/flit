// HU #10874 — panel de subsanación: motivo + checklist (AC1). El botón "Re-radicar" (AC2) se
// movió al pie del asistente, así que aquí solo se prueba la señal del checklist y Cancelar.
// Feature #11066 — Cancelar sale del flag.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { StatusHistory } from '@/lib/api/types/procedure-runtime';

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

// El panel ya no llama al cliente HTTP: el submit de Re-radicar lo dispara el pie del asistente.
beforeEach(() => {
  vi.clearAllMocks();
});

describe('SubsanacionPanel — estados de carga/error', () => {
  it('estado cargando: muestra indicador accesible', () => {
    render(
      <SubsanacionPanel
        statusHistory={[]}
        loading
        error={null}
      />,
    );
    expect(screen.getByRole('status')).toHaveTextContent(/cargando/i);
  });

  it('estado error: muestra el mensaje de fallo del fetch', () => {
    render(
      <SubsanacionPanel
        statusHistory={[]}
        loading={false}
        error="Error de red"
      />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/Error de red/);
  });
});

describe('SubsanacionPanel — AC1: motivo y checklist', () => {
  it('con metadata estructurada: pinta el motivo y cada ítem del checklist como checkbox editable', () => {
    render(
      <SubsanacionPanel
        statusHistory={HISTORY_WITH_ITEMS}
        loading={false}
        error={null}
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
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
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
        statusHistory={HISTORY_OPERATOR_DRIVEN}
        loading={false}
        error={null}
      />,
    );

    expect(container).toHaveTextContent(
      'Motivo del rechazo: Documentos ilegibles; vuelve a cargar la cédula del comprador.',
    );
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
  });
});

/**
 * AC2 — "Re-radicar" ya NO vive en el panel: es la acción terminal del asistente y está en el pie,
 * junto a Guardar y continuar (ver `hu10874-subsanacion-wizard.test.tsx` para el flujo completo).
 * Lo que el panel conserva es el checklist, y su única obligación hacia fuera es decir si está
 * resuelto: ese es el gate que el pie consulta.
 */
describe('SubsanacionPanel — AC2: señal del checklist hacia el pie', () => {
  it('el botón de re-radicar no está en el panel', () => {
    render(
      <SubsanacionPanel
        statusHistory={HISTORY_WITH_ITEMS}
        loading={false}
        error={null}
        canReradicar
      />,
    );
    expect(screen.queryByRole('button', { name: /re-radicar/i })).not.toBeInTheDocument();
  });

  it('con checklist: reporta `false` al montar y `true` solo al marcar todos los ítems', async () => {
    const user = userEvent.setup();
    const onChecklistResueltoChange = vi.fn();
    render(
      <SubsanacionPanel
        statusHistory={HISTORY_WITH_ITEMS}
        loading={false}
        error={null}
        onChecklistResueltoChange={onChecklistResueltoChange}
      />,
    );

    expect(onChecklistResueltoChange).toHaveBeenLastCalledWith(false);

    const checkboxes = screen.getAllByRole('checkbox');
    await user.click(checkboxes[0]);
    expect(onChecklistResueltoChange).toHaveBeenLastCalledWith(false);

    await user.click(checkboxes[1]);
    await waitFor(() => expect(onChecklistResueltoChange).toHaveBeenLastCalledWith(true));

    // Desmarcar vuelve a cerrar el gate: el pie tiene que reaccionar en los dos sentidos.
    await user.click(checkboxes[1]);
    await waitFor(() => expect(onChecklistResueltoChange).toHaveBeenLastCalledWith(false));
  });

  it('sin checklist: reporta `true` desde el primer render (no hay nada que marcar)', () => {
    const onChecklistResueltoChange = vi.fn();
    render(
      <SubsanacionPanel
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        onChecklistResueltoChange={onChecklistResueltoChange}
      />,
    );
    expect(onChecklistResueltoChange).toHaveBeenCalledWith(true);
  });

  it('explica por qué Re-radicar aún no está disponible y dónde encontrarlo', () => {
    const { rerender } = render(
      <SubsanacionPanel statusHistory={HISTORY_WITHOUT_METADATA} loading={false} error={null} />,
    );
    expect(screen.getByText(/Re-radicar te espera en el pie del asistente/)).toBeInTheDocument();

    rerender(
      <SubsanacionPanel
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        hasUnsavedChanges
      />,
    );
    expect(screen.getByText(/Hay cambios sin guardar/)).toBeInTheDocument();

    rerender(
      <SubsanacionPanel
        statusHistory={HISTORY_WITHOUT_METADATA}
        loading={false}
        error={null}
        canReradicar
      />,
    );
    expect(screen.getByText(/Cambios guardados/)).toBeInTheDocument();
  });

  // Las dos salidas están fuera del panel: Re-radicar en el pie y Cancelar subsanación en el
  // enlace de la cabecera. El panel no ofrece NINGÚN botón — solo el checklist.
  it('no ofrece ninguna salida: ni re-radicar ni cancelar la subsanación', () => {
    render(
      <SubsanacionPanel statusHistory={HISTORY_OPERATOR_DRIVEN} loading={false} error={null} />,
    );

    expect(screen.queryAllByRole('button')).toHaveLength(0);
    // …pero sí dice dónde están: el gestor no puede quedarse buscándolas.
    expect(
      screen.getAllByText(/«Cancelar subsanación» arriba a la derecha/).length,
    ).toBeGreaterThan(0);
  });
});

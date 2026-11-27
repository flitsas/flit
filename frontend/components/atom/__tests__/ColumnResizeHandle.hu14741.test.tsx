import { useState } from 'react';
import { describe, expect, it, vi, beforeAll } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import {
  COLUMN_RESIZE_MIN_PX,
  ColumnResizeHandle,
  useColumnWidths,
} from '../ColumnResizeHandle';

// HU #14741 — ajustar el ancho de las columnas arrastrando el borde de la cabecera.

beforeAll(() => {
  // jsdom no trae PointerEvent: sin él `clientX` se pierde en los eventos de puntero.
  if (typeof window.PointerEvent === 'undefined') {
    class PointerEventPoly extends MouseEvent {
      pointerId: number;
      constructor(type: string, init: PointerEventInit = {}) {
        super(type, init);
        this.pointerId = init.pointerId ?? 1;
      }
    }
    (window as unknown as { PointerEvent: unknown }).PointerEvent = PointerEventPoly;
  }
});

const KEYS = ['radicado', 'placa'];

/** Tabla mínima con el mismo cableado que usan las tres tablas: hook de anchos + tirador por cabecera. */
function Harness({
  initial = {},
  onSave = vi.fn(),
  onReset = vi.fn(),
}: {
  initial?: Record<string, number>;
  onSave?: (k: string, px: number) => void;
  onReset?: (k: string) => void;
}) {
  const [saved, setSaved] = useState<Record<string, number>>(initial);
  const widths = useColumnWidths(
    saved,
    KEYS,
    (k, px) => {
      onSave(k, px);
      setSaved((s) => ({ ...s, [k]: px }));
    },
    (k) => {
      onReset(k);
      setSaved((s) => {
        const { [k]: omitido, ...rest } = s;
        void omitido;
        return rest;
      });
    },
  );
  return (
    <table>
      <thead>
        <tr>
          {KEYS.map((k) => (
            <th key={k} style={{ position: 'sticky', width: widths.widths[k] ?? 120 }}>
              {k}
              <ColumnResizeHandle
                label={k}
                width={widths.widths[k] ?? 120}
                onPreview={(px) => widths.preview(k, px)}
                onCommit={(px) => widths.commit(k, px)}
                onReset={() => widths.reset(k)}
              />
            </th>
          ))}
        </tr>
      </thead>
    </table>
  );
}

const tirador = (k: string) => screen.getByRole('separator', { name: `Ancho de ${k}` });

describe('HU #14741 — tirador de ancho de columna', () => {
  it('AC1: arrastrar cambia el ancho al momento y se guarda UNA sola vez al soltar', () => {
    const onSave = vi.fn();
    render(<Harness onSave={onSave} />);
    const t = tirador('radicado');
    fireEvent.pointerDown(t, { clientX: 100, button: 0, pointerId: 1 });
    fireEvent.pointerMove(t, { clientX: 130, pointerId: 1 });
    expect(t).toHaveAttribute('aria-valuenow', '150');
    fireEvent.pointerMove(t, { clientX: 160, pointerId: 1 });
    expect(t).toHaveAttribute('aria-valuenow', '180');
    expect(onSave).not.toHaveBeenCalled();
    fireEvent.pointerUp(t, { clientX: 160, pointerId: 1 });
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave).toHaveBeenCalledWith('radicado', 180);
    expect(t).toHaveAttribute('aria-valuenow', '180');
  });

  it('AC2: doble clic restablece el ancho por defecto de esa columna', () => {
    const onReset = vi.fn();
    render(<Harness initial={{ radicado: 300 }} onReset={onReset} />);
    expect(tirador('radicado')).toHaveAttribute('aria-valuenow', '300');
    fireEvent.doubleClick(tirador('radicado'));
    expect(onReset).toHaveBeenCalledWith('radicado');
    expect(tirador('radicado')).toHaveAttribute('aria-valuenow', '120');
  });

  it('AC3: no baja del ancho mínimo legible', () => {
    const onSave = vi.fn();
    render(<Harness onSave={onSave} />);
    const t = tirador('placa');
    fireEvent.pointerDown(t, { clientX: 500, button: 0, pointerId: 1 });
    fireEvent.pointerMove(t, { clientX: 0, pointerId: 1 });
    expect(t).toHaveAttribute('aria-valuenow', String(COLUMN_RESIZE_MIN_PX));
    fireEvent.pointerUp(t, { clientX: 0, pointerId: 1 });
    expect(onSave).toHaveBeenCalledWith('placa', COLUMN_RESIZE_MIN_PX);
  });

  it('AC4: es un separador enfocable y ←/→ bajan o suben 16 px, actualizan aria-valuenow y guardan', () => {
    const onSave = vi.fn();
    render(<Harness onSave={onSave} />);
    const t = tirador('radicado');
    expect(t).toHaveAttribute('aria-orientation', 'vertical');
    expect(t).toHaveAttribute('aria-valuemin', String(COLUMN_RESIZE_MIN_PX));
    expect(t).toHaveAttribute('aria-valuemax');
    expect(t).toHaveAttribute('tabindex', '0');
    t.focus();
    expect(t).toHaveFocus();
    fireEvent.keyDown(t, { key: 'ArrowRight' });
    expect(t).toHaveAttribute('aria-valuenow', '136');
    fireEvent.keyDown(t, { key: 'ArrowLeft' });
    fireEvent.keyDown(t, { key: 'ArrowLeft' });
    expect(t).toHaveAttribute('aria-valuenow', '104');
    expect(onSave).toHaveBeenCalledTimes(3);
    expect(onSave).toHaveBeenLastCalledWith('radicado', 104);
  });

  it('un ancho guardado de una columna que ya no existe se ignora', () => {
    render(<Harness initial={{ fantasma: 500, placa: 200 }} />);
    expect(tirador('placa')).toHaveAttribute('aria-valuenow', '200');
    expect(tirador('radicado')).toHaveAttribute('aria-valuenow', '120');
  });

  it('un clic sobre el tirador no se propaga (no dispara el orden de la cabecera)', () => {
    const onClick = vi.fn();
    render(
      <div onClick={onClick}>
        <Harness />
      </div>,
    );
    fireEvent.click(tirador('radicado'));
    expect(onClick).not.toHaveBeenCalled();
  });
});

import { useRef } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';

import { useWizardFocusTrap } from '../use-wizard-focus-trap';

// Uso de ejemplo: useWizardFocusTrap(cardRef, { active: true, onEscape: onClose }) — atrapa Tab,
// cierra con Escape y devuelve el foco al disparador al desmontar.
// Bug #13194 punto 1: un `onEscape` nuevo por render (arrow inline) NO debe re-ejecutar el foco
// inicial; Escape debe invocar siempre el `onEscape` más reciente.

function Dialogo({ onEscape }: { onEscape: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useWizardFocusTrap(ref, { active: true, onEscape });
  return (
    <div ref={ref}>
      <button type="button">Cerrar</button>
      <textarea aria-label="Motivo" />
    </div>
  );
}

describe('useWizardFocusTrap', () => {
  it('enfoca el primer focusable al montar (sin cambio de UX)', () => {
    render(<Dialogo onEscape={() => {}} />);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Cerrar' }));
  });

  it('un rerender con un onEscape NUEVO no mueve el foco del elemento enfocado', () => {
    const { rerender } = render(<Dialogo onEscape={() => {}} />);
    const motivo = screen.getByLabelText('Motivo');
    motivo.focus();
    expect(document.activeElement).toBe(motivo);

    rerender(<Dialogo onEscape={() => {}} />);

    expect(document.activeElement).toBe(motivo);
  });

  it('Escape invoca el onEscape más reciente, no el capturado al montar', () => {
    const primero = vi.fn();
    const segundo = vi.fn();
    const { rerender } = render(<Dialogo onEscape={primero} />);
    rerender(<Dialogo onEscape={segundo} />);

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(segundo).toHaveBeenCalledTimes(1);
    expect(primero).not.toHaveBeenCalled();
  });

  it('devuelve el foco al disparador al desmontar', () => {
    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();
    const { unmount } = render(<Dialogo onEscape={() => {}} />);
    unmount();
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });
});

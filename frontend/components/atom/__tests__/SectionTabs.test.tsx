import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SectionTabs } from '../SectionTabs';

const tabs = [
  { id: 'a', label: 'Primera', content: <p>A</p> },
  { id: 'b', label: 'Segunda', content: <p>B</p> },
] as const;

describe('SectionTabs: color en tema oscuro', () => {
  it('la pestaña inactiva no fija el color con estilo inline y usa el texto secundario dark', () => {
    render(<SectionTabs tabs={tabs} active="a" onChange={() => {}} ariaLabel="Secciones" />);
    const inactiva = screen.getByRole('tab', { name: 'Segunda' });
    expect(inactiva.getAttribute('style') ?? '').not.toMatch(/color|opacity/i);
    // UI Kit 2026 (propuesta-2): el color sale de `.kit-pestana` (--kit-gris, que ya cambia en oscuro), no de clases sueltas.
    expect(inactiva.className).toContain('kit-pestana');
    expect(inactiva.className).not.toContain('kit-pestana--activa');
  });

  it('la activa conserva su azul y sin estilo inline', () => {
    render(<SectionTabs tabs={tabs} active="a" onChange={() => {}} ariaLabel="Secciones" />);
    const activa = screen.getByRole('tab', { name: 'Primera' });
    expect(activa.getAttribute('style') ?? '').toBe('');
    // UI Kit 2026: la activa va en --kit-azul-texto (#1D53FF; #8FB0FF en oscuro) por `.kit-pestana--activa`.
    expect(activa.className).toContain('kit-pestana--activa');
    expect(activa.className).not.toContain('dark:text-white');
  });
});

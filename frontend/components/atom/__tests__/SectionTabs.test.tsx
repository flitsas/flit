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
    expect(inactiva.className).toContain('dark:text-white/[0.78]');
    expect(inactiva.className).toContain('dark:opacity-100');
    expect(inactiva.className).toContain('text-[#162744]');
  });

  it('la activa conserva su azul y sin estilo inline', () => {
    render(<SectionTabs tabs={tabs} active="a" onChange={() => {}} ariaLabel="Secciones" />);
    const activa = screen.getByRole('tab', { name: 'Primera' });
    expect(activa.getAttribute('style') ?? '').toBe('');
    expect(activa.className).toContain('text-[#557EFF]');
    expect(activa.className).not.toContain('dark:text-white');
  });
});

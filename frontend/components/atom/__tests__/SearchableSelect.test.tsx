import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { SearchableSelect } from '@/components/atom/SearchableSelect';

/**
 * Combobox con buscador interno usado por todos los pickers de compañía. Lo relevante: que se pueda
 * teclear para filtrar (el motivo de existir), que el teclado funcione completo y que exponga los
 * roles ARIA del patrón APG — el repo exige WCAG 2.1 AA.
 */
describe('SearchableSelect', () => {
  const OPCIONES = [
    { value: 'a', label: 'Movilidad Bogotá', hint: '900111222' },
    { value: 'b', label: 'Tránsito Medellín', hint: '900333444' },
    { value: 'c', label: 'Secretaría de Cali', hint: '900555666' },
  ];

  const onChange = vi.fn();

  function renderizar(props: Partial<React.ComponentProps<typeof SearchableSelect>> = {}) {
    return render(
      <SearchableSelect
        label="Compañía"
        options={OPCIONES}
        value=""
        onChange={onChange}
        defaultLabel="Mi compañía"
        {...props}
      />,
    );
  }

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('expone el patrón combobox de APG', async () => {
    const user = userEvent.setup();
    renderizar();

    const input = screen.getByRole('combobox', { name: 'Compañía' });
    expect(input).toHaveAttribute('aria-expanded', 'false');

    await user.click(input);

    expect(input).toHaveAttribute('aria-expanded', 'true');
    const lista = screen.getByRole('listbox', { name: 'Compañía' });
    expect(within(lista).getAllByRole('option')).toHaveLength(4); // 3 + "Mi compañía"
    // La opción activa se señala por aria-activedescendant; el foco NO se mueve del input.
    expect(input).toHaveAttribute('aria-activedescendant');
    expect(input).toHaveFocus();
  });

  it('filtra por nombre mientras se teclea', async () => {
    const user = userEvent.setup();
    renderizar();

    await user.click(screen.getByRole('combobox', { name: 'Compañía' }));
    await user.keyboard('medell');

    const opciones = within(screen.getByRole('listbox')).getAllByRole('option');
    expect(opciones).toHaveLength(1);
    expect(opciones[0]).toHaveTextContent('Tránsito Medellín');
  });

  it('filtra sin distinguir tildes ni mayúsculas', async () => {
    const user = userEvent.setup();
    renderizar();

    await user.click(screen.getByRole('combobox', { name: 'Compañía' }));
    await user.keyboard('BOGOTA'); // sin tilde y en mayúsculas

    expect(within(screen.getByRole('listbox')).getAllByRole('option')[0]).toHaveTextContent(
      'Movilidad Bogotá',
    );
  });

  it('filtra también por la pista (NIT)', async () => {
    const user = userEvent.setup();
    renderizar();

    await user.click(screen.getByRole('combobox', { name: 'Compañía' }));
    await user.keyboard('900333');

    const opciones = within(screen.getByRole('listbox')).getAllByRole('option');
    expect(opciones).toHaveLength(1);
    expect(opciones[0]).toHaveTextContent('Tránsito Medellín');
  });

  it('avisa cuando el término no coincide con nada', async () => {
    const user = userEvent.setup();
    renderizar();

    await user.click(screen.getByRole('combobox', { name: 'Compañía' }));
    await user.keyboard('zzzz');

    expect(screen.getByText('Sin coincidencias')).toBeInTheDocument();
    expect(within(screen.getByRole('listbox')).queryAllByRole('option')).toHaveLength(0);
  });

  it('elige con el ratón', async () => {
    const user = userEvent.setup();
    renderizar();

    await user.click(screen.getByRole('combobox', { name: 'Compañía' }));
    await user.click(screen.getByRole('option', { name: /Tránsito Medellín/ }));

    expect(onChange).toHaveBeenCalledWith('b');
    await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument());
  });

  it('elige con el teclado: flechas + Enter', async () => {
    const user = userEvent.setup();
    renderizar();

    const input = screen.getByRole('combobox', { name: 'Compañía' });
    await user.click(input);
    // Arranca en "Mi compañía" (la seleccionada): dos flechas abajo llegan a la segunda empresa.
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith('b');
  });

  it('Escape cierra sin elegir y descarta lo tecleado', async () => {
    const user = userEvent.setup();
    renderizar();

    const input = screen.getByRole('combobox', { name: 'Compañía' });
    await user.click(input);
    await user.keyboard('medell{Escape}');

    expect(onChange).not.toHaveBeenCalled();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(input).toHaveValue('Mi compañía');
  });

  it('con una selección hecha, el input muestra su etiqueta y se puede limpiar', async () => {
    const user = userEvent.setup();
    renderizar({ value: 'c' });

    const input = screen.getByRole('combobox', { name: 'Compañía' });
    expect(input).toHaveValue('Secretaría de Cali');

    await user.click(screen.getByRole('button', { name: /quitar selección/i }));
    expect(onChange).toHaveBeenCalledWith('');
  });

  it('sin defaultLabel no ofrece opción vacía ni botón de limpiar', async () => {
    const user = userEvent.setup();
    renderizar({ defaultLabel: undefined, value: 'a' });

    expect(screen.queryByRole('button', { name: /quitar selección/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole('combobox', { name: 'Compañía' }));
    expect(within(screen.getByRole('listbox')).getAllByRole('option')).toHaveLength(3);
  });

  it('deshabilitado no abre la lista', async () => {
    const user = userEvent.setup();
    renderizar({ disabled: true });

    await user.click(screen.getByRole('combobox', { name: 'Compañía' }));
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });
});

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ColumnSelector } from '../ColumnSelector';

const COLUMNS = [
  { key: 'a', label: 'Columna A' },
  { key: 'b', label: 'Columna B' },
  { key: 'c', label: 'Columna C' },
];

describe('ColumnSelector', () => {
  it('el botón disparador tiene nombre accesible y el panel arranca cerrado', () => {
    render(<ColumnSelector columns={COLUMNS} visible={['a', 'b']} onChange={vi.fn()} label="Columnas" />);
    expect(screen.getByRole('button', { name: /columnas/i })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('abre el panel con un checkbox por columna, marcados según `visible`', async () => {
    const user = userEvent.setup();
    render(<ColumnSelector columns={COLUMNS} visible={['a', 'c']} onChange={vi.fn()} label="Columnas" />);
    await user.click(screen.getByRole('button', { name: /columnas/i }));

    expect(screen.getByRole('checkbox', { name: 'Columna A' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Columna B' })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Columna C' })).toBeChecked();
  });

  it('marcar una columna oculta llama onChange respetando el orden canónico de `columns`', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ColumnSelector columns={COLUMNS} visible={['a']} onChange={onChange} label="Columnas" />);
    await user.click(screen.getByRole('button', { name: /columnas/i }));
    await user.click(screen.getByRole('checkbox', { name: 'Columna C' }));

    expect(onChange).toHaveBeenCalledWith(['a', 'c']);
  });

  it('desmarcar una columna la quita de la selección', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ColumnSelector columns={COLUMNS} visible={['a', 'b']} onChange={onChange} label="Columnas" />);
    await user.click(screen.getByRole('button', { name: /columnas/i }));
    await user.click(screen.getByRole('checkbox', { name: 'Columna A' }));

    expect(onChange).toHaveBeenCalledWith(['b']);
  });

  it('impide dejar cero columnas visibles: el checkbox de la última marcada queda deshabilitado', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ColumnSelector columns={COLUMNS} visible={['a']} onChange={onChange} label="Columnas" />);
    await user.click(screen.getByRole('button', { name: /columnas/i }));

    const onlyChecked = screen.getByRole('checkbox', { name: 'Columna A' });
    expect(onlyChecked).toBeDisabled();
    await user.click(onlyChecked);
    expect(onChange).not.toHaveBeenCalled();
  });

  it('Escape cierra el panel y devuelve el foco al disparador', async () => {
    const user = userEvent.setup();
    render(<ColumnSelector columns={COLUMNS} visible={['a', 'b']} onChange={vi.fn()} label="Columnas" />);
    const trigger = screen.getByRole('button', { name: /columnas/i });
    await user.click(trigger);
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('un clic fuera del panel lo cierra', async () => {
    const user = userEvent.setup();
    render(
      <div>
        <ColumnSelector columns={COLUMNS} visible={['a', 'b']} onChange={vi.fn()} label="Columnas" />
        <button type="button">Afuera</button>
      </div>,
    );
    await user.click(screen.getByRole('button', { name: /columnas/i }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Afuera' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('el checkbox es alcanzable y activable solo con teclado (Tab + Espacio)', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ColumnSelector columns={COLUMNS} visible={['a']} onChange={onChange} label="Columnas" />);
    await user.click(screen.getByRole('button', { name: /columnas/i }));

    // El checkbox de "Columna A" (única marcada) está deshabilitado; se navega al de "Columna B".
    await user.tab();
    expect(screen.getByRole('checkbox', { name: 'Columna B' })).toHaveFocus();
    await user.keyboard(' ');
    expect(onChange).toHaveBeenCalledWith(['a', 'b']);
  });

  it('disabled deshabilita el disparador (p. ej. mientras guarda)', () => {
    render(<ColumnSelector columns={COLUMNS} visible={['a']} onChange={vi.fn()} label="Columnas" disabled />);
    expect(screen.getByRole('button', { name: /columnas/i })).toBeDisabled();
  });
});

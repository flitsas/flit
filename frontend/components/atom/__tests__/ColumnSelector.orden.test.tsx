import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ColumnSelector } from '../ColumnSelector';
import { useUiPreferences } from '@/hooks/useUiPreferences';
import { uiPreferencesClient, type UiPreferenceScope } from '@/lib/api/ui-preferences';
import { ToastProvider } from '@/components/admin/Toast';

vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: { get: vi.fn(), put: vi.fn() },
}));

const COLUMNS = [
  { key: 'a', label: 'Columna A' },
  { key: 'b', label: 'Columna B' },
  { key: 'c', label: 'Columna C' },
  { key: 'd', label: 'Columna D' },
];
const CATALOG = COLUMNS.map((c) => c.key);
const DEFAULTS = ['a', 'b', 'c'];

function Harness({ scope = 'tramites.columns' }: { scope?: UiPreferenceScope }) {
  const { visible, order, setVisible, setOrder, reset, status } = useUiPreferences(scope, DEFAULTS, {
    catalog: CATALOG,
  });
  return (
    <>
      <p data-testid="estado">{status}</p>
      <p data-testid="tabla">{visible.join(',')}</p>
      <ColumnSelector
        columns={COLUMNS}
        visible={visible}
        onChange={setVisible}
        order={order}
        onReorder={setOrder}
        onReset={reset}
        label="Columnas"
      />
    </>
  );
}

async function abrir(value: Record<string, unknown> = {}) {
  vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.columns', value });
  vi.mocked(uiPreferencesClient.put).mockResolvedValue({ scope: 'tramites.columns', value: {} });
  const user = userEvent.setup();
  render(
    <ToastProvider>
      <Harness />
    </ToastProvider>,
  );
  await waitFor(() => expect(screen.getByTestId('estado')).toHaveTextContent('ready'));
  await user.click(screen.getByRole('button', { name: /columnas: elegir/i }));
  return user;
}

const ultimoPut = () => vi.mocked(uiPreferencesClient.put).mock.calls.at(-1)![1];

describe('ColumnSelector — orden y «Por defecto» (HU #14740)', () => {
  afterEach(() => vi.resetAllMocks());

  it('AC1: «Por defecto» es la primera acción del panel, restablece visibles y orden y lo guarda', async () => {
    const user = await abrir({ visible: ['c', 'a', 'd'], order: ['c', 'a', 'd', 'b'], widths: { a: 100 } });
    expect(screen.getByTestId('tabla')).toHaveTextContent('c,a,d');

    const panel = screen.getByRole('dialog');
    expect(within(panel).getAllByRole('button')[0]).toHaveTextContent('Por defecto');

    await user.click(screen.getByRole('button', { name: 'Por defecto' }));
    expect(screen.getByTestId('tabla')).toHaveTextContent('a,b,c');
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalled());
    const put = ultimoPut();
    expect(put.visible).toEqual(['a', 'b', 'c']);
    expect(put.order).toBeUndefined();
    expect(put.widths).toBeUndefined(); // preparado para los anchos de otra HU
  });

  it('AC2: arrastrar una columna en el panel cambia el orden y lo persiste', async () => {
    const user = await abrir();
    const handleA = screen.getByRole('button', { name: /Mover Columna A/ });
    const filaC = screen.getByRole('checkbox', { name: 'Columna C' }).closest('[data-column-key]')!;
    fireEvent.dragStart(handleA);
    fireEvent.dragOver(filaC);
    fireEvent.drop(filaC);

    expect(screen.getByTestId('tabla')).toHaveTextContent('b,c,a');
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalled());
    expect(ultimoPut().order).toEqual(['b', 'c', 'a', 'd']);
    expect(ultimoPut().visible).toEqual(['b', 'c', 'a']);
    void user;
  });

  it('AC3: con el foco en el asa, ↓ y ↑ mueven una posición y se anuncia la nueva posición', async () => {
    const user = await abrir();
    screen.getByRole('button', { name: /Mover Columna A/ }).focus();
    await user.keyboard('{ArrowDown}');

    expect(screen.getByTestId('tabla')).toHaveTextContent('b,a,c');
    expect(screen.getAllByRole('status').some((n) => n.textContent === 'Columna A: posición 2 de 4')).toBe(true);
    // El foco sigue en el asa de la misma columna.
    expect(screen.getByRole('button', { name: /Mover Columna A, posición 2 de 4/ })).toHaveFocus();

    await user.keyboard('{ArrowUp}');
    expect(screen.getByTestId('tabla')).toHaveTextContent('a,b,c');
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalledTimes(2));
  });

  it('no permite dejar cero columnas visibles', async () => {
    const user = await abrir({ visible: ['a'], order: ['a', 'b', 'c', 'd'] });
    expect(screen.getByRole('checkbox', { name: 'Columna A' })).toBeDisabled();
    void user;
  });

  it('AC4: la vista es por usuario y por tabla — cada scope se lee y se guarda aparte', async () => {
    vi.mocked(uiPreferencesClient.get).mockImplementation(async (scope) => ({
      scope,
      value: scope === 'tramites.columns' ? { visible: ['c', 'a'], order: ['c', 'a', 'b', 'd'] } : {},
    }));
    vi.mocked(uiPreferencesClient.put).mockResolvedValue({ scope: 'x', value: {} });
    render(
      <ToastProvider>
        <Harness scope="tramites.columns" />
        <Harness scope="identidad.columns" />
      </ToastProvider>,
    );
    await waitFor(() => expect(screen.getAllByTestId('estado').every((n) => n.textContent === 'ready')).toBe(true));
    const tablas = screen.getAllByTestId('tabla');
    expect(tablas[0]).toHaveTextContent('c,a');
    expect(tablas[1]).toHaveTextContent('a,b,c');
    expect(uiPreferencesClient.get).toHaveBeenCalledWith('tramites.columns');
    expect(uiPreferencesClient.get).toHaveBeenCalledWith('identidad.columns');

    const user = userEvent.setup();
    await user.click(screen.getAllByRole('button', { name: /columnas: elegir/i })[1]);
    await user.click(screen.getByRole('button', { name: 'Por defecto' }));
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalled());
    expect(vi.mocked(uiPreferencesClient.put).mock.calls.every(([s]) => s === 'identidad.columns')).toBe(true);
  });

  it('AC5: una columna nueva del producto entra en su posición del catálogo y respeta el orden guardado', async () => {
    // El usuario conocía a/b/c (known) con orden c,b,a; 'd' es nueva y por defecto no es visible.
    await abrir({ visible: ['c', 'b', 'a'], order: ['c', 'b', 'a'], known: ['a', 'b', 'c'] });
    expect(screen.getByTestId('tabla')).toHaveTextContent('c,b,a');
    const filas = Array.from(screen.getByRole('dialog').querySelectorAll('[data-column-key]')).map((n) =>
      n.getAttribute('data-column-key'),
    );
    expect(filas).toEqual(['c', 'd', 'b', 'a']); // 'd' queda tras su vecina previa del catálogo ('c')
  });

  it('AC5: preferencia antigua sin `order` conserva el orden del catálogo', async () => {
    await abrir({ visible: ['b', 'a'] });
    expect(screen.getByTestId('tabla')).toHaveTextContent('a,b');
  });

  it('AC6: si falla al guardar conserva la vista que se veía y avisa', async () => {
    const user = await abrir();
    vi.mocked(uiPreferencesClient.put).mockRejectedValue(new Error('boom'));
    screen.getByRole('button', { name: /Mover Columna A/ }).focus();
    await user.keyboard('{ArrowDown}');

    await waitFor(() => expect(screen.getByTestId('tabla')).toHaveTextContent('a,b,c'));
    expect(screen.getByTestId('tabla').textContent).not.toBe('');
    expect(await screen.findByText(/No se pudo guardar la vista de columnas/)).toBeInTheDocument();
  });
});

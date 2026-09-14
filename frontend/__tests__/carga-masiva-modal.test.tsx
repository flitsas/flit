import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { CargaMasivaModal } from '@/components/operacion/CargaMasivaModal';

vi.mock('@/lib/api/bulk-tramites-client', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api/bulk-tramites-client')>(
    '@/lib/api/bulk-tramites-client',
  );
  return {
    ...actual,
    bulkTramitesClient: {
      descargarPlantilla: vi.fn(),
      subirLote: vi.fn(),
    },
  };
});

vi.mock('@/components/consultas/export', () => ({ download: vi.fn() }));

import { bulkTramitesClient } from '@/lib/api/bulk-tramites-client';
import { download } from '@/components/consultas/export';

const descargar = vi.mocked(bulkTramitesClient.descargarPlantilla);
const subir = vi.mocked(bulkTramitesClient.subirLote);

function archivoXlsx(nombre = 'lote.xlsx') {
  return new File(['contenido'], nombre, {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
}

describe('CargaMasivaModal (HU #12521)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    descargar.mockResolvedValue({ blob: new Blob(['x']), filename: 'plantilla.xlsx' });
    subir.mockResolvedValue({ batchId: 'b-1', totalRows: 3, rowsWithStructuralErrors: 0 });
  });

  it('ofrece las tres plantillas y descarga la del tipo elegido', async () => {
    const user = userEvent.setup();
    render(<CargaMasivaModal open onClose={() => {}} />);

    expect(screen.getByTestId('carga-masiva-tipo-matricula')).toBeInTheDocument();
    expect(screen.getByTestId('carga-masiva-tipo-traspaso')).toBeInTheDocument();
    expect(screen.getByTestId('carga-masiva-tipo-otros')).toBeInTheDocument();

    // Matrícula viene elegida de entrada; al pedir la plantilla se descarga esa.
    await user.click(screen.getByTestId('carga-masiva-descargar'));
    await waitFor(() => expect(descargar).toHaveBeenCalledWith('matricula'));

    await user.click(screen.getByTestId('carga-masiva-tipo-traspaso'));
    await user.click(screen.getByTestId('carga-masiva-descargar'));
    await waitFor(() => expect(descargar).toHaveBeenLastCalledWith('traspaso'));

    expect(download).toHaveBeenCalledTimes(2);
  });

  it('no envía al backend un archivo que ni siquiera es .xlsx', async () => {
    render(<CargaMasivaModal open onClose={() => {}} />);

    // Se dispara el change a mano y no con `user.upload` a propósito: el `accept=".xlsx"` del
    // input hace que el navegador ni siquiera adjunte el archivo, así que por esa vía el guardián
    // nunca correría. Pero `accept` es una ayuda de selección, no un control —no aplica al
    // arrastrar ni a un archivo renombrado—, y esta comprobación es la que evita el viaje inútil
    // al backend.
    const input = screen.getByTestId('carga-masiva-archivo') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [archivoXlsx('lote.csv')] } });

    expect(await screen.findByText(/debe ser un Excel con extensión \.xlsx/i)).toBeInTheDocument();
    expect(subir).not.toHaveBeenCalled();
    // Sin archivo válido no se puede procesar.
    expect(screen.getByTestId('carga-masiva-subir')).toBeDisabled();
  });

  it('al encolar avisa que puede cerrar y seguir trabajando, y notifica al listado', async () => {
    const user = userEvent.setup();
    const onEncolado = vi.fn();
    render(<CargaMasivaModal open onClose={() => {}} onEncolado={onEncolado} />);

    await user.upload(screen.getByTestId('carga-masiva-archivo'), archivoXlsx());
    await user.click(screen.getByTestId('carga-masiva-subir'));

    const aviso = await screen.findByTestId('carga-masiva-encolado');
    expect(aviso).toHaveTextContent(/3 filas/);
    expect(aviso).toHaveTextContent(/seguir usando la aplicación/i);
    expect(onEncolado).toHaveBeenCalledWith({
      batchId: 'b-1',
      totalRows: 3,
      rowsWithStructuralErrors: 0,
    });
  });

  it('cuenta las filas que quedaron en error antes de procesarse', async () => {
    subir.mockResolvedValue({ batchId: 'b-2', totalRows: 5, rowsWithStructuralErrors: 2 });
    const user = userEvent.setup();
    render(<CargaMasivaModal open onClose={() => {}} />);

    await user.upload(screen.getByTestId('carga-masiva-archivo'), archivoXlsx());
    await user.click(screen.getByTestId('carga-masiva-subir'));

    expect(await screen.findByTestId('carga-masiva-encolado')).toHaveTextContent(
      /2 quedaron en error/i,
    );
  });

  it('muestra el motivo cuando el backend rechaza el archivo completo', async () => {
    subir.mockRejectedValue(new Error('El archivo no coincide con la plantilla.'));
    const user = userEvent.setup();
    render(<CargaMasivaModal open onClose={() => {}} />);

    await user.upload(screen.getByTestId('carga-masiva-archivo'), archivoXlsx());
    await user.click(screen.getByTestId('carga-masiva-subir'));

    expect(await screen.findByText(/no coincide con la plantilla/i)).toBeInTheDocument();
  });
});

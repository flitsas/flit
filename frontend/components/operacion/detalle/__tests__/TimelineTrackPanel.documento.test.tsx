// Bug #14604 — la línea de tiempo dice qué documento se actualizó y deja abrir el archivo nuevo y el anterior.
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { TimelineTrackPanel } from '../TimelineTrackPanel';
import { mapModificacionesToTimelineNodes } from '../timeline-mappers';
import type { StatusHistoryModification } from '@/lib/api/types/procedure-runtime';

const fila = (documento: StatusHistoryModification['documento']): StatusHistoryModification => ({
  id: 'e1',
  etiqueta: 'Documento actualizado por el administrador',
  campo: 'documento_admin',
  anterior: '',
  nuevo: 'nueva.pdf · Reemplaza a: vieja.pdf · Estado del trámite: Aprobado · Observación: Corrección',
  changedAt: '2026-11-18T19:00:00Z',
  changedByUserId: 'u1',
  changedByName: 'Usuario Demo',
  changedByCompania: 'Empresa Demo FLIT',
  documento,
});

describe('Línea de tiempo — documento actualizado por el administrador', () => {
  it('muestra el tipo, el archivo nuevo y el anterior, y abre cada uno con «Ver»', async () => {
    const onVer = vi.fn();
    const nodes = mapModificacionesToTimelineNodes([
      fila({
        tipo: 'impronta',
        attachmentId: 'a-nuevo',
        filename: 'nueva.pdf',
        anteriorAttachmentId: 'a-viejo',
        anteriorFilename: 'vieja.pdf',
      }),
    ]);
    render(<TimelineTrackPanel title="Línea de tiempo" nodes={nodes} onVerDocumento={onVer} />);

    expect(screen.getByText('Documento')).toBeInTheDocument();
    expect(screen.getByText('Archivo nuevo')).toBeInTheDocument();
    expect(screen.getByText('Archivo anterior')).toBeInTheDocument();
    expect(screen.getByText('vieja.pdf')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Ver archivo anterior vieja.pdf' }));
    expect(onVer).toHaveBeenCalledWith({ id: 'a-viejo', filename: 'vieja.pdf', tipo: 'impronta' });

    await userEvent.click(screen.getByRole('button', { name: 'Ver archivo nuevo nueva.pdf' }));
    expect(onVer).toHaveBeenLastCalledWith({ id: 'a-nuevo', filename: 'nueva.pdf', tipo: 'impronta' });
  });

  it('sin archivo anterior lo dice en vez de inventar un «Ver»', () => {
    const nodes = mapModificacionesToTimelineNodes([
      fila({ tipo: 'aduana', attachmentId: 'a1', filename: 'aduana.pdf' }),
    ]);
    render(<TimelineTrackPanel title="Línea de tiempo" nodes={nodes} onVerDocumento={vi.fn()} />);

    expect(screen.getByText('No había un archivo anterior')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /^Ver / })).toHaveLength(1);
  });

  it('las filas sin documento no pintan el bloque', () => {
    const nodes = mapModificacionesToTimelineNodes([fila(null)]);
    render(<TimelineTrackPanel title="Línea de tiempo" nodes={nodes} onVerDocumento={vi.fn()} />);
    expect(screen.queryByText('Archivo nuevo')).not.toBeInTheDocument();
  });
});

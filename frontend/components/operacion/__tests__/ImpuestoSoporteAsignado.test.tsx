// Modal «Enviar al OT» en `asignado`: sección «Cargar PDF del impuesto departamental», igual a la del SOAT.
import { useState } from 'react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';

const uploadAttachment = vi.fn();
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: { uploadAttachment: (...args: unknown[]) => uploadAttachment(...args) },
}));

import { ImpuestoSoporteAsignado } from '../ImpuestoSoporteAsignado';

function Harness({ onResultado }: { onResultado?: (r: { estado: string }) => void }) {
  const [abierto, setAbierto] = useState(false);
  return (
    <ImpuestoSoporteAsignado
      instanceId="inst-1"
      tenantId="tenant-1"
      abierto={abierto}
      onToggle={setAbierto}
      onResultado={onResultado}
    />
  );
}

const pdf = () => new File(['%PDF-1.4'], 'impuesto.pdf', { type: 'application/pdf' });

beforeEach(() => {
  uploadAttachment.mockReset();
});

describe('ImpuestoSoporteAsignado', () => {
  it('se despliega, sube el PDF como liquidacion_impuesto y avisa que quedó registrado', async () => {
    uploadAttachment.mockResolvedValue({ id: 'att-1' });
    const onResultado = vi.fn();
    render(<Harness onResultado={onResultado} />);

    const toggle = screen.getByRole('button', { name: /Cargar PDF del impuesto departamental/i });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');

    const input = screen.getByLabelText(/Selecciona el PDF del impuesto departamental/i);
    expect(input).toHaveAttribute('accept', 'application/pdf');
    const file = pdf();
    fireEvent.change(input, { target: { files: [file] } });

    expect(await screen.findByText('Impuesto registrado')).toBeInTheDocument();
    expect(uploadAttachment).toHaveBeenCalledWith('inst-1', 'liquidacion_impuesto', file, 'tenant-1');
    expect(onResultado).toHaveBeenCalledWith(expect.objectContaining({ estado: 'registrado' }));
  });

  it('si la carga falla lo dice sin cerrar la sección', async () => {
    uploadAttachment.mockRejectedValue(
      Object.assign(new Error('conflict'), { status: 409, problem: { error: 'adjunto_bloqueado_flito' } }),
    );
    render(<Harness />);

    fireEvent.click(screen.getByRole('button', { name: /Cargar PDF del impuesto departamental/i }));
    fireEvent.change(screen.getByLabelText(/Selecciona el PDF del impuesto departamental/i), {
      target: { files: [pdf()] },
    });

    await waitFor(() => expect(screen.getByText('Impuesto pendiente')).toBeInTheDocument());
    expect(screen.getByText('Este documento ya fue cargado y no se puede reemplazar.')).toBeInTheDocument();
  });
});

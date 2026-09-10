import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({ fetchDocumentRequirementsPreview: vi.fn() }));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: { fetchDocumentRequirementsPreview: mocks.fetchDocumentRequirementsPreview },
}));

import { ProcedureDocsPreviewInformativo } from '@/components/operacion/ProcedureDocsPreviewInformativo';

function doc(id: string, nombre: string, obligatorio: boolean, descripcion?: string) {
  return { documentTypeId: id, nombre, obligatorio, descripcion: descripcion ?? null };
}

function pintar() {
  return render(
    <ProcedureDocsPreviewInformativo
      procedureTypeCode="MATRICULA_NUEVA"
      open
      onOpenChange={vi.fn()}
    />,
  );
}

/**
 * Guía de documentos del paso 1.
 *
 * Los documentos se agrupan POR OBLIGATORIEDAD en dos bloques con su cabecera. Antes iban mezclados
 * en una sola lista y la diferencia era un «(opcional)» atenuado al final del nombre — lo más fácil
 * de pasar por alto justo cuando el gestor está reuniendo papeles.
 */
describe('ProcedureDocsPreviewInformativo', () => {
  beforeEach(() => {
    mocks.fetchDocumentRequirementsPreview.mockReset();
  });

  it('separa obligatorios de opcionales en dos bloques', async () => {
    mocks.fetchDocumentRequirementsPreview.mockResolvedValue([
      doc('1', 'Certificado CEPD', true),
      doc('2', 'Factura de Venta', false),
      doc('3', 'Impronta', true),
    ]);

    pintar();

    const obligatorios = await screen.findByRole('region', { name: 'Documentos obligatorios' });
    expect(within(obligatorios).getByText('Certificado CEPD')).toBeInTheDocument();
    expect(within(obligatorios).getByText('Impronta')).toBeInTheDocument();
    expect(within(obligatorios).queryByText('Factura de Venta')).not.toBeInTheDocument();

    const opcionales = screen.getByRole('region', { name: 'Documentos opcionales' });
    expect(within(opcionales).getByText('Factura de Venta')).toBeInTheDocument();
  });

  it('no pinta un bloque vacío cuando el trámite no tiene opcionales', async () => {
    mocks.fetchDocumentRequirementsPreview.mockResolvedValue([
      doc('1', 'Certificado CEPD', true),
    ]);

    pintar();

    await screen.findByRole('region', { name: 'Documentos obligatorios' });
    expect(screen.queryByRole('region', { name: 'Documentos opcionales' })).not.toBeInTheDocument();
  });

  it('conserva la descripción del catálogo, que es lo que desambigua nombres parecidos', async () => {
    mocks.fetchDocumentRequirementsPreview.mockResolvedValue([
      doc('1', 'Certificado CEPD', true, 'Expedido por el organismo de tránsito'),
    ]);

    pintar();

    expect(await screen.findByText('Expedido por el organismo de tránsito')).toBeInTheDocument();
  });

  it('el aviso de formatos sale de lo que la carga acepta de verdad', async () => {
    // La maqueta decía «PDF, JPG o PNG» y la carga acepta además WEBP: escrito a mano, el aviso
    // habría desanimado a subir un archivo válido. Se deriva de ALLOWED_MIME.
    mocks.fetchDocumentRequirementsPreview.mockResolvedValue([doc('1', 'Impronta', true)]);

    pintar();

    expect(
      await screen.findByText(/legibles y en formato PDF, JPG, PNG o WEBP\./),
    ).toBeInTheDocument();
  });

  it('cierra desde el pie sin chocar con el nombre de la X', async () => {
    mocks.fetchDocumentRequirementsPreview.mockResolvedValue([doc('1', 'Impronta', true)]);
    const onOpenChange = vi.fn();
    const user = userEvent.setup();

    render(
      <ProcedureDocsPreviewInformativo
        procedureTypeCode="MATRICULA_NUEVA"
        open
        onOpenChange={onOpenChange}
      />,
    );

    // «Entendido», no «Cerrar»: la X del modal ya se llama así y dos controles con el mismo nombre
    // accesible en un mismo diálogo son indistinguibles para quien navega por nombre.
    await user.click(await screen.findByRole('button', { name: 'Entendido' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('un trámite sin documentos configurados lo dice, no se queda en blanco', async () => {
    mocks.fetchDocumentRequirementsPreview.mockResolvedValue([]);

    pintar();

    expect(
      await screen.findByText('No hay documentos configurados para este trámite.'),
    ).toBeInTheDocument();
  });
});

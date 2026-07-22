import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DocumentRequirementsStep } from '../DocumentRequirementsStep';

// FEATURE-08 / HU-FE-03 (CFD-06) — paso Documentos del wizard SuperAdmin. Los documentos se ELIGEN
// del catálogo (fetchDocumentTypes), no se escriben a mano.

const mocks = vi.hoisted(() => ({
  updateConformationProfile: vi.fn(),
  fetchDocumentTypes: vi.fn(),
}));
vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: { updateConformationProfile: mocks.updateConformationProfile },
}));
vi.mock('@/lib/api/admin-document-types', () => ({
  fetchDocumentTypes: () => mocks.fetchDocumentTypes(),
}));

const catalog = {
  data: [
    { id: 'd1', codigo: 'CEDULA', nombre: 'Cédula', estado: 'activo' },
    { id: 'd2', codigo: 'PROMESA', nombre: 'Promesa de compraventa', estado: 'activo' },
    { id: 'd3', codigo: 'INACTIVO', nombre: 'Viejo', estado: 'inactivo' },
  ],
  totalCount: 3,
  page: 1,
  pageSize: 200,
};

describe('DocumentRequirementsStep', () => {
  beforeEach(() => {
    mocks.updateConformationProfile.mockReset().mockResolvedValue({ documentRequirements: [] });
    mocks.fetchDocumentTypes.mockReset().mockResolvedValue(catalog);
  });

  it('solo ofrece documentos activos del catálogo (no texto libre)', async () => {
    render(<DocumentRequirementsStep procedureTypeId="pt-1" />);
    // Espera a que cargue el catálogo.
    await screen.findByRole('option', { name: /cédula \(CEDULA\)/i });
    expect(screen.getByRole('option', { name: /promesa de compraventa \(PROMESA\)/i })).toBeInTheDocument();
    // El inactivo NO se ofrece.
    expect(screen.queryByRole('option', { name: /viejo/i })).not.toBeInTheDocument();
  });

  it('permite elegir un documento del catálogo (AC-01)', async () => {
    const user = userEvent.setup();
    render(<DocumentRequirementsStep procedureTypeId="pt-1" />);
    await screen.findByRole('option', { name: /cédula \(CEDULA\)/i });

    await user.selectOptions(screen.getByLabelText('Documento del catálogo'), 'CEDULA');
    await user.click(screen.getByRole('button', { name: /^agregar$/i }));

    expect(screen.getByTestId('doc-CEDULA')).toBeInTheDocument();
  });

  it('guarda los documentRequirements (obligatorio + buzón)', async () => {
    const user = userEvent.setup();
    render(<DocumentRequirementsStep procedureTypeId="pt-2" />);
    await screen.findByRole('option', { name: /promesa de compraventa \(PROMESA\)/i });

    await user.selectOptions(screen.getByLabelText('Documento del catálogo'), 'PROMESA');
    await user.click(screen.getByRole('button', { name: /^agregar$/i }));
    await user.click(screen.getByLabelText('PROMESA buzón'));
    await user.click(screen.getByRole('button', { name: /guardar y continuar/i }));

    await waitFor(() => {
      expect(mocks.updateConformationProfile).toHaveBeenCalledWith('pt-2', {
        documentRequirements: [{ documentTypeCode: 'PROMESA', isRequired: true, isDummy: true }],
      });
    });
  });
});

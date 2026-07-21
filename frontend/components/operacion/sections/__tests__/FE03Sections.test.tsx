import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
// AC-07: cada sección se importa desde su propio path.
import { DocumentChecklistSection } from '../DocumentChecklistSection';
import { CommercialSection } from '../CommercialSection';
import { BiometricSection } from '../BiometricSection';
import { SignatureFurSection } from '../SignatureFurSection';

// FEATURE-08 / HU-FE-03 (CFD-06/07) — secciones del registry.

describe('DocumentChecklistSection (AC-02)', () => {
  it('marca los documentos is_dummy con indicador diferenciado', () => {
    render(
      <DocumentChecklistSection
        requirements={[
          { documentTypeCode: 'CEDULA', isRequired: true, isDummy: false },
          { documentTypeCode: 'PROMESA', isRequired: true, isDummy: true },
        ]}
        uploadedCodes={['CEDULA']}
      />,
    );
    expect(screen.getByTestId('checklist-CEDULA')).toHaveAttribute('data-dummy', 'false');
    const dummy = screen.getByTestId('checklist-PROMESA');
    expect(dummy).toHaveAttribute('data-dummy', 'true');
    expect(dummy).toHaveTextContent(/buzón/i);
  });
});

describe('CommercialSection (AC-03)', () => {
  it('muestra el campo valor de venta y la fuente cuando se requiere', () => {
    render(<CommercialSection requiresCommercialValue commercialValueSource="FASECOLDA" />);
    expect(screen.getByLabelText('Valor de venta')).toBeInTheDocument();
    expect(screen.getByText(/FASECOLDA/)).toBeInTheDocument();
  });

  it('oculta el campo cuando no se requiere', () => {
    render(<CommercialSection requiresCommercialValue={false} />);
    expect(screen.queryByLabelText('Valor de venta')).not.toBeInTheDocument();
  });
});

describe('BiometricSection (AC-04)', () => {
  it('renderiza el estado por actor', () => {
    render(<BiometricSection actors={['OWNER', 'BUYER']} approvedActors={['OWNER']} />);
    expect(screen.getByTestId('biometric-OWNER')).toHaveTextContent(/aprobada/i);
    expect(screen.getByTestId('biometric-BUYER')).toHaveTextContent(/pendiente/i);
  });
});

describe('SignatureFurSection (AC-05)', () => {
  it('permite generar el FUR cuando no está generado', async () => {
    const user = userEvent.setup();
    const onGenerate = vi.fn();
    render(<SignatureFurSection furGenerated={false} onGenerate={onGenerate} />);
    await user.click(screen.getByRole('button', { name: /generar fur/i }));
    expect(onGenerate).toHaveBeenCalledTimes(1);
  });

  it('muestra FUR generado cuando ya existe', () => {
    render(<SignatureFurSection furGenerated />);
    expect(screen.getByText(/fur generado/i)).toBeInTheDocument();
  });
});

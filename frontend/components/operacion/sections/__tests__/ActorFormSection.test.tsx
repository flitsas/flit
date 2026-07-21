import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ActorFormSection } from '../ActorFormSection';
import type { ConformationRuleProfile } from '@/lib/api/types/procedure-parametrization-f08';

// FEATURE-08 / HU-FE-02 (CFD-05) — ActorFormSection renderiza N actores incl. LESSEE.

const rules: ConformationRuleProfile[] = [
  { entityCode: 'VEHICLE', validationProfile: {} },
  { entityCode: 'OWNER', validationProfile: { allowsNaturalPerson: true, requiresRunt: true } },
  {
    entityCode: 'BUYER',
    validationProfile: { allowsNaturalPerson: true, allowsJuridicalPerson: true, allowsMultiple: true },
  },
  { entityCode: 'LESSEE', validationProfile: { allowsJuridicalPerson: true, requiresRunt: true } },
];

describe('ActorFormSection', () => {
  it('renderiza un actor por regla excluyendo VEHICLE, incluyendo LESSEE (AC-04/05)', () => {
    render(<ActorFormSection conformationRules={rules} />);

    expect(screen.getByTestId('actor-OWNER')).toBeInTheDocument();
    expect(screen.getByTestId('actor-BUYER')).toBeInTheDocument();
    expect(screen.getByTestId('actor-LESSEE')).toBeInTheDocument();
    expect(screen.queryByTestId('actor-VEHICLE')).not.toBeInTheDocument();
    expect(screen.getByText('Locatario')).toBeInTheDocument();
  });

  it('muestra el toggle de persona solo cuando permite natural y jurídica (BUYER)', () => {
    render(<ActorFormSection conformationRules={rules} />);
    expect(screen.getByLabelText('Comprador persona natural')).toBeInTheDocument();
    expect(screen.getByLabelText('Comprador persona jurídica')).toBeInTheDocument();
    expect(screen.queryByLabelText('Propietario persona jurídica')).not.toBeInTheDocument();
  });

  it('allowsMultiple=true habilita botón Agregar comprador (AC-06)', () => {
    render(<ActorFormSection conformationRules={rules} />);
    expect(screen.getByRole('button', { name: /agregar comprador/i })).toBeInTheDocument();
    // OWNER no es múltiple → sin botón.
    expect(screen.queryByRole('button', { name: /agregar propietario/i })).not.toBeInTheDocument();
  });

  it('actor PJ (LESSEE) muestra NIT y representante legal (AC-07)', () => {
    render(<ActorFormSection conformationRules={rules} />);
    expect(screen.getByLabelText('NIT de Locatario')).toBeInTheDocument();
    expect(screen.getByLabelText('Representante legal de Locatario')).toBeInTheDocument();
    expect(screen.getByText('Razón social')).toBeInTheDocument();
  });

  it('propaga onChange al escribir el NIT de LESSEE', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ActorFormSection conformationRules={rules} onChange={onChange} />);

    await user.type(screen.getByLabelText('NIT de Locatario'), '9');

    expect(onChange).toHaveBeenLastCalledWith('LESSEE', { documentNumber: '9' });
  });
});

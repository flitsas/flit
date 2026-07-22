import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Step3Aristas } from '../Step3Aristas';
import type { ConformationRuleItem } from '@/lib/api/types/procedure-parametrization';

// FEATURE-08 / CFD-05 — el paso Aristas captura, por actor activo, las banderas del validation_profile
// (PN/PJ, múltiples, RUNT). VEHICLE no es actor y no muestra banderas.

const rules: ConformationRuleItem[] = [
  { procedureEntityCode: 'VEHICLE', isActive: true, sortOrder: 1 },
  { procedureEntityCode: 'BUYER', isActive: true, sortOrder: 2, validationProfile: { allowsMultiple: true } },
  { procedureEntityCode: 'OWNER', isActive: false, sortOrder: 3 },
];

describe('Step3Aristas (CFD-05)', () => {
  it('muestra banderas por-actor solo para actores activos (no VEHICLE, no inactivos)', () => {
    render(<Step3Aristas rules={rules} onToggle={vi.fn()} onProfileChange={vi.fn()} />);

    // BUYER activo → sus banderas visibles, con allowsMultiple marcado.
    const buyerMultiple = screen.getByRole('checkbox', { name: /comprador — permite múltiples/i });
    expect(buyerMultiple).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /comprador — requiere runt/i })).toBeInTheDocument();

    // VEHICLE no tiene banderas de actor.
    expect(screen.queryByRole('checkbox', { name: /vehículo — permite múltiples/i })).not.toBeInTheDocument();
    // OWNER inactivo → sin banderas.
    expect(screen.queryByRole('checkbox', { name: /propietario — requiere runt/i })).not.toBeInTheDocument();
  });

  it('al marcar una bandera emite onProfileChange con el patch', async () => {
    const onProfileChange = vi.fn();
    const user = userEvent.setup();
    render(<Step3Aristas rules={rules} onToggle={vi.fn()} onProfileChange={onProfileChange} />);

    await user.click(screen.getByRole('checkbox', { name: /comprador — requiere runt/i }));

    expect(onProfileChange).toHaveBeenCalledWith('BUYER', { requiresRunt: true });
  });
});

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { VehicleQuerySection } from '../VehicleQuerySection';

// FEATURE-08 / HU-FE-01 (CFD-02) — VehicleQuerySection adapta los campos al entryMode.

describe('VehicleQuerySection', () => {
  it('con entryMode=VIN muestra solo el campo VIN', () => {
    render(<VehicleQuerySection entryMode="VIN" />);
    expect(screen.getByLabelText('VIN del vehículo')).toBeInTheDocument();
    expect(screen.queryByLabelText('Placa del vehículo')).not.toBeInTheDocument();
  });

  it('con entryMode=PLATE muestra solo el campo Placa', () => {
    render(<VehicleQuerySection entryMode="PLATE" />);
    expect(screen.getByLabelText('Placa del vehículo')).toBeInTheDocument();
    expect(screen.queryByLabelText('VIN del vehículo')).not.toBeInTheDocument();
  });

  it('con entryMode=BOTH muestra placa y VIN', () => {
    render(<VehicleQuerySection entryMode="BOTH" />);
    expect(screen.getByLabelText('Placa del vehículo')).toBeInTheDocument();
    expect(screen.getByLabelText('VIN del vehículo')).toBeInTheDocument();
  });

  it('normaliza a mayúsculas y propaga onVinChange', async () => {
    const user = userEvent.setup();
    const onVinChange = vi.fn();
    render(<VehicleQuerySection entryMode="VIN" onVinChange={onVinChange} />);

    // Input controlado con value fijo: cada tecla emite el carácter normalizado a mayúscula.
    await user.type(screen.getByLabelText('VIN del vehículo'), 'a');

    expect(onVinChange).toHaveBeenLastCalledWith('A');
  });

  it('dispara onConsult al pulsar Consultar', async () => {
    const user = userEvent.setup();
    const onConsult = vi.fn();
    render(<VehicleQuerySection entryMode="PLATE" onConsult={onConsult} />);

    await user.click(screen.getByRole('button', { name: /consultar/i }));

    expect(onConsult).toHaveBeenCalledTimes(1);
  });
});

// HU #12127 — validación de campos dinámicos de "Otros Trámites" (DynamicFieldRenderer).
import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import {
  DynamicFieldRenderer,
  resolveFieldFormat,
  validateDynamicField,
} from '../DynamicFieldRenderer';
import type { FormFieldItem } from '@/lib/api/types/procedure-parametrization';

function makeField(overrides: Partial<FormFieldItem> = {}): FormFieldItem {
  return {
    fieldKey: 'campo_generico',
    label: 'Campo genérico',
    fieldType: 'text',
    isRequired: false,
    isLocked: false,
    lockReason: null,
    consultationTemplateId: null,
    ...overrides,
  };
}

describe('resolveFieldFormat', () => {
  it('infiere el formato por fieldKey cuando validationSchema no trae `format`', () => {
    expect(resolveFieldFormat(makeField({ fieldKey: 'placa_remolque' }))).toBe('plate');
    expect(resolveFieldFormat(makeField({ fieldKey: 'nit_acreedor' }))).toBe('nit');
    expect(resolveFieldFormat(makeField({ fieldKey: 'numero_documento' }))).toBe('cedula');
    expect(resolveFieldFormat(makeField({ fieldKey: 'vin_motor' }))).toBe('vin');
    expect(resolveFieldFormat(makeField({ fieldKey: 'observaciones' }))).toBeNull();
  });

  it('`validationSchema.format` explícito tiene prioridad sobre la inferencia por fieldKey', () => {
    const field = makeField({ fieldKey: 'observaciones', validationSchema: { format: 'plate' } });
    expect(resolveFieldFormat(field)).toBe('plate');
  });

  it('tolera `validationSchema` como string JSON', () => {
    const field = makeField({ fieldKey: 'observaciones', validationSchema: '{"format":"nit"}' });
    expect(resolveFieldFormat(field)).toBe('nit');
  });
});

describe('validateDynamicField — AC1 (formato) y AC2 (obligatoriedad)', () => {
  it('AC2 — campo obligatorio vacío bloquea con mensaje inline', () => {
    const field = makeField({ isRequired: true, label: 'Placa' });
    expect(validateDynamicField(field, '')).toBe('Placa es obligatorio.');
    expect(validateDynamicField(field, null)).toBe('Placa es obligatorio.');
  });

  it('AC2 — campo opcional vacío no bloquea', () => {
    const field = makeField({ isRequired: false });
    expect(validateDynamicField(field, '')).toBeNull();
  });

  it('AC1 — placa con formato inválido bloquea (reutiliza validatePlate de fieldRules)', () => {
    const field = makeField({ fieldKey: 'placa', isRequired: true });
    expect(validateDynamicField(field, 'XYZ')).toMatch(/Placa inválida/);
  });

  it('AC1 — placa válida no bloquea', () => {
    const field = makeField({ fieldKey: 'placa', isRequired: true });
    expect(validateDynamicField(field, 'ABC123')).toBeNull();
  });

  it('AC1 — NIT con letras bloquea (reutiliza validateDocNumber de fieldRules)', () => {
    const field = makeField({ fieldKey: 'nit', isRequired: true });
    expect(validateDynamicField(field, 'ABC123')).toMatch(/solo admite dígitos/);
  });

  it('AC1 — cédula con letras bloquea', () => {
    const field = makeField({ fieldKey: 'numero_documento', isRequired: true });
    expect(validateDynamicField(field, '10a20')).toMatch(/solo admite dígitos/);
  });

  it('checkbox obligatorio sin marcar bloquea', () => {
    const field = makeField({ fieldType: 'checkbox', isRequired: true, label: 'Acepto' });
    expect(validateDynamicField(field, false)).toBe('Acepto es obligatorio.');
    expect(validateDynamicField(field, true)).toBeNull();
  });
});

describe('<DynamicFieldRenderer /> — AC1/AC2 mensaje inline + accesibilidad', () => {
  it('AC2 — muestra error inline tras blur en un campo obligatorio vacío, con aria-invalid/aria-describedby', () => {
    const field = makeField({ isRequired: true, label: 'Placa', fieldKey: 'placa' });
    render(<DynamicFieldRenderer field={field} value="" onChange={() => {}} />);
    const input = screen.getByRole('textbox');
    expect(input).not.toHaveAttribute('aria-invalid', 'true');
    fireEvent.blur(input);
    expect(screen.getByRole('alert')).toHaveTextContent('Placa es obligatorio.');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input.getAttribute('aria-describedby')).toContain('-err');
  });

  it('AC1 — muestra el mensaje de formato de placa tras blur con valor inválido', () => {
    const field = makeField({ isRequired: true, label: 'Placa', fieldKey: 'placa' });
    render(<DynamicFieldRenderer field={field} value="XYZ" onChange={() => {}} />);
    fireEvent.blur(screen.getByRole('textbox'));
    expect(screen.getByRole('alert')).toHaveTextContent(/Placa inválida/);
  });

  it('`showErrors` del padre revela el error sin necesidad de blur (intento de avanzar de paso)', () => {
    const field = makeField({ isRequired: true, label: 'Placa' });
    render(<DynamicFieldRenderer field={field} value="" onChange={() => {}} showErrors />);
    expect(screen.getByRole('alert')).toHaveTextContent('Placa es obligatorio.');
  });

  it('AC5 — un campo válido no muestra error aunque se fuerce showErrors', () => {
    const field = makeField({ isRequired: true, label: 'Placa', fieldKey: 'placa' });
    render(<DynamicFieldRenderer field={field} value="ABC123" onChange={() => {}} showErrors />);
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('AC4 — un `serverErrorCode` se traduce a mensaje descriptivo inline, con prioridad sobre el cliente', () => {
    const field = makeField({ isRequired: true, label: 'Placa', fieldKey: 'placa' });
    render(
      <DynamicFieldRenderer field={field} value="ABC123" onChange={() => {}} serverErrorCode="VIN_PLATE_RULE" />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/VIN o la placa no cumple el formato/);
  });

  it('AC4 — MISSING_REQUIRED_FIELD y NIT_PERSON_TYPE también se traducen', () => {
    const field = makeField({ fieldKey: 'nit' });
    const { rerender } = render(
      <DynamicFieldRenderer field={field} value="900123456" onChange={() => {}} serverErrorCode="MISSING_REQUIRED_FIELD" />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Este campo es obligatorio.');
    rerender(
      <DynamicFieldRenderer field={field} value="900123456" onChange={() => {}} serverErrorCode="NIT_PERSON_TYPE" />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent(/no coincide con el tipo de persona/);
  });

  it('notifica la validez al padre vía onValidityChange (gate de avance del paso, AC2/AC5)', () => {
    const onValidityChange = vi.fn();
    const field = makeField({ isRequired: true, fieldKey: 'placa', label: 'Placa' });
    const { rerender } = render(
      <DynamicFieldRenderer field={field} value="" onChange={() => {}} onValidityChange={onValidityChange} />,
    );
    expect(onValidityChange).toHaveBeenLastCalledWith('placa', false);
    rerender(
      <DynamicFieldRenderer field={field} value="ABC123" onChange={() => {}} onValidityChange={onValidityChange} />,
    );
    expect(onValidityChange).toHaveBeenLastCalledWith('placa', true);
  });
});

describe('<DynamicFieldRenderer /> — AC3 (regresión) campo numérico bloquea letras on-change', () => {
  it('no deja pasar caracteres alfabéticos ni especiales al escribir', () => {
    const handleChange = vi.fn();
    const field = makeField({ fieldType: 'number', fieldKey: 'numero_placas' });
    render(<DynamicFieldRenderer field={field} value="" onChange={handleChange} />);
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'a1b2c3' } });
    expect(handleChange).toHaveBeenCalledWith('123');
  });

  it('deja pasar solo dígitos sin alterarlos', () => {
    const handleChange = vi.fn();
    const field = makeField({ fieldType: 'number', fieldKey: 'numero_placas' });
    render(<DynamicFieldRenderer field={field} value="" onChange={handleChange} />);
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '4590' } });
    expect(handleChange).toHaveBeenCalledWith('4590');
  });
});

'use client';

import { Lock } from 'lucide-react';
import type { FormFieldItem } from '@/lib/api/types/procedure-parametrization';
import { digitsOnly } from '@/lib/format/currency';
import { WIZARD_INPUT } from './wizard-field-styles';

interface SelectOption {
  value: string;
  label: string;
}

/** Parsea `options` (JSON string o ya-objeto) a una lista normalizada. */
function parseOptions(options: FormFieldItem['options']): SelectOption[] {
  if (options == null) return [];
  let raw: unknown = options;
  if (typeof options === 'string') {
    if (!options.trim()) return [];
    try {
      raw = JSON.parse(options);
    } catch {
      return [];
    }
  }
  const arr = Array.isArray(raw)
    ? raw
    : raw && typeof raw === 'object' && 'options' in raw
      ? (raw as { options: unknown }).options
      : [];
  if (!Array.isArray(arr)) return [];
  return arr.map((opt) => {
    if (typeof opt === 'string') return { value: opt, label: opt };
    const o = opt as Record<string, unknown>;
    const value = String(o.value ?? o.id ?? o.key ?? '');
    return { value, label: String(o.label ?? o.name ?? value) };
  });
}

interface Props {
  field: FormFieldItem;
  value: unknown;
  onChange: (value: unknown) => void;
}

const INPUT_BASE = WIZARD_INPUT;

export function DynamicFieldRenderer({ field, value, onChange }: Props) {
  const { fieldType, isRequired, isLocked, lockReason } = field;
  const fieldId = `field-${field.id ?? field.fieldKey}`;
  const disabled = isLocked;
  const describedBy = isLocked ? `${fieldId}-lock` : undefined;

  const control = (() => {
    switch (fieldType) {
      case 'checkbox':
        return (
          <label className="flex items-center gap-2 text-xs">
            <input
              id={fieldId}
              type="checkbox"
              checked={Boolean(value)}
              disabled={disabled}
              required={isRequired}
              aria-describedby={describedBy}
              onChange={(e) => onChange(e.target.checked)}
              className="h-4 w-4 accent-[#557EFF] disabled:opacity-60"
            />
            <span>{field.label}</span>
          </label>
        );
      case 'select':
        return (
          <select
            id={fieldId}
            value={(value as string) ?? ''}
            disabled={disabled}
            required={isRequired}
            aria-describedby={describedBy}
            onChange={(e) => onChange(e.target.value)}
            className={INPUT_BASE}
          >
            <option value="" disabled>
              Selecciona una opción
            </option>
            {parseOptions(field.options).map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        );
      case 'radio':
        return (
          <div className="flex flex-wrap gap-3" role="radiogroup" aria-label={field.label}>
            {parseOptions(field.options).map((opt) => (
              <label key={opt.value} className="flex items-center gap-2 text-xs">
                <input
                  type="radio"
                  name={fieldId}
                  value={opt.value}
                  checked={value === opt.value}
                  disabled={disabled}
                  required={isRequired}
                  onChange={() => onChange(opt.value)}
                  className="accent-[#557EFF]"
                />
                {opt.label}
              </label>
            ))}
          </div>
        );
      case 'number':
        // text + digitsOnly: type=number aún permite e/E/+/- en varios navegadores (HU #11072).
        return (
          <input
            id={fieldId}
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            value={value == null ? '' : String(value)}
            disabled={disabled}
            required={isRequired}
            aria-describedby={describedBy}
            onChange={(e) => onChange(digitsOnly(e.target.value))}
            className={INPUT_BASE}
          />
        );
      case 'date':
        return (
          <input
            id={fieldId}
            type="date"
            value={(value as string) ?? ''}
            disabled={disabled}
            required={isRequired}
            aria-describedby={describedBy}
            onChange={(e) => onChange(e.target.value)}
            className={INPUT_BASE}
          />
        );
      case 'text':
      default:
        return (
          <input
            id={fieldId}
            type="text"
            value={(value as string) ?? ''}
            disabled={disabled}
            required={isRequired}
            aria-describedby={describedBy}
            onChange={(e) => onChange(e.target.value)}
            className={INPUT_BASE}
          />
        );
    }
  })();

  return (
    <div>
      {fieldType !== 'checkbox' && (
        <label htmlFor={fieldId} className="text-xs font-semibold mb-1.5 flex items-center gap-1.5">
          {field.label}
          {isRequired && (
            <span style={{ color: '#FF4E00' }} aria-label="obligatorio">
              *
            </span>
          )}
          {isLocked && (
            <span
              className="inline-flex items-center gap-1 text-xs font-medium px-1.5 py-0.5 rounded"
              style={{ background: 'rgba(249,172,0,0.15)', color: 'var(--badge-warning-fg)' }}
              title={lockReason ?? 'Campo bloqueado'}
            >
              <Lock className="h-3 w-3" aria-hidden="true" /> Bloqueado
            </span>
          )}
        </label>
      )}
      {control}
      {isLocked && lockReason && (
        <p id={`${fieldId}-lock`} className="text-xs opacity-60 mt-1">
          {lockReason}
        </p>
      )}
    </div>
  );
}

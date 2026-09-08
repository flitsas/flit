'use client';

import { useEffect, useState } from 'react';
import { Lock } from 'lucide-react';
import type { FormFieldItem, ValidationErrorCode } from '@/lib/api/types/procedure-parametrization';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { digitsOnly } from '@/lib/format/currency';
import {
  sanitizeDocNumber,
  sanitizePlate,
  sanitizeVin,
  validateDocNumber,
  validatePlate,
  validateVin,
  validationErrorCodeMessage,
} from '@/lib/validation/fieldRules';
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

/**
 * HU #12127 — parsea `FormFieldItem.validationSchema` a un objeto plano.
 *
 * El backend lo persiste como JSONB opaco (`{}` por defecto en la DDL, `Flit.Tramites.Domain
 * .Entities.FormField.ValidationSchema`): ningún productor (Step5Campos, el editor de plantillas)
 * lo llena todavía, así que hoy casi siempre llega `{}`/`null`. Se deja el parseo listo para cuando
 * exista un editor que lo escriba.
 */
function parseValidationSchema(schema: FormFieldItem['validationSchema']): Record<string, unknown> | null {
  if (schema == null) return null;
  if (typeof schema === 'string') {
    if (!schema.trim()) return null;
    try {
      const parsed = JSON.parse(schema);
      return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
    } catch {
      return null;
    }
  }
  return schema;
}

type FieldFormat = 'plate' | 'vin' | 'nit' | 'cedula';
const KNOWN_FORMATS: readonly FieldFormat[] = ['plate', 'vin', 'nit', 'cedula'];

/**
 * Formato de contenido esperado por un campo de texto, para reutilizar los validadores maduros de
 * `fieldRules.ts` (espejo del backend) en vez de reimplementar los patrones de placa/NIT/cédula/VIN.
 *
 * Prioridad:
 *  1) `validationSchema.format` explícito — convención de este componente (ver
 *     `parseValidationSchema`); es la vía "correcta" cuando exista un productor.
 *  2) Inferencia por `fieldKey` — heurística de continuidad mientras el punto 1 no tiene productor:
 *     los tipos de "Otros Trámites" de hoy nombran sus campos de forma descriptiva
 *     (`placa`, `nit`, `numero_documento`, `vin`), así que el field key ya es la señal disponible.
 */
export function resolveFieldFormat(field: FormFieldItem): FieldFormat | null {
  const schema = parseValidationSchema(field.validationSchema);
  const explicit = schema && typeof schema.format === 'string' ? schema.format : null;
  if (explicit && (KNOWN_FORMATS as readonly string[]).includes(explicit)) {
    return explicit as FieldFormat;
  }
  const key = field.fieldKey.toLowerCase();
  if (key.includes('placa') || key.includes('plate')) return 'plate';
  if (key.includes('vin')) return 'vin';
  if (key.includes('nit')) return 'nit';
  if (key.includes('cedula') || key.includes('cédula') || key.includes('documento')) return 'cedula';
  return null;
}

/** ¿El valor cuenta como "vacío" para efectos de la regla de obligatoriedad (AC2)? */
function isEmptyValue(field: FormFieldItem, value: unknown): boolean {
  if (field.fieldType === 'checkbox') return value !== true;
  if (value == null) return true;
  if (typeof value === 'string') return value.trim() === '';
  return false;
}

/**
 * Validación de cliente de un campo dinámico (AC1/AC2), pura y testeable de forma aislada:
 *  - obligatoriedad (cualquier `fieldType`);
 *  - formato placa/NIT/cédula/VIN en campos de texto (AC1), reutilizando `fieldRules.ts`.
 * Los campos `number` ya sanean caracteres inválidos on-change (AC3) y no necesitan regla de
 * formato aquí; solo heredan la de obligatoriedad.
 */
export function validateDynamicField(field: FormFieldItem, value: unknown): string | null {
  if (field.isRequired && isEmptyValue(field, value)) {
    return `${field.label} es obligatorio.`;
  }
  if (isEmptyValue(field, value)) return null;
  if (field.fieldType === 'text') {
    const format = resolveFieldFormat(field);
    const strValue = String(value);
    if (format === 'plate') return validatePlate(strValue);
    if (format === 'vin') return validateVin(strValue);
    if (format === 'nit' || format === 'cedula') return validateDocNumber(strValue, 'CC');
  }
  return null;
}

interface Props {
  field: FormFieldItem;
  value: unknown;
  onChange: (value: unknown) => void;
  /**
   * El padre (paso del wizard) lo activa al intentar avanzar de paso, para revelar el error de un
   * campo obligatorio que el gestor nunca llegó a tocar (AC2). Sin esto, el error de un campo
   * intacto solo aparecería tras un blur manual.
   */
  showErrors?: boolean;
  /**
   * AC4 — código de error devuelto por el backend para ESTE campo (`VIN_PLATE_RULE`,
   * `NIT_PERSON_TYPE`, `MISSING_REQUIRED_FIELD`, …). Tiene prioridad sobre la validación de
   * cliente: es la fuente de verdad final una vez que el backend respondió.
   */
  serverErrorCode?: ValidationErrorCode | null;
  /**
   * Notifica al padre la validez vigente del campo (con la MISMA regla que bloquea el mensaje
   * visible, sin depender de `touched`/`showErrors`) para que agregue el gate de "Continuar" del
   * paso — mismo patrón que `onCamposRequeridosGateChange` / `onPrendaDocumentGateChange` en
   * `TramiteWizard`. Se dispara en cada cambio relevante, incluido el montaje.
   */
  onValidityChange?: (fieldKey: string, isValid: boolean) => void;
}

const INPUT_BASE = WIZARD_INPUT;

export function DynamicFieldRenderer({ field, value, onChange, showErrors, serverErrorCode, onValidityChange }: Props) {
  const { fieldType, isRequired, isLocked, lockReason } = field;
  const fieldId = `field-${field.id ?? field.fieldKey}`;
  const errorId = `${fieldId}-err`;
  const lockId = `${fieldId}-lock`;
  const disabled = isLocked;

  const [touched, setTouched] = useState(false);
  const markTouched = () => setTouched(true);

  const clientError = validateDynamicField(field, value);
  const isValid = !serverErrorCode && !clientError;
  const displayError = serverErrorCode ? validationErrorCodeMessage(serverErrorCode) : touched || showErrors ? clientError : null;

  // AC4/AC5 — la validez real (no la visible) es la que gatea el avance del paso; se recalcula en
  // cada cambio de valor/serverErrorCode y también al montar, para que el padre conozca el estado
  // inicial sin esperar una interacción.
  useEffect(() => {
    onValidityChange?.(field.fieldKey, isValid);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [field.fieldKey, isValid]);

  const describedBy = [isLocked ? lockId : null, displayError ? errorId : null].filter(Boolean).join(' ') || undefined;
  const ariaInvalid = displayError ? true : undefined;

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
              aria-invalid={ariaInvalid}
              aria-describedby={describedBy}
              onChange={(e) => onChange(e.target.checked)}
              onBlur={markTouched}
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
            aria-invalid={ariaInvalid}
            aria-describedby={describedBy}
            onChange={(e) => onChange(e.target.value)}
            onBlur={markTouched}
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
          <div className="flex flex-wrap gap-3" role="radiogroup" aria-label={field.label} aria-invalid={ariaInvalid} aria-describedby={describedBy}>
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
                  onBlur={markTouched}
                  className="accent-[#557EFF]"
                />
                {opt.label}
              </label>
            ))}
          </div>
        );
      case 'number':
        // text + digitsOnly: type=number aún permite e/E/+/- en varios navegadores (HU #11072).
        // AC3 (regresión): sigue bloqueando la entrada on-change, sin excepción de esta HU.
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
            aria-invalid={ariaInvalid}
            aria-describedby={describedBy}
            onChange={(e) => onChange(digitsOnly(e.target.value))}
            onBlur={markTouched}
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
            aria-invalid={ariaInvalid}
            aria-describedby={describedBy}
            onChange={(e) => onChange(e.target.value)}
            onBlur={markTouched}
            className={INPUT_BASE}
          />
        );
      case 'text':
      default: {
        const format = resolveFieldFormat(field);
        const sanitize =
          format === 'plate' ? sanitizePlate : format === 'vin' ? sanitizeVin : format === 'nit' || format === 'cedula' ? (v: string) => sanitizeDocNumber(v, 'CC') : null;
        return (
          <input
            id={fieldId}
            type="text"
            value={(value as string) ?? ''}
            disabled={disabled}
            required={isRequired}
            aria-invalid={ariaInvalid}
            aria-describedby={describedBy}
            onChange={(e) => onChange(sanitize ? sanitize(e.target.value) : e.target.value)}
            onBlur={markTouched}
            className={INPUT_BASE}
          />
        );
      }
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
        <p id={lockId} className="text-xs opacity-60 mt-1">
          {lockReason}
        </p>
      )}
      {displayError && (
        <InlineAlert id={errorId} tone="warning" compact className="mt-1">
          {displayError}
        </InlineAlert>
      )}
    </div>
  );
}

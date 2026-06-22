import type { ValidationResult } from '@/lib/api/types/procedure-parametrization';

interface Step7Props {
  validationResult: ValidationResult | null;
  loading: boolean;
  error: string | null;
  onValidate: () => void;
}

const ERROR_STEP_MAP: Record<string, string> = {
  MISSING_CONFORMATION: 'Aristas (Paso 3)',
  MISSING_REQUIRED_FIELD: 'Campos (Paso 5)',
  LOCKED_FIELD_REMOVED: 'Campos (Paso 5)',
  VIN_PLATE_RULE: 'Aristas / Campos (Pasos 3–5)',
  NIT_PERSON_TYPE: 'Campos (Paso 5)',
  INCOMPLETE_CONSULTATION_FIELDS: 'Campos (Paso 5)',
};

export function Step7Validar({ validationResult, loading, error, onValidate }: Step7Props) {
  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Validar parametrización</h2>
        <p className="text-xs opacity-60">
          Ejecuta las reglas de negocio antes de guardar o publicar.
        </p>
      </div>

      <button
        onClick={onValidate}
        disabled={loading}
        className="px-5 py-2.5 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
        style={{ background: '#557EFF' }}
        aria-label="Ejecutar validación de parametrización"
      >
        {loading ? 'Validando…' : 'Ejecutar validación'}
      </button>

      {error && (
        <div
          className="rounded-xl p-3 border text-xs"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
        >
          {error}
        </div>
      )}

      {validationResult && (
        <div
          aria-live="polite"
          aria-atomic="true"
          className="space-y-3"
        >
          {validationResult.isValid ? (
            <div
              className="flex items-center gap-3 rounded-xl p-4 border text-xs font-semibold"
              style={{
                borderColor: '#00DBD5',
                background: 'rgba(0,219,213,0.08)',
                color: '#00DBD5',
              }}
              role="status"
            >
              <svg
                className="h-5 w-5 shrink-0"
                fill="none"
                stroke="currentColor"
                strokeWidth={2}
                viewBox="0 0 24 24"
                aria-hidden="true"
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
              </svg>
              Validación exitosa. Puedes guardar o publicar.
            </div>
          ) : (
            <div className="space-y-2">
              <p className="text-xs font-semibold" style={{ color: '#FF4E00' }}>
                {validationResult.errors.length} error(es) encontrado(s):
              </p>
              <ul className="space-y-2">
                {validationResult.errors.map((err, i) => (
                  <li
                    key={i}
                    className="rounded-xl p-3 border text-xs"
                    style={{
                      borderColor: '#FF4E00',
                      background: 'rgba(255,78,0,0.06)',
                    }}
                  >
                    <div className="flex items-start gap-2">
                      <span
                        className="px-1.5 py-0.5 rounded text-[9px] font-bold text-white shrink-0"
                        style={{ background: '#FF4E00' }}
                      >
                        {err.code}
                      </span>
                      <div className="min-w-0">
                        <p className="font-medium">{err.message}</p>
                        {ERROR_STEP_MAP[err.code] && (
                          <p className="opacity-60 mt-0.5">
                            Revisar: {ERROR_STEP_MAP[err.code]}
                          </p>
                        )}
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

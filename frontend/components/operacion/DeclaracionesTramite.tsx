'use client';

import { useEffect, useState } from 'react';
import { Search } from 'lucide-react';
import { isRuesPreviewUnavailable, tramitesClient } from '@/lib/api/tramites-client';
import { shortRuesRazonSocial } from '@/lib/tramites/rues-razon-social';
import type {
  FieldValue,
  VehicleServiceTypeOption,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import { VehicleTransformationsCard } from './VehicleTransformationsCard';
import { WizardCardHeader } from './wizard-atoms';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { WIZARD_BTN, WIZARD_BTN_SOLID, WIZARD_INPUT, WIZARD_SELECT } from './wizard-field-styles';

/** El proveedor RUES respondió y no existe empresa con ese NIT — distinto del fallo transitorio 503. */
const RUES_NO_ENCONTRADO =
  'No se encontró una empresa con ese NIT en el RUES. Verifica el número e inténtalo de nuevo.';

/** 503 del proveedor RUES: fallo transitorio, no es un error del operador — se ofrece reintentar. */
const RUES_NO_DISPONIBLE =
  'El RUES no respondió en este momento. No es un error tuyo: puedes reintentar en unos segundos.';

// B4 (guardián de diseño) — antes duplicaba a mano la clase de campo del wizard (sin `focus:ring`,
// solo cambio de borde). Se usa `WIZARD_INPUT` (`wizard-field-styles.ts`), la única fuente para el
// anillo de foco de 2px que exige el sistema.
const inputClass = WIZARD_INPUT;

/**
 * Declaraciones del paso de requisitos: TIPO DE SERVICIO (casilla 18 del FUR, con la empresa
 * vinculadora de la casilla 19) y TRANSFORMACIONES del trámite.
 * El leasing no se declara aquí: se elige al crear (matrícula leasing / traspaso unilateral).
 *
 * Las dos vivían en el paso 1. Se mueven aquí porque es donde las pone el repo de diseño
 * (`MatriculaInicial.tsx`, Step 3 «Documentos y requisitos del trámite»): el paso 1 es la consulta
 * del vehículo, y lo que se declara SOBRE ese vehículo —y ajusta el checklist de documentos— es
 * materia de requisitos. Las reglas de negocio no cambian; sí cambia cuándo se guardan: aquí el
 * trámite YA existe, así que cada control hace su PATCH inmediato en vez de esperar a la creación.
 *
 * Efecto lateral bienvenido del traslado: el tipo de servicio ya se puede LEER Y EDITAR en un
 * borrador retomado. Mientras se capturaba en el paso 1 solo existía durante la creación del
 * trámite (`createInstanceFromConsulta`), y un borrador reabierto no volvía a ofrecerlo.
 */
export function DeclaracionesTramite({
  instanceId,
  modalidad,
  onChanged,
  onTipoServicioGateChange,
  hideTransformaciones = false,
  noCardWrapper = false,
}: {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Refresca el wizard: las condiciones declaradas cambian el checklist de documentos. */
  onChanged?: () => void;
  /** Informa al pie si el tipo de servicio está completo (gate de "Continuar", solo matrícula). */
  onTipoServicioGateChange?: (ok: boolean) => void;
  /**
   * Oleada 2 (PDF 20/08) — cuando TramiteWizard mueve VehicleTransformationsCard al paso de
   * documentos después del checklist, ocultar la tarjeta aquí para no duplicarla. Sin esto
   * permanece el comportamiento original (útil en tests unitarios de DeclaracionesTramite).
   */
  hideTransformaciones?: boolean;
  /**
   * Oleada 2 — cuando el componente vive DENTRO de un WizardAccordion que ya actúa como
   * contenedor (borde + fondo), omite el div de tarjeta interno para evitar "card en card".
   */
  noCardWrapper?: boolean;
}) {
  const readOnly = useWizardReadOnly();
  // Tipo de servicio: solo matrícula inicial. En traspaso `vehicle_service` lo hidrata el RUNT como
  // texto libre y no es una elección del gestor, así que no se ofrece el selector.
  const esMatricula = modalidad !== 'traspaso';

  const [fieldValues, setFieldValues] = useState<FieldValue[]>([]);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [tiposServicio, setTiposServicio] = useState<VehicleServiceTypeOption[]>([]);
  const [tiposServicioLoading, setTiposServicioLoading] = useState(() => esMatricula);
  const [tiposServicioError, setTiposServicioError] = useState<string | null>(null);
  const [tipoServicioCode, setTipoServicioCode] = useState('');
  const [empresaVinculadoraNit, setEmpresaVinculadoraNit] = useState('');
  const [empresaVinculadoraRazonSocial, setEmpresaVinculadoraRazonSocial] = useState<string | null>(
    null,
  );
  const [ruesLoading, setRuesLoading] = useState(false);
  const [ruesError, setRuesError] = useState<string | null>(null);
  const [ruesUnavailable, setRuesUnavailable] = useState(false);
  /** La razón social salió del directorio de la compañía, no del RUES: hay que decirlo. */

  /**
   * Carga inicial: instancia y catálogo EN PARALELO, y se hidrata todo junto. El catálogo hace
   * falta para hidratar el selector: `vehicle_service` también lo escribe el RUNT como texto libre
   * (traspaso, o una re-consulta), y un valor fuera del catálogo dejaría el `<select>` en blanco
   * mientras el gate lo daría por elegido.
   */
  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    void (async () => {
      const [detail, tipos] = await Promise.all([
        tramitesClient.getInstance(instanceId).catch(() => null),
        esMatricula
          ? tramitesClient.listVehicleServiceTypes().catch(() => 'error' as const)
          : Promise.resolve<VehicleServiceTypeOption[]>([]),
      ]);
      if (!active) return;

      if (tipos === 'error') {
        setTiposServicioError('No se pudieron cargar los tipos de servicio.');
      } else {
        setTiposServicio(tipos);
      }
      setTiposServicioLoading(false);

      const valores = detail?.fieldValues ?? [];
      setFieldValues(valores);
      if (!esMatricula || tipos === 'error') return;
      const byKey = (key: string) => valores.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';
      const guardado = byKey('vehicle_service').toUpperCase();
      const enCatalogo = tipos.some((t) => t.code === guardado);
      if (!enCatalogo) return;
      setTipoServicioCode(guardado);
      setEmpresaVinculadoraNit(byKey('empresa_vinculadora_nit'));
      const razonSocial = byKey('empresa_vinculadora_razon_social');
      setEmpresaVinculadoraRazonSocial(razonSocial || null);
    })();
    return () => {
      active = false;
    };
  }, [instanceId, esMatricula]);

  /**
   * Gate de "Continuar": la misma regla que antes gobernaba el paso 1 —sin tipo de servicio no se
   * avanza, y con servicio PÚBLICO tampoco hasta que la consulta devuelva la razón social de la
   * empresa vinculadora—, ahora aplicada en el paso donde se captura. En solo lectura no hay nada
   * que completar: bloquear la navegación de un trámite ya enviado sería atrapar al operador.
   */
  useEffect(() => {
    const ok =
      !esMatricula ||
      readOnly ||
      (!!tipoServicioCode &&
        (tipoServicioCode !== 'PUBLICO' || !!empresaVinculadoraRazonSocial));
    onTipoServicioGateChange?.(ok);
    // `onTipoServicioGateChange` es un setState del shell recreado en cada render: incluirlo
    // reemitiría en bucle. Lo que gobierna la emisión es lo capturado aquí.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [esMatricula, readOnly, tipoServicioCode, empresaVinculadoraRazonSocial]);

  const recargarFieldValues = async () => {
    if (!instanceId) return;
    const detail = await tramitesClient.getInstance(instanceId).catch(() => null);
    if (detail?.fieldValues) setFieldValues(detail.fieldValues);
  };

  const persistir = async (items: { fieldKey: string; valueText: string | null }[]) => {
    if (!instanceId || items.length === 0) return;
    setSaving(true);
    setSaveError(null);
    try {
      await tramitesClient.patchFieldValues(
        instanceId,
        items.map((i) => ({
          formFieldId: null,
          fieldKey: i.fieldKey,
          valueText: i.valueText,
          valueJson: null,
        })),
      );
      await recargarFieldValues();
      onChanged?.();
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'No se pudo guardar. Reintenta.');
    } finally {
      setSaving(false);
    }
  };

  const handleTipoServicioChange = async (code: string) => {
    setTipoServicioCode(code);
    setEmpresaVinculadoraNit('');
    setEmpresaVinculadoraRazonSocial(null);
    setRuesError(null);
    setRuesUnavailable(false);
    // La empresa vinculadora (casilla 19) solo tiene sentido con servicio PÚBLICO: al cambiar de
    // tipo se borra también en el trámite, o quedaría un NIT huérfano en el FUR.
    await persistir([
      { fieldKey: 'vehicle_service', valueText: code || null },
      { fieldKey: 'empresa_vinculadora_nit', valueText: null },
      { fieldKey: 'empresa_vinculadora_razon_social', valueText: null },
    ]);
  };

  const handleEmpresaVinculadoraNitChange = (nit: string) => {
    setEmpresaVinculadoraNit(nit);
    // Editar el NIT invalida la razón social ya encontrada: exige una nueva consulta RUES. No se
    // persiste por tecla — la pareja NIT/razón social se guarda junta cuando la consulta resuelve.
    setEmpresaVinculadoraRazonSocial(null);
    setRuesError(null);
    setRuesUnavailable(false);
  };

  /**
   * Consulta RUES (siempre) para la razón social. El directorio de RL no sustituye al RUES.
   */
  const handleConsultarRues = async () => {
    const nit = empresaVinculadoraNit.trim();
    if (!nit) {
      setRuesError('Ingresa el NIT de la empresa vinculadora antes de consultar.');
      return;
    }
    setRuesLoading(true);
    setRuesError(null);
    setRuesUnavailable(false);
    try {
      const result = await tramitesClient.ruesPreview({ documentNumber: nit });
      if (result.found) {
        const razonSocial = shortRuesRazonSocial(result.razonSocial);
        setEmpresaVinculadoraRazonSocial(razonSocial);
        await persistir([
          { fieldKey: 'empresa_vinculadora_nit', valueText: nit },
          { fieldKey: 'empresa_vinculadora_razon_social', valueText: razonSocial || null },
        ]);
      } else {
        setEmpresaVinculadoraRazonSocial(null);
        setRuesError(RUES_NO_ENCONTRADO);
      }
    } catch (err) {
      setEmpresaVinculadoraRazonSocial(null);
      if (isRuesPreviewUnavailable(err)) {
        setRuesUnavailable(true);
      } else {
        setRuesError(err instanceof Error ? err.message : 'No se pudo consultar el RUES.');
      }
    } finally {
      setRuesLoading(false);
    }
  };

  // A4/B4 (HU #10674) — transformaciones color/combustible/carrocería: patch atómico de varias
  // claves (efectivo + flag) en una sola llamada, para que el valor declarado y su bandera queden
  // consistentes (el backend no pisa el efectivo si el flag está activo).
  const saveTransformacion = async (items: { fieldKey: string; valueText: string }[]) => {
    await persistir(items);
  };

  return (
    <div className="space-y-4">
      {/* Misma espera externa que las demás consultas del asistente (directorio y, si hace falta,
          RUES): se cubre con la escena de la propuesta en vez de dejar solo el rótulo del botón. */}
      {ruesLoading && <CarLoaderModal mode="runt" />}
      {/* Tipo de servicio (casilla 18 del FUR) — solo matrícula inicial. Seis valores fijos: un
          <select> simple es más accesible y más rápido de operar que un combobox con buscador
          (SearchableSelect), pensado para catálogos largos. */}
      {esMatricula && (
        <div className={noCardWrapper ? 'space-y-3' : 'rounded-2xl border bg-white p-4 dark:bg-[#162744] space-y-3'}>
          {!noCardWrapper && (
            <WizardCardHeader
              title="Tipo de servicio del vehículo"
              subtitle="Determina la casilla 18 del FUR. Con servicio público hay que identificar además la empresa vinculadora."
            />
          )}

          {tiposServicioLoading ? (
            <p className="text-xs opacity-70" role="status" aria-live="polite">
              Cargando tipos de servicio…
            </p>
          ) : tiposServicioError ? (
            <p className="text-xs font-medium" style={{ color: '#FF4E00' }} role="alert">
              {tiposServicioError}
            </p>
          ) : (
            /* Oleada 2 — cuando el tipo es PÚBLICO, selector + NIT+Buscar + Razón social
               quedan en la MISMA FILA (flex-wrap) para que el gestor los vea juntos sin
               desplazarse entre secciones distantes. Layout Lovable: MatriculaInicial Step 3. */
            <div className="flex flex-wrap items-end gap-4">
              <div className="min-w-[160px]">
                <label htmlFor="tramite-tipo-servicio" className="mb-1.5 block text-xs font-semibold">
                  Tipo de servicio
                </label>
                <select
                  id="tramite-tipo-servicio"
                  value={tipoServicioCode}
                  onChange={(e) => void handleTipoServicioChange(e.target.value)}
                  disabled={readOnly || saving}
                  className={`${WIZARD_SELECT} disabled:opacity-60`}
                >
                  <option value="">Selecciona…</option>
                  {tiposServicio.map((t) => (
                    <option key={t.id} value={t.code}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </div>

              {/* Empresa vinculadora: pegada al selector de tipo para que el gestor perciba la
                  relación NIT ↔ tipo PÚBLICO. El botón actúa sobre el NIT; el error se pinta bajo
                  ese mismo campo. La razón social queda en una segunda línea (flex-wrap) para las
                  razones sociales largas del RUES (Bancolombia: 79 caracteres). */}
              {tipoServicioCode === 'PUBLICO' && (
                <>
                  <div>
                    <label
                      htmlFor="tramite-empresa-vinculadora-nit"
                      className="mb-1.5 block text-xs font-semibold"
                    >
                      NIT empresa vinculadora
                    </label>
                    <div className="flex items-stretch gap-2">
                      <input
                        id="tramite-empresa-vinculadora-nit"
                        type="text"
                        inputMode="numeric"
                        value={empresaVinculadoraNit}
                        onChange={(e) => handleEmpresaVinculadoraNitChange(e.target.value)}
                        disabled={readOnly}
                        className={`${inputClass} min-w-0 w-40 flex-none disabled:opacity-60`}
                        placeholder="Ej. 900123456"
                        aria-describedby={ruesError ? 'tramite-rues-error' : undefined}
                        aria-invalid={ruesError ? true : undefined}
                      />
                      {!readOnly && (
                        <button
                          type="button"
                          onClick={() => void handleConsultarRues()}
                          disabled={ruesLoading || !empresaVinculadoraNit.trim()}
                          className={`${WIZARD_BTN} flex shrink-0 items-center justify-center gap-1.5 bg-[#557EFF] text-white focus-visible:ring-[#557EFF] disabled:cursor-not-allowed disabled:opacity-50`}
                          style={{ background: WIZARD_BTN_SOLID }}
                          aria-label="Buscar empresa en RUES"
                        >
                          <Search className="h-3.5 w-3.5" aria-hidden="true" />
                          {ruesLoading ? 'Consultando…' : 'Buscar'}
                        </button>
                      )}
                    </div>
                    {ruesError && (
                      <p
                        id="tramite-rues-error"
                        className="mt-1.5 text-xs font-medium leading-tight"
                        style={{ color: '#FF4E00' }}
                        role="alert"
                        aria-live="polite"
                      >
                        {ruesError}
                      </p>
                    )}
                  </div>

                  {/* El campo NO existe hasta que se consulta — `null` es "todavía sin consultar".
                      Ocupa una fila completa (w-full) porque las razones sociales del RUES llegan
                      largas y a media rejilla no se leen. */}
                  {empresaVinculadoraRazonSocial !== null && (
                    <div className="w-full sm:min-w-[280px] sm:flex-1">
                      <label
                        htmlFor="tramite-empresa-vinculadora-razon-social"
                        className="mb-1.5 block text-xs font-semibold"
                      >
                        Razón social
                      </label>
                      <output
                        id="tramite-empresa-vinculadora-razon-social"
                        className={`block w-full whitespace-pre-line break-words rounded-xl border bg-[#EEF5FF] px-3 py-2 text-xs leading-relaxed dark:bg-[#162744] ${
                          empresaVinculadoraRazonSocial ? '' : 'opacity-70'
                        }`}
                      >
                        {empresaVinculadoraRazonSocial || 'El RUES no reportó razón social para este NIT'}
                      </output>
                    </div>
                  )}
                </>
              )}
            </div>
          )}

          {ruesUnavailable && (
            <div
              className="flex flex-col gap-2 rounded-xl p-3 sm:flex-row sm:items-center sm:justify-between"
              style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
              role="alert"
              aria-live="assertive"
            >
              <span className="text-xs font-medium" style={{ color: '#FF4E00' }}>
                {RUES_NO_DISPONIBLE}
              </span>
              <button
                type="button"
                onClick={() => void handleConsultarRues()}
                className="shrink-0 rounded-xl bg-[#557EFF] px-4 py-2 text-xs font-semibold text-white"
                style={{ background: WIZARD_BTN_SOLID }}
                aria-label="Reintentar consulta al RUES"
              >
                Reintentar
              </button>
            </div>
          )}

          {saveError && (
            <p className="text-xs font-medium" style={{ color: '#FF4E00' }} role="alert">
              {saveError}
            </p>
          )}
        </div>
      )}

      {/* Trámites simultáneos: la tarjeta de la propuesta (`MatriculaInicial`, Step 3).
          Oleada 2 (PDF 20/08) — TramiteWizard puede moverla después del checklist de documentos
          pasando hideTransformaciones=true. Sin ese prop se mantiene aquí (tests unitarios). */}
      {!hideTransformaciones && (
        <VehicleTransformationsCard
          fieldValues={fieldValues}
          readOnly={readOnly}
          saving={saving}
          onPatch={saveTransformacion}
        />
      )}
    </div>
  );
}

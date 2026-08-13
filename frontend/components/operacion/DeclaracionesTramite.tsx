'use client';

import { useEffect, useState } from 'react';
import { Info, Search } from 'lucide-react';
import { isRuesPreviewUnavailable, tramitesClient } from '@/lib/api/tramites-client';
import type {
  FieldValue,
  VehicleServiceTypeOption,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import { VehicleTransformationsCard } from './VehicleTransformationsCard';
import { WizardAccordion } from './WizardAccordion';
import { WizardCardHeader } from './wizard-atoms';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { WIZARD_INPUT } from './wizard-field-styles';

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
 * vinculadora de la casilla 19) y TRANSFORMACIONES Y CONDICIONES del trámite.
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
}: {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Refresca el wizard: las condiciones declaradas cambian el checklist de documentos. */
  onChanged?: () => void;
  /** Informa al pie si el tipo de servicio está completo (gate de "Continuar", solo matrícula). */
  onTipoServicioGateChange?: (ok: boolean) => void;
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
  const [razonSocialDesdeDirectorio, setRazonSocialDesdeDirectorio] = useState(false);

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
    setRazonSocialDesdeDirectorio(false);
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
    setRazonSocialDesdeDirectorio(false);
  };

  /**
   * Misma escalera que usa el actor jurídico (HU #10906, R3): PRIMERO el directorio de la compañía,
   * y solo si el NIT no está ahí se gasta una consulta al proveedor externo. Dos motivos: es
   * instantáneo cuando la empresa ya se consultó antes, y deja de depender de que el RUES esté arriba
   * para el caso más común. Si el directorio falla no se bloquea nada — se cae al RUES.
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
    setRazonSocialDesdeDirectorio(false);
    try {
      const preload = await tramitesClient
        .lookupLegalRepresentativeByNit(nit)
        .catch(() => null); // el directorio es un atajo, no un requisito: su fallo no corta el flujo
      const razonSocialDirectorio = preload?.company.razonSocial?.trim();
      if (razonSocialDirectorio) {
        const nitDirectorio = preload!.company.nit || nit;
        setEmpresaVinculadoraNit(nitDirectorio);
        setEmpresaVinculadoraRazonSocial(razonSocialDirectorio);
        setRazonSocialDesdeDirectorio(true);
        await persistir([
          { fieldKey: 'empresa_vinculadora_nit', valueText: nitDirectorio },
          { fieldKey: 'empresa_vinculadora_razon_social', valueText: razonSocialDirectorio },
        ]);
        return;
      }

      const result = await tramitesClient.ruesPreview({ documentNumber: nit });
      if (result.found) {
        const razonSocial = result.razonSocial ?? '';
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

  // Banderas manuales que gatillan documentos condicionales (el backend las lee en
  // TramiteDocumentContextMapper). Leasing solo aplica en traspaso; la prenda se gestiona aparte
  // con PrendaForm (Feature #10585) y la carrocería vive en VehicleTransformationsCard (P2/P3).
  const esLeasing = fieldValues.find((f) => f.fieldKey === 'es_leasing')?.valueText === 'true';

  return (
    <div className="space-y-4">
      {/* Misma espera externa que las demás consultas del asistente (directorio y, si hace falta,
          RUES): se cubre con la escena de la propuesta en vez de dejar solo el rótulo del botón. */}
      {ruesLoading && <CarLoaderModal mode="runt" />}
      {/* Tipo de servicio (casilla 18 del FUR) — solo matrícula inicial. Seis valores fijos: un
          <select> simple es más accesible y más rápido de operar que un combobox con buscador
          (SearchableSelect), pensado para catálogos largos. */}
      {esMatricula && (
        <div className="rounded-2xl border bg-white p-4 dark:bg-[#162744] space-y-3">
          <WizardCardHeader
            title="Tipo de servicio del vehículo"
            subtitle="Determina la casilla 18 del FUR. Con servicio público hay que identificar además la empresa vinculadora."
          />

          {tiposServicioLoading ? (
            <p className="text-xs opacity-60" role="status" aria-live="polite">
              Cargando tipos de servicio…
            </p>
          ) : tiposServicioError ? (
            <p className="text-xs font-medium" style={{ color: '#FF4E00' }} role="alert">
              {tiposServicioError}
            </p>
          ) : (
            <div className="sm:max-w-xs">
              <label htmlFor="tramite-tipo-servicio" className="mb-1.5 block text-xs font-semibold">
                Tipo de servicio
              </label>
              <select
                id="tramite-tipo-servicio"
                value={tipoServicioCode}
                onChange={(e) => void handleTipoServicioChange(e.target.value)}
                disabled={readOnly || saving}
                className={`${inputClass} disabled:opacity-60`}
              >
                <option value="">Selecciona…</option>
                {tiposServicio.map((t) => (
                  <option key={t.id} value={t.code}>
                    {t.name}
                  </option>
                ))}
              </select>
            </div>
          )}

          {/* Empresa vinculadora: dos columnas alineadas. El botón va PEGADO al NIT porque actúa
              sobre él, y el error se pinta bajo ese mismo campo — no suelto al final de la tarjeta,
              donde el operador no sabía a cuál de los dos campos se refería. */}
          {tipoServicioCode === 'PUBLICO' && (
            <div className="grid max-w-3xl gap-4 sm:grid-cols-2">
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
                    className={`${inputClass} min-w-0 flex-1 disabled:opacity-60`}
                    placeholder="Ej. 900123456"
                    aria-describedby={ruesError ? 'tramite-rues-error' : undefined}
                    aria-invalid={ruesError ? true : undefined}
                  />
                  {!readOnly && (
                    <button
                      type="button"
                      onClick={() => void handleConsultarRues()}
                      disabled={ruesLoading || !empresaVinculadoraNit.trim()}
                      className="flex shrink-0 items-center gap-1.5 rounded-xl px-3 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                      style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
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

              {/* Fila completa: las razones sociales del RUES llegan largas (la de Bancolombia son
                  79 caracteres, con su cláusula de denominación alterna) y a media rejilla no se
                  leen. */}
              {/* El campo NO existe hasta que se consulta. Un recuadro vacío con "la trae el RUES"
                  ocupa sitio para no decir nada y se lee como un dato pendiente de llenar, cuando en
                  realidad no hay nada que el gestor pueda hacer ahí: aparece solo, con el nombre
                  dentro, en cuanto el directorio o el RUES responden. `null` es exactamente eso —
                  "todavía no se ha consultado" — y vuelve a null al cambiar el NIT, así que el campo
                  también desaparece si el gestor corrige el número. */}
              {empresaVinculadoraRazonSocial !== null && (
                <div className="sm:col-span-2">
                  <label
                    htmlFor="tramite-empresa-vinculadora-razon-social"
                    className="mb-1.5 block text-xs font-semibold"
                  >
                    Razón social
                  </label>
                  {/* `output`, no `input`: el dato NO se edita, y un campo de una línea recorta el
                      nombre dejando el resto accesible solo moviendo el cursor — en un campo de solo
                      lectura, inalcanzable. `output` envuelve el texto, es etiquetable (conserva el
                      `htmlFor` de arriba) y su rol implícito `status` anuncia el valor cuando el RUES
                      responde, que es justo cuando el gestor quiere oírlo. */}
                  <output
                    id="tramite-empresa-vinculadora-razon-social"
                    aria-describedby={
                      razonSocialDesdeDirectorio ? 'tramite-razon-social-origen' : undefined
                    }
                    className={`block w-full whitespace-pre-line break-words rounded-xl border bg-[#F4F6FA] px-3 py-2 text-xs leading-relaxed dark:bg-[#131A22] ${
                      empresaVinculadoraRazonSocial ? '' : 'opacity-60'
                    }`}
                  >
                    {/* Aquí ya se consultó (el `null` no llega: el campo entero no se pinta). Falta
                        distinguir el otro vacío, que sí es un resultado: el RUES respondió `found` SIN
                        razón social, y la consulta lo guarda como cadena vacía. Sin este `||` el
                        recuadro aparecería colapsado a puro padding justo cuando hay algo que explicar. */}
                    {empresaVinculadoraRazonSocial || 'El RUES no reportó razón social para este NIT'}
                  </output>
                  {/* De dónde salió el dato, con el mismo criterio honesto del actor jurídico: si vino
                      del directorio NO se consultó el RUES, y el gestor tiene derecho a saberlo. */}
                  {razonSocialDesdeDirectorio && (
                    <p
                      id="tramite-razon-social-origen"
                      className="mt-1.5 flex items-center gap-1 text-xs leading-tight"
                      style={{ color: '#557EFF' }}
                      role="status"
                      aria-live="polite"
                    >
                      <Info className="h-3 w-3 shrink-0" aria-hidden="true" />
                      Precargado desde el directorio de la compañía
                    </p>
                  )}
                </div>
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
                className="shrink-0 rounded-xl px-4 py-2 text-xs font-semibold text-white"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
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

      {/* Transformaciones y condiciones, juntas en un acordeón como en la propuesta: las dos
          declaran lo mismo —en qué se aparta el vehículo de lo que dice el RUNT— y las dos ajustan
          el checklist de documentos. Separadas en dos tarjetas se leían como temas distintos.
          Plegado por defecto: en la mayoría de trámites no hay nada que declarar. */}
      <WizardAccordion title="Transformaciones y condiciones del trámite">
        <div className="space-y-4">
          <VehicleTransformationsCard
            fieldValues={fieldValues}
            readOnly={readOnly}
            saving={saving}
            onPatch={saveTransformacion}
            bare
          />

          {/* Condiciones: en traspaso el leasing; en matrícula ya no hay flags aquí (carrocería
              vive en Transformaciones). Solo se pinta el bloque si hay algo que marcar. */}
          {!esMatricula && (
            <div className="space-y-2 border-t pt-4">
              <WizardCardHeader
                title="Condiciones del trámite"
                level="h4"
                className=""
                subtitle="Marca las condiciones que apliquen; el checklist de documentos se ajusta automáticamente."
              />
              <label className="flex items-start gap-2.5">
                <input
                  type="checkbox"
                  checked={esLeasing}
                  onChange={(e) =>
                    void persistir([
                      { fieldKey: 'es_leasing', valueText: e.target.checked ? 'true' : 'false' },
                    ])
                  }
                  disabled={readOnly || saving}
                  className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF] disabled:opacity-60"
                />
                <span className="text-xs">
                  <span className="font-semibold">Vehículo en leasing</span>
                  <span className="mt-0.5 block opacity-55">
                    Exige contrato de leasing y declaración de la arrendadora.
                  </span>
                </span>
              </label>
            </div>
          )}
        </div>
      </WizardAccordion>
    </div>
  );
}

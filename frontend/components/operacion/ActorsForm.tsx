'use client';

import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useMemo,
  useState,
} from 'react';
import { Search, UserRound } from 'lucide-react';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import type { WizardStepFormHandle } from './wizard-step-form';
import { useProcedureActors } from '@/hooks/useProcedureActors';
import { tramitesClient } from '@/lib/api/tramites-client';
import { filterCiudades } from '@/lib/catalogs/ciudades-co';
import {
  sanitizeDocNumber,
  validateDocNumber,
  sanitizeName,
  validateReadableName,
} from '@/lib/validation/fieldRules';
import type {
  ActorDocumentType,
  ActorRol,
  ProcedureActor,
  RuntPersonLookupResult,
} from '@/lib/api/types/procedure-runtime';

export type ActorsModalidad = 'matricula_inicial' | 'traspaso';

/** Handle imperativo para que la shell del wizard dispare guardar+validar. */
export type ActorsFormHandle = WizardStepFormHandle;

interface Props {
  instanceId: string | null;
  modalidad: ActorsModalidad;
  /**
   * Acota el formulario a roles concretos (p.ej. un step solo "vendedor" o
   * solo "comprador" en el wizard diferenciado). Si se omite, usa los roles
   * por defecto de la modalidad.
   */
  roles?: ActorRol[];
  /** Callback opcional tras un guardado exitoso (p.ej. avanzar el wizard). */
  onSaved?: (actors: ProcedureActor[]) => void;
  /**
   * Embebido en el wizard: oculta el botón "Guardar actores" propio (la shell
   * dispara save() vía ref desde el footer "Guardar y continuar").
   */
  embeddedInWizard?: boolean;
  /**
   * Layout en 2 secciones (Identificación + Datos de contacto). Se activa
   * explícitamente o de forma implícita cuando el form es de un solo comprador.
   */
  layout?: 'split';
  /**
   * Siembra el documento del actor (tipo + número) desde el documento del
   * propietario capturado en el paso 1 de la consulta (`owner_document_*` en
   * field_values), cuando el actor aún no tiene documento. Pensado para el paso
   * "vendedor" del traspaso: en un traspaso estándar el vendedor ES el
   * propietario registrado que validó el vehículo. El valor queda editable.
   */
  seedDocumentoFromOwner?: boolean;
}

const DOC_OPTIONS: { value: ActorDocumentType; label: string }[] = [
  { value: 'CC', label: 'Cédula de ciudadanía (CC)' },
  { value: 'CE', label: 'Cédula de extranjería (CE)' },
  { value: 'NIT', label: 'NIT' },
  { value: 'PAS', label: 'Pasaporte (PAS)' },
  { value: 'TI', label: 'Tarjeta de identidad (TI)' },
];

const ROL_LABEL: Record<ActorRol, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

/** Roles requeridos por modalidad. Matrícula = solo comprador; traspaso = ambos. */
function rolesFor(modalidad: ActorsModalidad): ActorRol[] {
  return modalidad === 'matricula_inicial'
    ? ['comprador']
    : ['vendedor', 'comprador'];
}

function emptyActor(rol: ActorRol): ProcedureActor {
  return {
    rol,
    tipoDocumento: 'CC',
    numeroDocumento: '',
    nombreCompleto: '',
    email: '',
    telefono: '',
    ciudad: '',
    direccion: '',
  };
}

// Validación de email pragmática (no exhaustiva): algo@algo.dominio.
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Errores por actor, indexados por campo. Vacío = sin errores. */
export type ActorErrors = Partial<Record<keyof ProcedureActor, string>>;

export interface ActorsValidation {
  valid: boolean;
  /** Errores por actor en el mismo orden del arreglo. */
  byActor: ActorErrors[];
}

/**
 * Valida requeridos + email + (traspaso) vendedor≠comprador por doc/email.
 * Pura: sin estado, testeable de forma aislada. Ciudad/dirección son opcionales.
 */
export function validateActors(
  actors: ProcedureActor[],
  modalidad: ActorsModalidad,
): ActorsValidation {
  const byActor: ActorErrors[] = actors.map((a) => {
    const e: ActorErrors = {};
    if (!a.numeroDocumento.trim()) e.numeroDocumento = 'Número requerido';
    else {
      const docErr = validateDocNumber(a.numeroDocumento.trim(), a.tipoDocumento);
      if (docErr) e.numeroDocumento = docErr;
    }
    if (!a.nombreCompleto.trim()) e.nombreCompleto = 'Nombre requerido';
    else {
      const nameErr = validateReadableName(a.nombreCompleto.trim(), 'El nombre');
      if (nameErr) e.nombreCompleto = nameErr;
    }
    if (!a.email.trim()) e.email = 'Correo requerido';
    else if (!EMAIL_RE.test(a.email.trim())) e.email = 'Correo no válido';
    return e;
  });

  // Regla vendedor≠comprador (solo traspaso, con ambos roles presentes).
  if (modalidad === 'traspaso') {
    const vendedor = actors.find((a) => a.rol === 'vendedor');
    const comprador = actors.find((a) => a.rol === 'comprador');
    if (vendedor && comprador) {
      const sameDoc =
        vendedor.tipoDocumento === comprador.tipoDocumento &&
        vendedor.numeroDocumento.trim() !== '' &&
        vendedor.numeroDocumento.trim() === comprador.numeroDocumento.trim();
      const sameEmail =
        vendedor.email.trim() !== '' &&
        vendedor.email.trim().toLowerCase() ===
          comprador.email.trim().toLowerCase();
      if (sameDoc || sameEmail) {
        const msg =
          'El vendedor y el comprador no pueden ser la misma persona (documento o correo coinciden).';
        const ci = actors.indexOf(comprador);
        if (sameDoc) byActor[ci].numeroDocumento = msg;
        if (sameEmail) byActor[ci].email = msg;
      }
    }
  }

  const valid = byActor.every((e) => Object.keys(e).length === 0);
  return { valid, byActor };
}

/** Normaliza opcionales vacíos a undefined antes de persistir. */
function normalizeActors(actors: ProcedureActor[]): ProcedureActor[] {
  const blankToUndef = (v?: string) => (v?.trim() ? v.trim() : undefined);
  return actors.map((a) => ({
    ...a,
    telefono: blankToUndef(a.telefono),
    ciudad: blankToUndef(a.ciudad),
    direccion: blankToUndef(a.direccion),
  }));
}

const INPUT_BASE =
  'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF] aria-[invalid=true]:border-[#FF4E00]';

const GRADIENT = 'linear-gradient(135deg,#557EFF,#00DBD5)';

/** Estado por actor de la consulta RUNT (autopopulado). */
type RuntState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'found'; result: RuntPersonLookupResult }
  | { status: 'not_found' }
  | { status: 'error'; message: string };

/**
 * Formulario reutilizable de captura de actores. Dos presentaciones:
 *  - SPLIT (un solo comprador / `layout='split'`): 2 secciones — Identificación
 *    (documento + Consultar RUNT + resultado) y Datos de contacto (incluye
 *    ciudad con autocomplete y dirección). Refleja el mockup de matrícula.
 *  - MULTI (traspaso): un fieldset por actor (vendedor + comprador).
 *
 * Expone `save()` vía ref para el patrón "Guardar y continuar" del wizard.
 */
/** Tipos de documento válidos del selector (para validar el seed del paso 1). */
const DOC_VALUES = new Set<ActorDocumentType>(DOC_OPTIONS.map((o) => o.value));

export const ActorsForm = forwardRef<ActorsFormHandle, Props>(function ActorsForm(
  {
    instanceId,
    modalidad,
    roles: rolesProp,
    onSaved,
    embeddedInWizard = false,
    layout,
    seedDocumentoFromOwner = false,
  },
  ref,
) {
  const roles = useMemo(
    () => rolesProp ?? rolesFor(modalidad),
    [rolesProp, modalidad],
  );
  const { state, save, clearError } = useProcedureActors(instanceId);
  // Solo lectura (Track C): inputs deshabilitados + sin Consultar RUNT/guardar.
  const readOnly = useWizardReadOnly();

  const [actors, setActors] = useState<ProcedureActor[]>(() =>
    roles.map(emptyActor),
  );
  const [showErrors, setShowErrors] = useState(false);
  // Estado de la consulta RUNT por índice de actor (autopopulado).
  const [runt, setRunt] = useState<Record<number, RuntState>>({});
  // Autocomplete de ciudad por índice de actor.
  const [ciudadOpen, setCiudadOpen] = useState<Record<number, boolean>>({});

  // Documento del propietario capturado en el paso 1 (`owner_document_*` en
  // field_values), para sembrar el documento del vendedor cuando aún no lo tiene.
  const [ownerSeed, setOwnerSeed] = useState<{
    tipo: ActorDocumentType;
    numero: string;
  } | null>(null);

  // Carga el documento del propietario desde los field_values de la instancia.
  // Solo aplica cuando `seedDocumentoFromOwner` (paso vendedor del traspaso).
  useEffect(() => {
    if (!seedDocumentoFromOwner || !instanceId) return;
    let active = true;
    tramitesClient
      .getInstance(instanceId)
      .then((detail) => {
        if (!active || !detail?.fieldValues) return;
        const byKey = (key: string) =>
          detail.fieldValues.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';
        const numero = byKey('owner_document_number');
        if (!numero) return;
        const tipoRaw = byKey('owner_document_type') as ActorDocumentType;
        setOwnerSeed({ numero, tipo: DOC_VALUES.has(tipoRaw) ? tipoRaw : 'CC' });
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [seedDocumentoFromOwner, instanceId]);

  // Aplica el documento del propietario (paso 1) a un actor sin documento. No
  // pisa un documento ya escrito/persistido: solo siembra el campo vacío.
  const withOwnerSeed = (a: ProcedureActor): ProcedureActor =>
    ownerSeed && !a.numeroDocumento.trim()
      ? { ...a, numeroDocumento: ownerSeed.numero, tipoDocumento: ownerSeed.tipo }
      : a;

  // Rehidrata desde el backend cuando llegan actores cargados, respetando los
  // roles de la modalidad (rellena los faltantes con vacíos).
  const loadedKey = state.actors
    ? state.actors.map((a) => a.rol).join(',')
    : null;
  const [hydratedKey, setHydratedKey] = useState<string | null>(null);
  if (state.actors && loadedKey !== hydratedKey) {
    setHydratedKey(loadedKey);
    setActors(
      roles.map((rol) => {
        const found = state.actors?.find((a) => a.rol === rol);
        return withOwnerSeed(found ? { ...emptyActor(rol), ...found } : emptyActor(rol));
      }),
    );
  }

  // El seed puede llegar después de la rehidratación (fetch async). Cuando
  // aterriza, completa el documento del actor si seguía vacío.
  useEffect(() => {
    if (!ownerSeed) return;
    setActors((prev) => prev.map(withOwnerSeed));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ownerSeed]);

  // Split implícito: un único comprador. Explícito: layout='split'.
  const isSplit =
    layout === 'split' || (roles.length === 1 && roles[0] === 'comprador');

  const validation = validateActors(actors, modalidad);

  const updateActor = (index: number, patch: Partial<ProcedureActor>) => {
    setActors((prev) =>
      prev.map((a, i) => {
        if (i !== index) return a;
        const next = { ...a, ...patch };
        // Saneo de caracteres por tipo de campo (Ajuste 3): número de documento según
        // el tipo (pasaporte alfanumérico, resto solo dígitos) y nombre sin caracteres
        // especiales. Se re-sanea el documento al cambiar de tipo (p.ej. PAS→CC).
        if (patch.numeroDocumento !== undefined || patch.tipoDocumento !== undefined)
          next.numeroDocumento = sanitizeDocNumber(next.numeroDocumento, next.tipoDocumento);
        if (patch.nombreCompleto !== undefined)
          next.nombreCompleto = sanitizeName(next.nombreCompleto);
        return next;
      }),
    );
  };

  const setRuntFor = (index: number, value: RuntState) =>
    setRunt((prev) => ({ ...prev, [index]: value }));

  // Consulta RUNT por documento y, si encuentra a la persona, autopopula el
  // actor. Nunca bloquea la captura manual: not_found/error dejan los campos
  // editables.
  const handleRuntLookup = async (index: number) => {
    const actor = actors[index];
    const documentNumber = actor.numeroDocumento.trim();
    if (!instanceId || !documentNumber || runt[index]?.status === 'loading') {
      return;
    }
    setRuntFor(index, { status: 'loading' });
    try {
      const result = await tramitesClient.runtPersonLookup(instanceId, {
        documentType: actor.tipoDocumento,
        documentNumber,
      });
      if (result.found) {
        updateActor(index, {
          nombreCompleto: result.fullName ?? actor.nombreCompleto,
          tipoDocumento:
            (result.documentType as ActorDocumentType) || actor.tipoDocumento,
          numeroDocumento: result.documentNumber || actor.numeroDocumento,
        });
        setRuntFor(index, { status: 'found', result });
      } else {
        setRuntFor(index, { status: 'not_found' });
      }
    } catch (err) {
      setRuntFor(index, {
        status: 'error',
        message: err instanceof Error ? err.message : 'Error consultando RUNT',
      });
    }
  };

  // Valida + guarda. Núcleo compartido por el submit propio y el save() del ref.
  const submitActors = async (): Promise<boolean> => {
    setShowErrors(true);
    if (!validateActors(actors, modalidad).valid) return false;
    const normalized = normalizeActors(actors);
    const ok = await save(normalized);
    if (ok) onSaved?.(normalized);
    return ok;
  };

  useImperativeHandle(ref, () => ({ save: submitActors }));

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    await submitActors();
  };

  const errorBanner = state.error && (
    <div
      className="rounded-xl p-3 text-xs border mb-3 flex items-center justify-between gap-3"
      style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
      role="alert"
      aria-live="polite"
    >
      <span>{state.error}</span>
      <button type="button" onClick={clearError} className="font-bold" aria-label="Descartar error">
        ×
      </button>
    </div>
  );

  const footer = !embeddedInWizard && (
    <div className="flex items-center justify-between gap-3 mt-4">
      {state.saved ? (
        <span
          className="text-[11px] font-semibold"
          style={{ color: '#8CC63F' }}
          role="status"
          aria-live="polite"
        >
          Actores guardados ✓
        </span>
      ) : (
        <span />
      )}
      <button
        type="submit"
        disabled={state.saving}
        className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
        style={{ background: GRADIENT }}
      >
        {state.saving ? 'Guardando…' : 'Guardar actores'}
      </button>
    </div>
  );

  // ── RUNT result block (compartido entre layouts) ──────────────────────────
  const runtResult = (index: number) => {
    const runtState: RuntState = runt[index] ?? { status: 'idle' };
    if (runtState.status === 'found') {
      const r = runtState.result;
      const hasLicenses = r.hasActiveLicense ?? (r.licenseStatus != null);
      return (
        <div className="space-y-2" role="status" aria-live="polite">
          {/* Card A — Datos del conductor */}
          <div
            className="rounded-xl p-3 text-xs border"
            style={{ borderColor: '#8CC63F', background: 'rgba(140,198,63,0.08)' }}
          >
            <p className="font-semibold mb-2 flex items-center gap-1.5" style={{ color: '#5a8a1f' }}>
              <span aria-hidden="true">✓</span>
              Persona encontrada en RUNT
            </p>
            <div className="grid grid-cols-3 gap-x-4 gap-y-1.5">
              <div>
                <span className="opacity-60 font-normal">Nombres: </span>
                <span className="font-semibold" style={{ color: '#162744' }}>
                  {r.firstName ?? r.fullName ?? '—'}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">Apellidos: </span>
                <span className="font-semibold" style={{ color: '#162744' }}>
                  {r.lastName ?? '—'}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">Documento: </span>
                <span className="font-semibold font-mono" style={{ color: '#162744' }}>
                  {r.documentNumber}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">Estado: </span>
                <span
                  className="font-semibold"
                  style={{ color: r.citizenStatus === 'ACTIVA' ? '#5a8a1f' : '#162744' }}
                >
                  {r.citizenStatus ?? r.licenseStatus ?? '—'}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">Licencias: </span>
                <span className="font-semibold" style={{ color: '#162744' }}>
                  {hasLicenses
                    ? `Sí${r.licenseCategories ? ` (${r.licenseCategories})` : ''}`
                    : 'No'}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">Conductor: </span>
                <span
                  className="font-semibold"
                  style={{ color: r.licenseStatus === 'ACTIVO' ? '#5a8a1f' : '#162744' }}
                >
                  {r.licenseStatus ?? '—'}
                </span>
              </div>
            </div>
          </div>

          {/* Card B — Multas */}
          {r.hasPendingFines !== undefined && (
            <div
              className="rounded-xl p-3 text-xs border flex items-center gap-2"
              style={
                r.hasPendingFines
                  ? { borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }
                  : { borderColor: '#8CC63F', background: 'rgba(140,198,63,0.06)', color: '#5a8a1f' }
              }
              role="status"
            >
              <span aria-hidden="true">{r.hasPendingFines ? '⚠' : '⊙'}</span>
              <span className="font-semibold">
                {r.hasPendingFines
                  ? 'ALERTA: Comparendos/Multas pendientes'
                  : `Sin multas ni comparendos pendientes${r.nroPazYSalvo ? ` · Paz y Salvo ${r.nroPazYSalvo}` : ''}`}
              </span>
            </div>
          )}
        </div>
      );
    }
    if (runtState.status === 'not_found') {
      return (
        <div
          className="rounded-xl p-3 text-[11px] border opacity-80"
          style={{ borderColor: '#DFE5ED' }}
          role="status"
          aria-live="polite"
        >
          No se encontró en RUNT — completa los datos manualmente.
        </div>
      );
    }
    if (runtState.status === 'error') {
      return (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          No se pudo consultar RUNT ({runtState.message}). Puedes completar los datos manualmente.
        </div>
      );
    }
    return null;
  };

  // ── Layout SPLIT (un comprador): 2 secciones ──────────────────────────────
  if (isSplit && actors.length === 1) {
    const actor = actors[0];
    const errors = showErrors ? validation.byActor[0] : {};
    const runtState: RuntState = runt[0] ?? { status: 'idle' };
    const ciudades = filterCiudades(actor.ciudad ?? '');
    const showCiudades = !!ciudadOpen[0] && ciudades.length > 0;
    const sectionHeader = 'border-b px-4 py-3 flex items-center gap-2';
    return (
      <form
        onSubmit={handleSubmit}
        aria-label="Captura de actores del trámite"
        noValidate
      >
       <fieldset disabled={readOnly} className="space-y-5 min-w-0 border-0 p-0 m-0">
        {errorBanner}

        {/* Sección A — Identificación */}
        <section className="rounded-2xl border bg-white dark:bg-[#0B0F14] overflow-hidden" style={{ borderColor: '#DFE5ED' }}>
          <div className={sectionHeader} style={{ borderColor: '#DFE5ED', background: 'rgba(85,126,255,0.04)' }}>
            <UserRound className="h-4 w-4" style={{ color: '#557EFF' }} />
            <span className="text-[11px] font-bold uppercase tracking-wide" style={{ color: '#162744' }}>
              {`Identificación · ${ROL_LABEL[actor.rol]}`}
            </span>
          </div>
          <div className="p-4 space-y-3">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-start">
              <div className="flex-1">
                <input
                  id="comprador-numeroDoc"
                  type="text"
                  value={actor.numeroDocumento}
                  onChange={(e) => updateActor(0, { numeroDocumento: e.target.value })}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      void handleRuntLookup(0);
                    }
                  }}
                  aria-label="Número de documento"
                  aria-invalid={!!errors.numeroDocumento}
                  aria-describedby={errors.numeroDocumento ? 'comprador-numeroDoc-err' : undefined}
                  placeholder={`Número de documento del ${actor.rol}…`}
                  className={`${INPUT_BASE} font-mono`}
                  style={{ borderColor: '#DFE5ED' }}
                />
                {errors.numeroDocumento && (
                  <p id="comprador-numeroDoc-err" className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                    {errors.numeroDocumento}
                  </p>
                )}
              </div>
              {!readOnly && (
                <button
                  type="button"
                  onClick={() => void handleRuntLookup(0)}
                  disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                  className="flex shrink-0 items-center justify-center gap-2 rounded-xl px-5 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                  style={{ background: GRADIENT }}
                  aria-label="Consultar RUNT"
                >
                  <Search className="h-3.5 w-3.5" />
                  {runtState.status === 'loading' ? 'Consultando…' : 'Consultar RUNT'}
                </button>
              )}
            </div>
            {runtResult(0)}
          </div>
        </section>

        {/* Sección B — Datos de contacto */}
        <section className="rounded-2xl border bg-white dark:bg-[#0B0F14] overflow-hidden" style={{ borderColor: '#DFE5ED' }}>
          <div className="border-b px-4 py-3" style={{ borderColor: '#DFE5ED', background: 'rgba(85,126,255,0.04)' }}>
            <span className="text-[11px] font-bold uppercase tracking-wide" style={{ color: '#162744' }}>
              Datos de contacto
            </span>
          </div>
          <div className="p-4 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {/* Nombre completo */}
            <div className="lg:col-span-2">
              <label htmlFor="comprador-nombre" className="text-xs font-semibold mb-1.5 flex items-center gap-1.5">
                Nombre completo
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
              </label>
              <input
                id="comprador-nombre"
                type="text"
                value={actor.nombreCompleto}
                onChange={(e) => updateActor(0, { nombreCompleto: e.target.value })}
                aria-invalid={!!errors.nombreCompleto}
                aria-describedby={errors.nombreCompleto ? 'comprador-nombre-err' : undefined}
                className={INPUT_BASE}
                style={{ borderColor: '#DFE5ED' }}
              />
              {errors.nombreCompleto && (
                <p id="comprador-nombre-err" className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                  {errors.nombreCompleto}
                </p>
              )}
            </div>
            {/* Documento (readonly — viene de la consulta) */}
            <div>
              <label htmlFor="comprador-doc-ro" className="text-xs font-semibold mb-1.5 block">
                Documento
              </label>
              <input
                id="comprador-doc-ro"
                type="text"
                value={actor.numeroDocumento}
                readOnly
                className="w-full px-3 py-2 rounded-xl border text-xs font-mono opacity-80"
                style={{ borderColor: '#DFE5ED', background: 'rgba(223,229,237,0.35)' }}
              />
            </div>
            {/* Email */}
            <div>
              <label htmlFor="comprador-email" className="text-xs font-semibold mb-1.5 flex items-center gap-1.5">
                Correo electrónico
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
              </label>
              <input
                id="comprador-email"
                type="email"
                value={actor.email}
                onChange={(e) => updateActor(0, { email: e.target.value })}
                placeholder="correo@ejemplo.com"
                aria-invalid={!!errors.email}
                aria-describedby={errors.email ? 'comprador-email-err' : undefined}
                className={INPUT_BASE}
                style={{ borderColor: '#DFE5ED' }}
              />
              {errors.email && (
                <p id="comprador-email-err" className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                  {errors.email}
                </p>
              )}
            </div>
            {/* Teléfono (opcional) */}
            <div>
              <label htmlFor="comprador-telefono" className="text-xs font-semibold mb-1.5 block">
                Teléfono <span className="opacity-50 font-normal">(opcional)</span>
              </label>
              <input
                id="comprador-telefono"
                type="tel"
                value={actor.telefono ?? ''}
                onChange={(e) => updateActor(0, { telefono: e.target.value })}
                placeholder="3001234567"
                className={INPUT_BASE}
                style={{ borderColor: '#DFE5ED' }}
              />
            </div>
            {/* Ciudad (autocomplete) */}
            <div className="relative">
              <label htmlFor="comprador-ciudad" className="text-xs font-semibold mb-1.5 block">
                Ciudad
              </label>
              <input
                id="comprador-ciudad"
                type="text"
                value={actor.ciudad ?? ''}
                onChange={(e) => {
                  updateActor(0, { ciudad: e.target.value });
                  setCiudadOpen((p) => ({ ...p, 0: true }));
                }}
                onFocus={() => {
                  if ((actor.ciudad ?? '').trim().length >= 2) setCiudadOpen((p) => ({ ...p, 0: true }));
                }}
                onBlur={() => setTimeout(() => setCiudadOpen((p) => ({ ...p, 0: false })), 150)}
                autoComplete="off"
                placeholder="Escribe para buscar…"
                className={INPUT_BASE}
                style={{ borderColor: '#DFE5ED' }}
              />
              {showCiudades && (
                <ul
                  className="absolute top-full left-0 right-0 mt-1 z-50 max-h-48 overflow-auto rounded-xl border bg-white dark:bg-[#0B0F14]"
                  style={{ borderColor: '#DFE5ED' }}
                  aria-label="Sugerencias de ciudad"
                >
                  {ciudades.map((c) => (
                    <li key={c}>
                      <button
                        type="button"
                        onMouseDown={(e) => {
                          e.preventDefault();
                          updateActor(0, { ciudad: c });
                          setCiudadOpen((p) => ({ ...p, 0: false }));
                        }}
                        className="w-full text-left px-3 py-2 text-xs border-b last:border-0 hover:bg-[rgba(85,126,255,0.06)]"
                        style={{ borderColor: '#DFE5ED' }}
                      >
                        {c}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            {/* Dirección (full width) */}
            <div className="md:col-span-2 lg:col-span-3">
              <label htmlFor="comprador-direccion" className="text-xs font-semibold mb-1.5 block">
                Dirección
              </label>
              <input
                id="comprador-direccion"
                type="text"
                value={actor.direccion ?? ''}
                onChange={(e) => updateActor(0, { direccion: e.target.value })}
                className={INPUT_BASE}
                style={{ borderColor: '#DFE5ED' }}
              />
            </div>
          </div>
        </section>

        {footer}
       </fieldset>
      </form>
    );
  }

  // ── Layout MULTI (traspaso): un fieldset por actor ────────────────────────
  return (
    <form
      onSubmit={handleSubmit}
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14] mt-4"
      style={{ borderColor: '#DFE5ED' }}
      aria-label="Captura de actores del trámite"
      noValidate
    >
     <fieldset disabled={readOnly} className="contents">
      {/* Embebido en el wizard el título del paso lo pinta la shell (h2); aquí el
          h4 sería un segundo título redundante, así que se omite. */}
      {!embeddedInWizard && (
        <div className="mb-3">
          <h4 className="text-sm font-bold">Actores del trámite</h4>
          <p className="text-[11px] opacity-60">
            {modalidad === 'matricula_inicial'
              ? 'Registra los datos del comprador (propietario inicial).'
              : 'Registra los datos del vendedor y del comprador.'}
          </p>
        </div>
      )}

      {errorBanner}

      <div className="space-y-5">
        {actors.map((actor, index) => {
          const errors = showErrors ? validation.byActor[index] : {};
          const prefix = `actor-${actor.rol}`;
          const runtState: RuntState = runt[index] ?? { status: 'idle' };
          return (
            <fieldset key={actor.rol} className="rounded-xl border p-4" style={{ borderColor: '#DFE5ED' }}>
              <legend className="px-1 text-xs font-bold">{ROL_LABEL[actor.rol]}</legend>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* Tipo de documento */}
                <div>
                  <label htmlFor={`${prefix}-tipoDoc`} className="text-xs font-semibold mb-1.5 block">
                    Tipo de documento
                  </label>
                  <select
                    id={`${prefix}-tipoDoc`}
                    value={actor.tipoDocumento}
                    onChange={(e) => updateActor(index, { tipoDocumento: e.target.value as ActorDocumentType })}
                    className={INPUT_BASE}
                    style={{ borderColor: '#DFE5ED' }}
                  >
                    {DOC_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                </div>

                {/* Número de documento */}
                <div>
                  <label htmlFor={`${prefix}-numeroDoc`} className="text-xs font-semibold mb-1.5 flex items-center gap-1.5">
                    Número de documento
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-numeroDoc`}
                    type="text"
                    value={actor.numeroDocumento}
                    onChange={(e) => updateActor(index, { numeroDocumento: e.target.value })}
                    aria-invalid={!!errors.numeroDocumento}
                    aria-describedby={errors.numeroDocumento ? `${prefix}-numeroDoc-err` : undefined}
                    className={INPUT_BASE}
                    style={{ borderColor: '#DFE5ED' }}
                  />
                  {errors.numeroDocumento && (
                    <p id={`${prefix}-numeroDoc-err`} className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                      {errors.numeroDocumento}
                    </p>
                  )}
                  {/* Consultar RUNT: autopopula el actor por documento. */}
                  {!readOnly && (
                    <button
                      type="button"
                      onClick={() => void handleRuntLookup(index)}
                      disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                      className="mt-2 px-3 py-1.5 rounded-xl text-[11px] font-semibold border disabled:opacity-50"
                      style={{ borderColor: '#557EFF', color: '#557EFF' }}
                    >
                      {runtState.status === 'loading' ? 'Consultando…' : 'Consultar RUNT'}
                    </button>
                  )}
                </div>

                {/* Resultado de la consulta RUNT (autopopulado). */}
                {runt[index] && runt[index].status !== 'idle' && (
                  <div className="md:col-span-2">{runtResult(index)}</div>
                )}

                {/* Nombre completo */}
                <div className="md:col-span-2">
                  <label htmlFor={`${prefix}-nombre`} className="text-xs font-semibold mb-1.5 flex items-center gap-1.5">
                    Nombre completo
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-nombre`}
                    type="text"
                    value={actor.nombreCompleto}
                    onChange={(e) => updateActor(index, { nombreCompleto: e.target.value })}
                    aria-invalid={!!errors.nombreCompleto}
                    aria-describedby={errors.nombreCompleto ? `${prefix}-nombre-err` : undefined}
                    className={INPUT_BASE}
                    style={{ borderColor: '#DFE5ED' }}
                  />
                  {errors.nombreCompleto && (
                    <p id={`${prefix}-nombre-err`} className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                      {errors.nombreCompleto}
                    </p>
                  )}
                </div>

                {/* Email */}
                <div>
                  <label htmlFor={`${prefix}-email`} className="text-xs font-semibold mb-1.5 flex items-center gap-1.5">
                    Correo electrónico
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-email`}
                    type="email"
                    value={actor.email}
                    onChange={(e) => updateActor(index, { email: e.target.value })}
                    aria-invalid={!!errors.email}
                    aria-describedby={errors.email ? `${prefix}-email-err` : undefined}
                    className={INPUT_BASE}
                    style={{ borderColor: '#DFE5ED' }}
                  />
                  {errors.email && (
                    <p id={`${prefix}-email-err`} className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                      {errors.email}
                    </p>
                  )}
                </div>

                {/* Teléfono (opcional) */}
                <div>
                  <label htmlFor={`${prefix}-telefono`} className="text-xs font-semibold mb-1.5 block">
                    Teléfono <span className="opacity-50 font-normal">(opcional)</span>
                  </label>
                  <input
                    id={`${prefix}-telefono`}
                    type="tel"
                    value={actor.telefono ?? ''}
                    onChange={(e) => updateActor(index, { telefono: e.target.value })}
                    className={INPUT_BASE}
                    style={{ borderColor: '#DFE5ED' }}
                  />
                </div>
              </div>
            </fieldset>
          );
        })}
      </div>

      {footer}
     </fieldset>
    </form>
  );
});

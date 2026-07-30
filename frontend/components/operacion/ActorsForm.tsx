'use client';

import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from 'react';
import { AlertTriangle, Search, UserRound } from 'lucide-react';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { Modal } from '@/components/atom/Modal';
import { FineDetailList } from './PreflightPanel';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import type { WizardStepFormHandle } from './wizard-step-form';
import { useProcedureActors } from '@/hooks/useProcedureActors';
import { tramitesClient } from '@/lib/api/tramites-client';
import { filterCiudades } from '@/lib/catalogs/ciudades-co';
import { digitsOnly } from '@/lib/format/currency';
import {
  sanitizeDocNumber,
  validateDocNumber,
  sanitizeName,
  validateReadableName,
} from '@/lib/validation/fieldRules';
import type {
  ActorContactLookupResult,
  ActorDocumentType,
  ActorPersonType,
  ActorRol,
  LegalRepresentativeLookupResult,
  LegalRepresentativeOption,
  MecanismoFirma,
  BiometricEstado,
  ProcedureActor,
  RepresentanteLegal,
  RuesPersonLookupResult,
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
   * propietario registrado que validó el vehículo.
   */
  seedDocumentoFromOwner?: boolean;
  /**
   * Paso vendedor (traspaso): si ya hay número de documento (seed o rehidratación),
   * consulta RUNT al montar, oculta el botón manual y deja el documento en solo lectura.
   * Sin documento: el campo sigue editable y no se dispara consulta.
   */
  autoConsultRunt?: boolean;
  /**
   * FEATURE 05 — `true` si el RNMC aplica a este trámite (el OT destino lo exige y no está
   * inhabilitado para la compañía). Solo entonces se muestra la fecha de expedición del documento
   * (necesaria para consultar el RNMC y generar el certificado). Ausente/false ⇒ el campo se oculta.
   */
  rnmcEnabled?: boolean;
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

const PERSON_TYPE_OPTIONS: { value: ActorPersonType; label: string }[] = [
  { value: 'natural', label: 'Persona natural' },
  { value: 'juridical', label: 'Persona jurídica' },
];

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
    // Por defecto persona natural: caso común (compraventa entre particulares); el
    // gestor puede cambiar a jurídica. HU #10543.
    personType: 'natural',
  };
}

// Validación de email pragmática (no exhaustiva): algo@algo.dominio.
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// Fecha de expedición del documento (RNMC): se persiste en DD/MM/YYYY (contrato del RNMC), pero el
// input nativo <input type="date"> usa YYYY-MM-DD. Estos helpers convierten entre ambos formatos.
function dmyToInput(dmy?: string | null): string {
  if (!dmy) return '';
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(dmy.trim());
  return m ? `${m[3]}-${m[2]}-${m[1]}` : '';
}
function inputToDmy(iso?: string | null): string {
  if (!iso) return '';
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso.trim());
  return m ? `${m[3]}/${m[2]}/${m[1]}` : '';
}

/** Errores por actor, indexados por campo. Vacío = sin errores. */
export type ActorErrors = Partial<Record<keyof ProcedureActor, string>>;

export interface ActorsValidation {
  valid: boolean;
  /** Errores por actor en el mismo orden del arreglo. */
  byActor: ActorErrors[];
}

/**
 * Valida requeridos + formato de email + (traspaso) vendedor≠comprador por DOCUMENTO.
 * El correo compartido entre las partes no bloquea desde la HU #11019.
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
    // HU #10688 (Fase 1): en persona jurídica el correo del representante legal es obligatorio
    // (es quien valida la identidad de la PJ). Nombre/documento del RL siguen opcionales.
    if (isJuridical(a)) {
      const rlEmail = a.representanteLegal?.email?.trim() ?? '';
      if (!rlEmail) e.representanteLegal = 'Correo del representante legal requerido';
      else if (!EMAIL_RE.test(rlEmail))
        e.representanteLegal = 'Correo del representante legal no válido';
    }
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
      // HU #11019 — el CORREO COMPARTIDO ya no bloquea: es legítimo que ambas partes usen el mismo
      // buzón (una empresa que gestiona por su contacto, un familiar que recibe por los dos). Lo que
      // sigue prohibido es el mismo DOCUMENTO: ahí sí serían la misma persona.
      if (sameDoc) {
        const ci = actors.indexOf(comprador);
        byActor[ci].numeroDocumento =
          'El vendedor y el comprador no pueden tener el mismo número de documento.';
      }
    }
  }

  const valid = byActor.every((e) => Object.keys(e).length === 0);
  return { valid, byActor };
}

/** Normaliza opcionales vacíos a undefined antes de persistir. */
function normalizeActors(actors: ProcedureActor[]): ProcedureActor[] {
  const blankToUndef = (v?: string) => (v?.trim() ? v.trim() : undefined);
  return actors.map((a) => {
    // HU #10956 (AC1) — el check de reutilización desapareció: el campo NUNCA viaja en el PUT,
    // ni siquiera si un actor persistido ANTES de esta HU lo trae en `true` desde el backend.
    const { autorizaReutilizacionDatos: _legacyConsent, ...rest } = a;
    return {
      ...rest,
      telefono: blankToUndef(a.telefono),
      ciudad: blankToUndef(a.ciudad),
      direccion: blankToUndef(a.direccion),
    };
  });
}

const INPUT_BASE =
  'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF] aria-[invalid=true]:border-[#FF4E00]';

const GRADIENT = 'linear-gradient(135deg,#557EFF,#00DBD5)';

/**
 * Estado por actor de la consulta de identidad (autopopulado). Bifurca por tipo de persona:
 * natural → RUNT (conductor), jurídica → RUES (por NIT). El `found` distingue la forma del
 * resultado con `kind`.
 */
type LookupState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'found'; kind: 'runt'; result: RuntPersonLookupResult }
  | { status: 'found'; kind: 'rues'; result: RuesPersonLookupResult }
  // HU #10906 — precarga desde el directorio del tenant por NIT (corta RUES/RUNT). El resultado
  // trae la compañía representada + representante + banderas de firma/identidad vigentes.
  | { status: 'found'; kind: 'preload'; result: LegalRepresentativeLookupResult }
  | { status: 'not_found' }
  | { status: 'error'; message: string };

/**
 * HU #10956 (AC2/AC3/AC4/AC5) — estado por actor de la precarga de datos de CONTACTO (ciudad,
 * correo, dirección, teléfono), disparada tras resolver la identidad del actor (RUNT/RUES/
 * directorio). `found`/`empty` distinguen si la persona tenía antecedentes de contacto en el
 * tenant (AC4: sin antecedentes, campos vacíos, sin error). `error` nunca bloquea: los 4 campos
 * quedan editables igual (degradación silenciosa, AC5).
 */
type ContactLookupState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'found' }
  | { status: 'empty' }
  | { status: 'error'; message: string };

/** Campos de contacto sujetos a precarga (AC2) y a la protección "no pisar" del operador (AC3). */
type ContactField = 'ciudad' | 'email' | 'direccion' | 'telefono';
const CONTACT_FIELDS: readonly ContactField[] = ['ciudad', 'email', 'direccion', 'telefono'];

/** Nombre completo del representante legal a partir de sus partes (omite vacíos). */
function repFullName(rep: {
  nombres: string;
  primerApellido: string;
  segundoApellido?: string | null;
}): string {
  return [rep.nombres, rep.primerApellido, rep.segundoApellido]
    .map((s) => s?.trim() ?? '')
    .filter((s) => s !== '')
    .join(' ')
    .trim();
}

/**
 * Representantes seleccionables del resultado de precarga (HU #10937). Usa la lista `representantes`
 * si viene; si no (contrato previo), construye una lista de un solo elemento a partir del
 * representante primario. Así el selector funciona con ambos contratos.
 */
function repsOf(result: LegalRepresentativeLookupResult): LegalRepresentativeOption[] {
  if (result.representantes?.length) return result.representantes;
  const r = result.representante;
  return [
    {
      tipoDoc: r.tipoDoc,
      documento: r.documento,
      nombres: r.nombres,
      primerApellido: r.primerApellido,
      segundoApellido: r.segundoApellido,
      email: r.email,
      telefono: r.telefono,
      firmaVigente: result.firmaVigente,
      identidadVigente: result.identidadVigente,
    },
  ];
}

/** Tipos de documento del representante legal: persona natural (excluye NIT). */
const RL_DOC_OPTIONS = DOC_OPTIONS.filter((o) => o.value !== 'NIT');

/** ¿El actor debe consultarse como persona jurídica (RUES)? Jurídica explícita o documento NIT. */
function isJuridical(actor: ProcedureActor): boolean {
  return actor.personType === 'juridical' || actor.tipoDocumento === 'NIT';
}

/**
 * HU #11014 — deriva el tipo de persona del DOCUMENTO. El paso 1 siembra el documento del propietario
 * (que en una empresa es un NIT) pero el actor nacía siempre 'natural', así que el paso del vendedor no
 * cambiaba a persona jurídica ni pedía el representante legal. Con NIT el tipo es jurídica, sin
 * excepción: el selector solo permite volver a natural cambiando también el documento (NIT → CC).
 */
function withDerivedPersonType(a: ProcedureActor): ProcedureActor {
  return a.tipoDocumento === 'NIT' && a.personType !== 'juridical'
    ? { ...a, personType: 'juridical' }
    : a;
}

// ── HU #10886 — aviso de reenvío al editar el correo del sujeto de identidad ────────────────────
// El backend (HU #10880) reenvía la validación de identidad y expira el enlace anterior cuando
// cambia el correo del SUJETO de identidad de una parte que ya tenía una validación vigente: en
// persona natural el sujeto es el propio actor; en jurídica es el representante legal (mismo
// criterio que `IdentitySubjectResolver` del backend). Antes de persistir, el front detecta ese
// cambio y pide confirmación explícita (AC1); sin confirmación no se envía el PUT.

/** Correo del sujeto de identidad de un actor: RL en jurídica, el propio actor en natural. */
function subjectEmail(actor: ProcedureActor): string {
  return (isJuridical(actor) ? actor.representanteLegal?.email : actor.email)?.trim() ?? '';
}

/** Documento del sujeto de identidad de un actor: RL en jurídica, el propio actor en natural. */
function subjectDocument(actor: ProcedureActor): { tipo?: string; numero?: string } {
  return isJuridical(actor)
    ? { tipo: actor.representanteLegal?.tipoDocumento, numero: actor.representanteLegal?.numeroDocumento }
    : { tipo: actor.tipoDocumento, numero: actor.numeroDocumento };
}

/**
 * Estados en los que una validación de identidad se considera "con envío vigente" a efectos del
 * aviso: incluye `aprobado` porque el backend igualmente la expira y reenvía si el correo cambia
 * (espejo de `ResendIdentityOnEmailChangeAsync`). `rechazado`/`expirado` quedan fuera: no hay nada
 * vigente que invalidar.
 */
const ACTIVE_IDENTITY_STATUSES: readonly BiometricEstado[] = [
  'pendiente_envio',
  'enviado',
  'en_proceso',
  'aprobado',
];

/** Info mínima para el modal de confirmación: a quién afecta y a qué correo se reenviará. */
interface EmailChangeConfirmInfo {
  rol: ActorRol;
  roleLabel: string;
  newEmail: string;
}

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
    autoConsultRunt = false,
    rnmcEnabled = false,
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
  // Estado de la consulta de identidad por índice de actor (RUNT o RUES, autopoblado).
  const [runt, setRunt] = useState<Record<number, LookupState>>({});
  // Estado de la consulta RUNT del representante legal por índice de actor jurídico.
  const [rlRunt, setRlRunt] = useState<Record<number, LookupState>>({});
  // HU #10937 — representante ELEGIDO (índice en la lista precargada) por índice de actor jurídico,
  // cuando la compañía tiene varios. Default 0 (el primario). Gobierna qué representante se precarga y
  // firma, y las banderas mostradas.
  const [selectedRepIdx, setSelectedRepIdx] = useState<Record<number, number>>({});
  // Autocomplete de ciudad por índice de actor.
  const [ciudadOpen, setCiudadOpen] = useState<Record<number, boolean>>({});
  // Fecha de expedición del documento (RNMC) por índice de actor, en formato de input (YYYY-MM-DD).
  // Se persiste como field value `{rol}_document_issue_date` en DD/MM/YYYY al guardar.
  const [issueDates, setIssueDates] = useState<Record<number, string>>({});
  // Evita doble auto-consulta RUNT para el mismo documento en el mismo montaje.
  const autoLookupTriggeredRef = useRef<string | null>(null);

  // HU #10956 — precarga de datos de contacto por índice de actor (AC2/AC4/AC5) + campos que el
  // operador ya editó a mano (AC3: la precarga nunca los pisa). Se dispara UNA vez, de forma
  // imperativa, al final de `handleIdentityLookup` (justo cuando la identidad se resuelve) — NO
  // como efecto reactivo sobre `actors`, precisamente para no crear un bucle de re-precarga.
  const [contactLookup, setContactLookup] = useState<Record<number, ContactLookupState>>({});
  const [touchedContact, setTouchedContact] = useState<Record<number, Set<ContactField>>>({});
  // Refs con el valor más reciente de `actors`/`touchedContact`: `runContactLookup` es async y debe
  // leer el estado vigente al momento de aplicar el resultado, no el capturado al iniciar la consulta
  // (el operador puede escribir mientras la precarga de contacto sigue en vuelo).
  const actorsRef = useRef(actors);
  useEffect(() => {
    actorsRef.current = actors;
  }, [actors]);
  const touchedContactRef = useRef(touchedContact);
  useEffect(() => {
    touchedContactRef.current = touchedContact;
  }, [touchedContact]);

  const markContactTouched = (index: number, field: ContactField) => {
    setTouchedContact((prev) => {
      const next = new Set(prev[index] ?? []);
      next.add(field);
      return { ...prev, [index]: next };
    });
  };

  // Aplica el resultado de la precarga de contacto SIN pisar (AC3): omite un campo si el operador ya
  // lo marcó como editado, o si ya tiene un valor no vacío (defensa adicional ante una carrera entre
  // la edición manual y la respuesta async del lookup).
  const applyContactLookup = (index: number, result: ActorContactLookupResult) => {
    const touched = touchedContactRef.current[index] ?? new Set<ContactField>();
    const current = actorsRef.current[index];
    if (!current) return;
    const patch: Partial<ProcedureActor> = {};
    for (const field of CONTACT_FIELDS) {
      if (touched.has(field)) continue;
      if ((current[field] ?? '').toString().trim()) continue;
      const value = result[field];
      if (value && value.trim()) patch[field] = value.trim();
    }
    if (Object.keys(patch).length > 0) updateActor(index, patch);
  };

  // Dispara el lookup de contacto (HU #10956, AC2) tras resolver la identidad del actor. Nunca
  // bloquea: un error solo se refleja como aviso no intrusivo (AC5), la captura manual sigue posible.
  const runContactLookup = async (
    index: number,
    tipoDocumento: ActorDocumentType,
    numeroDocumento: string,
  ) => {
    const numero = numeroDocumento.trim();
    if (!numero) return;
    setContactLookup((prev) => ({ ...prev, [index]: { status: 'loading' } }));
    try {
      const result = await tramitesClient.actorContactLookup({ tipoDocumento, numeroDocumento: numero });
      applyContactLookup(index, result);
      const hasData = CONTACT_FIELDS.some((f) => !!result[f]?.trim());
      setContactLookup((prev) => ({ ...prev, [index]: { status: hasData ? 'found' : 'empty' } }));
    } catch (err) {
      setContactLookup((prev) => ({
        ...prev,
        [index]: {
          status: 'error',
          message: err instanceof Error ? err.message : 'Error consultando contacto',
        },
      }));
    }
  };

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
      ? withDerivedPersonType({
          ...a,
          numeroDocumento: ownerSeed.numero,
          tipoDocumento: ownerSeed.tipo,
        })
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
        // HU #11014 — al rehidratar desde el backend también se deriva el tipo de persona: un actor
        // persistido con NIT y personType 'natural' (creado antes de esta corrección) se corrige solo.
        return withOwnerSeed(
          found ? withDerivedPersonType({ ...emptyActor(rol), ...found }) : emptyActor(rol),
        );
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

  // Siembra la fecha de expedición (RNMC) de cada actor desde los field_values persistidos
  // (`{rol}_document_issue_date`, DD/MM/YYYY → input YYYY-MM-DD). Best-effort.
  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    tramitesClient
      .getInstance(instanceId)
      .then((detail) => {
        if (!active || !detail?.fieldValues) return;
        const seed: Record<number, string> = {};
        roles.forEach((rol, i) => {
          const dmy = detail.fieldValues.find(
            (f) => f.fieldKey === `${rol}_document_issue_date`,
          )?.valueText;
          const iso = dmyToInput(dmy);
          if (iso) seed[i] = iso;
        });
        if (Object.keys(seed).length > 0) setIssueDates((prev) => ({ ...seed, ...prev }));
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId, roles]);

  // Persiste las fechas de expedición (RNMC) de los actores persona natural como field_values
  // `{rol}_document_issue_date` en DD/MM/YYYY. Best-effort: un fallo no bloquea el guardado (RNMC
  // no es bloqueante) — el preflight tratará la fecha ausente como dato no crítico.
  const persistIssueDates = async () => {
    if (!instanceId) return;
    const items = actors.flatMap((a, i) => {
      if (isJuridical(a)) return [];
      const dmy = inputToDmy(issueDates[i]);
      return dmy
        ? [{ formFieldId: null, fieldKey: `${a.rol}_document_issue_date`, valueText: dmy }]
        : [];
    });
    if (items.length === 0) return;
    try {
      await tramitesClient.patchFieldValues(instanceId, items);
    } catch {
      // RNMC no es bloqueante: no propagamos el error de persistencia de la fecha.
    }
  };

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
        if (patch.telefono !== undefined)
          next.telefono = digitsOnly(next.telefono ?? '');
        if (patch.nombreCompleto !== undefined)
          next.nombreCompleto = sanitizeName(next.nombreCompleto);
        return next;
      }),
    );
  };

  const setRuntFor = (index: number, value: LookupState) =>
    setRunt((prev) => ({ ...prev, [index]: value }));

  const setRlRuntFor = (index: number, value: LookupState) =>
    setRlRunt((prev) => ({ ...prev, [index]: value }));

  // Actualiza el representante legal (persona jurídica) de un actor, embebido en el propio actor.
  const updateRepLegal = (index: number, patch: Partial<RepresentanteLegal>) =>
    setActors((prev) =>
      prev.map((a, i) => {
        if (i !== index) return a;
        const rl = { ...a.representanteLegal, ...patch };
        if (patch.numeroDocumento !== undefined || patch.tipoDocumento !== undefined) {
          rl.numeroDocumento = sanitizeDocNumber(
            rl.numeroDocumento ?? '',
            rl.tipoDocumento ?? 'CC',
          );
        }
        if (patch.telefono !== undefined) rl.telefono = digitsOnly(rl.telefono ?? '');
        return { ...a, representanteLegal: rl };
      }),
    );

  // HU #10937 — precarga en el actor el representante ELEGIDO (su documento + contacto). El actor
  // guarda este representante embebido; el backend firma/valida identidad por SU documento.
  const applySelectedRep = (index: number, rep: LegalRepresentativeOption) =>
    updateRepLegal(index, {
      tipoDocumento: (rep.tipoDoc as ActorDocumentType) || 'CC',
      numeroDocumento: rep.documento,
      nombreCompleto: repFullName(rep),
      email: rep.email ?? undefined,
      telefono: rep.telefono ?? undefined,
    });

  // Cambia el representante elegido del actor jurídico (selector cuando la compañía tiene varios) y
  // reprecarga sus datos. Las banderas mostradas se derivan del representante elegido.
  const handleSelectRep = (index: number, repIdx: number) => {
    const lookup = runt[index];
    if (!lookup || lookup.status !== 'found' || lookup.kind !== 'preload') return;
    const rep = repsOf(lookup.result)[repIdx];
    if (!rep) return;
    setSelectedRepIdx((prev) => ({ ...prev, [index]: repIdx }));
    applySelectedRep(index, rep);
  };

  // Consulta de identidad por documento. Bifurca por tipo de persona: jurídica → RUES (por NIT),
  // natural → RUNT (conductor). Si encuentra, autopopula el actor. Nunca bloquea la captura
  // manual: not_found/error dejan los campos editables (registro sin resultado de consulta).
  const handleIdentityLookup = async (index: number) => {
    const actor = actors[index];
    const documentNumber = actor.numeroDocumento.trim();
    if (!instanceId || !documentNumber || runt[index]?.status === 'loading') {
      return;
    }
    setRuntFor(index, { status: 'loading' });
    try {
      if (isJuridical(actor)) {
        // HU #10906 (R3) — precarga por NIT desde el directorio del tenant ANTES de consultar RUES:
        // si el NIT coincide con un representante registrado del tenant, se precargan razón social +
        // datos del representante legal y se CORTA la consulta externa (siempre que haya match, sin
        // importar si es comprador o vendedor). Sin match (404 → null) ⇒ flujo RUES normal.
        const preload = await tramitesClient.lookupLegalRepresentativeByNit(documentNumber);
        if (preload) {
          const nit = preload.company.nit || documentNumber;
          updateActor(index, {
            nombreCompleto: preload.company.razonSocial,
            tipoDocumento: 'NIT',
            numeroDocumento: nit,
          });
          // HU #10937 — si la compañía tiene varios representantes, se precarga el primero por defecto
          // y el gestor puede cambiarlo con el selector (handleSelectRep). Con uno solo, comportamiento
          // previo (auto-seleccionado). El representante elegido queda embebido en el actor.
          const reps = repsOf(preload);
          setSelectedRepIdx((prev) => ({ ...prev, [index]: 0 }));
          if (reps[0]) applySelectedRep(index, reps[0]);
          setRuntFor(index, { status: 'found', kind: 'preload', result: preload });
          // HU #10956 (AC2) — identidad resuelta (match del directorio): precarga el contacto conocido.
          void runContactLookup(index, 'NIT', nit);
          return;
        }

        const result = await tramitesClient.ruesPersonLookup(instanceId, {
          documentNumber,
        });
        if (result.found) {
          const nit = result.documentNumber || actor.numeroDocumento;
          updateActor(index, {
            nombreCompleto: result.razonSocial ?? actor.nombreCompleto,
            tipoDocumento: 'NIT',
            numeroDocumento: nit,
          });
          setRuntFor(index, { status: 'found', kind: 'rues', result });
          // HU #10956 (AC2) — identidad resuelta en RUES: precarga el contacto conocido.
          void runContactLookup(index, 'NIT', nit);
        } else {
          setRuntFor(index, { status: 'not_found' });
        }
        return;
      }

      const result = await tramitesClient.runtPersonLookup(instanceId, {
        documentType: actor.tipoDocumento,
        documentNumber,
      });
      if (result.found) {
        const resolvedTipo = (result.documentType as ActorDocumentType) || actor.tipoDocumento;
        const resolvedNumero = result.documentNumber || actor.numeroDocumento;
        updateActor(index, {
          nombreCompleto: result.fullName ?? actor.nombreCompleto,
          tipoDocumento: resolvedTipo,
          numeroDocumento: resolvedNumero,
        });
        setRuntFor(index, { status: 'found', kind: 'runt', result });
        // HU #10956 (AC2) — identidad resuelta en RUNT: precarga el contacto conocido.
        void runContactLookup(index, resolvedTipo, resolvedNumero);
      } else {
        setRuntFor(index, { status: 'not_found' });
      }
    } catch (err) {
      setRuntFor(index, {
        status: 'error',
        message: err instanceof Error ? err.message : 'Error consultando identidad',
      });
    }
  };

  // Consulta RUNT del representante legal (persona natural) de un actor jurídico. Autopobla el
  // nombre del RL; nunca bloquea la captura manual.
  const handleRlLookup = async (index: number) => {
    const rl = actors[index].representanteLegal;
    const documentNumber = rl?.numeroDocumento?.trim();
    const documentType = rl?.tipoDocumento ?? 'CC';
    if (!instanceId || !documentNumber || rlRunt[index]?.status === 'loading') {
      return;
    }
    setRlRuntFor(index, { status: 'loading' });
    try {
      const result = await tramitesClient.runtPersonLookup(instanceId, {
        documentType,
        documentNumber,
      });
      if (result.found) {
        updateRepLegal(index, {
          nombreCompleto: result.fullName ?? rl?.nombreCompleto,
          tipoDocumento:
            (result.documentType as ActorDocumentType) || documentType,
          numeroDocumento: result.documentNumber || documentNumber,
        });
        setRlRuntFor(index, { status: 'found', kind: 'runt', result });
      } else {
        setRlRuntFor(index, { status: 'not_found' });
      }
    } catch (err) {
      setRlRuntFor(index, {
        status: 'error',
        message: err instanceof Error ? err.message : 'Error consultando RUNT',
      });
    }
  };

  // Paso vendedor: dispara la consulta en cuanto el documento está disponible (sembrado desde el
  // paso 1 o rehidratado del backend), sin clic manual. HU #10906 — el cortocircuito de precarga por
  // NIT vive dentro de handleIdentityLookup (rama jurídica): al delegar aquí, un vendedor con NIT en
  // el directorio del tenant se precarga SIN consultar RUES/RUNT; uno natural cae a RUNT como antes.
  useEffect(() => {
    if (!autoConsultRunt || !instanceId || readOnly || !isSplit || actors.length !== 1) {
      return;
    }
    const actor = actors[0];
    const documentNumber = actor.numeroDocumento.trim();
    if (!documentNumber) return;
    const runtState = runt[0] ?? { status: 'idle' };
    if (runtState.status !== 'idle') return;
    const lookupKey = `${actor.tipoDocumento}:${documentNumber}`;
    if (autoLookupTriggeredRef.current === lookupKey) return;
    autoLookupTriggeredRef.current = lookupKey;
    void handleIdentityLookup(0);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- handleIdentityLookup lee actors[0] actual
  }, [
    autoConsultRunt,
    instanceId,
    readOnly,
    isSplit,
    actors[0]?.numeroDocumento,
    actors[0]?.tipoDocumento,
    runt[0]?.status,
  ]);

  // ── HU #10886 (AC1) — aviso de reenvío al cambiar el correo del sujeto de identidad ────────────
  // Modal de confirmación pendiente (null = cerrado) + resolver de la promesa que bloquea
  // submitActors() hasta que el operador confirme/cancele.
  const [emailChangeConfirm, setEmailChangeConfirm] = useState<EmailChangeConfirmInfo[] | null>(null);
  const emailChangeResolverRef = useRef<((confirmed: boolean) => void) | null>(null);

  const requestEmailChangeConfirm = (changes: EmailChangeConfirmInfo[]): Promise<boolean> =>
    new Promise((resolve) => {
      emailChangeResolverRef.current = resolve;
      setEmailChangeConfirm(changes);
    });

  const resolveEmailChangeConfirm = (confirmed: boolean) => {
    setEmailChangeConfirm(null);
    emailChangeResolverRef.current?.(confirmed);
    emailChangeResolverRef.current = null;
  };

  /**
   * ¿La validación biométrica corresponde al rol dado? Espejo del filtro de `BiometricStep`: en
   * matrícula (única parte) el backend puede dejar `partyRole` en null.
   */
  const matchesRol = (partyRole: string | null, rol: ActorRol) =>
    modalidad === 'traspaso' ? partyRole === rol : partyRole === null || partyRole === 'comprador';

  /**
   * Detecta, ANTES de persistir, qué partes cambiaron el correo de su sujeto de identidad
   * respecto al último valor guardado (`state.actors`) Y tienen una validación de identidad
   * vigente (no rechazada/expirada) para el documento previamente persistido. Solo esas partes
   * disparan el aviso de confirmación (AC1); correo igual → lista vacía, sin llamada de red.
   */
  const detectEmailChangesWithActiveValidation = async (
    updated: ProcedureActor[],
  ): Promise<EmailChangeConfirmInfo[]> => {
    const persisted = state.actors ?? [];
    const candidates = updated.flatMap((a) => {
      const prev = persisted.find((p) => p.rol === a.rol);
      if (!prev) return [];
      const prevEmail = subjectEmail(prev).toLowerCase();
      const newEmail = subjectEmail(a).toLowerCase();
      if (!prevEmail || !newEmail || prevEmail === newEmail) return [];
      return [{ rol: a.rol, newEmail: subjectEmail(a), prevDoc: subjectDocument(prev) }];
    });
    if (candidates.length === 0 || !instanceId) return [];

    try {
      const { validations } = await tramitesClient.getBiometricState(instanceId);
      return candidates
        .filter(({ rol, prevDoc }) =>
          validations.some(
            (v) =>
              matchesRol(v.partyRole, rol) &&
              ACTIVE_IDENTITY_STATUSES.includes(v.status) &&
              !!prevDoc.tipo &&
              !!prevDoc.numero &&
              v.documentType === prevDoc.tipo &&
              v.documentNumber === prevDoc.numero?.trim(),
          ),
        )
        .map(({ rol, newEmail }) => ({ rol, roleLabel: ROL_LABEL[rol], newEmail }));
    } catch {
      // Best-effort: si no se puede consultar el estado biométrico, no se bloquea el guardado (el
      // aviso previo es una ayuda informativa, no un gate duro — el backend reenvía igual si aplica).
      return [];
    }
  };

  // Valida + guarda. Núcleo compartido por el submit propio y el save() del ref.
  const submitActors = async (): Promise<boolean> => {
    setShowErrors(true);
    if (!validateActors(actors, modalidad).valid) return false;
    const normalized = normalizeActors(actors);

    // AC1 — correo cambiado con validación en curso: pide confirmación antes de persistir. Sin
    // confirmación explícita, NO se envía el PUT.
    const changes = await detectEmailChangesWithActiveValidation(normalized);
    if (changes.length > 0) {
      const confirmed = await requestEmailChangeConfirm(changes);
      if (!confirmed) return false;
    }

    const ok = await save(normalized);
    if (ok) {
      // Fecha de expedición (RNMC) — persistencia best-effort tras guardar los actores.
      await persistIssueDates();
      onSaved?.(normalized);
    }
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

  // ── Selector de tipo de persona (HU #10543) ───────────────────────────────
  // Persona natural: el documento de identidad se incorpora desde la validación
  // biométrica, por lo que el checklist no ofrece la carga manual de cédula.
  const personTypeSelector = (index: number) => {
    const current = actors[index].personType ?? 'natural';
    return (
      <div>
        <span className="text-xs font-semibold mb-1.5 block">Tipo de persona</span>
        <div
          className="inline-flex rounded-xl border p-0.5 gap-0.5"
          role="group"
          aria-label="Tipo de persona"
        >
          {PERSON_TYPE_OPTIONS.map((o) => {
            const active = current === o.value;
            return (
              <button
                key={o.value}
                type="button"
                onClick={() => {
                  // Jurídica ⇒ documento NIT (RUES). Volver a natural desde NIT ⇒ CC por defecto.
                  const patch: Partial<ProcedureActor> = { personType: o.value };
                  if (o.value === 'juridical') patch.tipoDocumento = 'NIT';
                  else if (actors[index].tipoDocumento === 'NIT') patch.tipoDocumento = 'CC';
                  updateActor(index, patch);
                }}
                aria-pressed={active}
                className={`px-3 py-1.5 rounded-[10px] text-xs font-semibold transition-colors ${
                  active ? 'text-white' : 'opacity-70'
                }`}
                style={active ? { background: GRADIENT } : undefined}
              >
                {o.label}
              </button>
            );
          })}
        </div>
        {current === 'natural' && (
          <p className="text-[10px] mt-1 opacity-60">
            La cédula se toma de la validación de identidad; no se carga manualmente.
          </p>
        )}
      </div>
    );
  };

  // ── AC1 (HU #10885, Feature #10862, CF-04) — badge de origen cuando el lookup de persona se
  // sirvió desde la caché de reúso cross-trámite (ADR-0030) en vez de llamar al proveedor externo.
  // El backend (RuntPersonLookupHandler/RuesPersonLookupHandler) señaliza el reúso con
  // `mode: 'cache'`; NO expone fecha de la consulta origen para este lookup (gap de contrato
  // documentado — a diferencia de `ConsultationResult.fromCache/queriedAt` del flujo de vehículo).
  const originBadge = (mode: string, source: string) =>
    mode === 'cache' && (
      <span
        className="rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase"
        style={{ background: 'rgba(85,126,255,0.15)', color: '#557EFF' }}
      >
        Dato reutilizado · {source}
      </span>
    );

  // ── Bloque de resultado de la consulta (RUNT o RUES, compartido entre layouts) ─────────────
  const runtResult = (index: number) => {
    const runtState: LookupState = runt[index] ?? { status: 'idle' };
    const actor = actors[index];
    const channel = actor && isJuridical(actor) ? 'RUES' : 'RUNT';
    if (runtState.status === 'loading') {
      return (
        <p className="text-xs opacity-70" role="status" aria-live="polite">
          Consultando {channel}…
        </p>
      );
    }
    if (runtState.status === 'found' && runtState.kind === 'preload') {
      // HU #10906/#10937 — precarga desde el directorio de la compañía (NO se consultó RUES/RUNT). Copy
      // honesto + selector de representante cuando hay varios + badges de firma/identidad vigentes del
      // representante ELEGIDO, con los tonos unificados de StatusBadge.
      const { company } = runtState.result;
      const reps = repsOf(runtState.result);
      const sel = selectedRepIdx[index] ?? 0;
      const rep = reps[sel] ?? reps[0];
      const firmaVigente = rep?.firmaVigente ?? false;
      const identidadVigente = rep?.identidadVigente ?? false;
      return (
        <div className="space-y-2" role="status" aria-live="polite">
          <div
            className="rounded-xl p-3 text-xs border"
            style={{ borderColor: '#8CC63F', background: 'rgba(140,198,63,0.08)' }}
          >
            <p className="font-semibold mb-2 flex items-center gap-1.5" style={{ color: '#5a8a1f' }}>
              <span aria-hidden="true">✓</span>
              Precargado desde el directorio de la compañía
            </p>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1.5">
              <div className="col-span-2">
                <span className="opacity-60 font-normal">Razón social: </span>
                <span className="font-semibold" style={{ color: '#162744' }}>
                  {company.razonSocial}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">NIT: </span>
                <span className="font-semibold font-mono" style={{ color: '#162744' }}>
                  {company.nit}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">Representante: </span>
                <span className="font-semibold" style={{ color: '#162744' }}>
                  {rep ? repFullName(rep) || rep.documento : '—'}
                </span>
              </div>
            </div>
            {/* HU #10937 — selector de representante cuando la compañía tiene más de uno. El elegido
                se precarga y firma con su información. */}
            {reps.length > 1 && (
              <div className="mt-2">
                <label
                  htmlFor={`${index}-rep-select`}
                  className="opacity-60 font-normal block mb-1"
                >
                  Representante legal que firma
                </label>
                <select
                  id={`${index}-rep-select`}
                  value={sel}
                  disabled={readOnly}
                  onChange={(e) => handleSelectRep(index, Number(e.target.value))}
                  className={INPUT_BASE}
                >
                  {reps.map((r, i) => (
                    <option key={`${r.tipoDoc}-${r.documento}`} value={i}>
                      {`${repFullName(r) || r.documento} · ${r.tipoDoc} ${r.documento}`}
                    </option>
                  ))}
                </select>
              </div>
            )}
            <div className="mt-2 flex flex-wrap gap-2">
              <StatusBadge
                tone={firmaVigente ? 'success' : 'neutral'}
                label={firmaVigente ? 'Firma vigente' : 'Sin firma vigente'}
              />
              <StatusBadge
                tone={identidadVigente ? 'success' : 'neutral'}
                label={identidadVigente ? 'Identidad vigente' : 'Sin identidad vigente'}
              />
            </div>
            {/* HU #11061 — con los DOS mecanismos vigentes el gestor elige con cuál se registra el
                trámite. Con uno solo no se pregunta: se usa el que hay. Sin ninguno el flujo
                continúa y los badges de arriba ya dicen que no hay firma que plasmar. */}
            {firmaVigente && identidadVigente && (
              <div className="mt-2">
                <label
                  htmlFor={`${index}-mecanismo-firma`}
                  className="opacity-60 font-normal block mb-1"
                >
                  Firma con la que se registra el trámite
                </label>
                <select
                  id={`${index}-mecanismo-firma`}
                  value={actors[index]?.representanteLegal?.mecanismoFirma ?? 'baul'}
                  disabled={readOnly}
                  onChange={(e) =>
                    updateRepLegal(index, {
                      mecanismoFirma: e.target.value as MecanismoFirma,
                    })
                  }
                  className={INPUT_BASE}
                >
                  <option value="baul">Firma del baúl</option>
                  <option value="identidad">Sello de validación de identidad</option>
                </select>
              </div>
            )}
          </div>
        </div>
      );
    }
    if (runtState.status === 'found' && runtState.kind === 'rues') {
      const r = runtState.result;
      const activa = (r.estado ?? '').toUpperCase() === 'ACTIVA';
      return (
        <div className="space-y-2" role="status" aria-live="polite">
          <div
            className="rounded-xl p-3 text-xs border"
            style={{ borderColor: '#8CC63F', background: 'rgba(140,198,63,0.08)' }}
          >
            <p className="font-semibold mb-2 flex items-center gap-1.5 flex-wrap" style={{ color: '#5a8a1f' }}>
              <span aria-hidden="true">✓</span>
              Empresa encontrada en RUES
              {originBadge(r.mode, 'RUES')}
            </p>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1.5">
              <div className="col-span-2">
                <span className="opacity-60 font-normal">Razón social: </span>
                <span className="font-semibold" style={{ color: '#162744' }}>
                  {r.razonSocial ?? '—'}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">NIT: </span>
                <span className="font-semibold font-mono" style={{ color: '#162744' }}>
                  {r.documentNumber}
                </span>
              </div>
              <div>
                <span className="opacity-60 font-normal">Estado: </span>
                <span
                  className="font-semibold"
                  style={{ color: activa ? '#5a8a1f' : '#FF4E00' }}
                >
                  {r.estado ?? '—'}
                </span>
              </div>
              {r.camaraComercio && (
                <div className="col-span-2">
                  <span className="opacity-60 font-normal">Cámara de comercio: </span>
                  <span className="font-semibold" style={{ color: '#162744' }}>
                    {r.camaraComercio}
                  </span>
                </div>
              )}
            </div>
          </div>
        </div>
      );
    }
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
            <p className="font-semibold mb-2 flex items-center gap-1.5 flex-wrap" style={{ color: '#5a8a1f' }}>
              <span aria-hidden="true">✓</span>
              Persona encontrada en RUNT
              {originBadge(r.mode, 'RUNT')}
            </p>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-x-4 gap-y-1.5">
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

          {/* Card B — Multas. Con multas pendientes, bajo la alerta se lista el detalle de cada
              comparendo (SIMIT best-effort); si el detalle no llegó, se conserva solo la alerta. */}
          {r.hasPendingFines !== undefined && (
            <div
              className="rounded-xl p-3 text-xs border"
              style={
                r.hasPendingFines
                  ? { borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }
                  : { borderColor: '#8CC63F', background: 'rgba(140,198,63,0.06)', color: '#5a8a1f' }
              }
              role="status"
            >
              <span className="flex items-center gap-2">
                <span aria-hidden="true">{r.hasPendingFines ? '⚠' : '⊙'}</span>
                <span className="font-semibold">
                  {r.hasPendingFines
                    ? 'ALERTA: Comparendos/Multas pendientes'
                    : `Sin multas ni comparendos pendientes${r.nroPazYSalvo ? ` · Paz y Salvo ${r.nroPazYSalvo}` : ''}`}
                </span>
              </span>
              {r.hasPendingFines && r.fines && r.fines.length > 0 && (
                <FineDetailList details={r.fines} />
              )}
            </div>
          )}
        </div>
      );
    }
    if (runtState.status === 'not_found') {
      return (
        <div
          className="rounded-xl p-3 text-[11px] border opacity-80"
          role="status"
          aria-live="polite"
        >
          No se encontró en {channel} — completa los datos manualmente.
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
          No se pudo consultar {channel} ({runtState.message}). Puedes completar los datos manualmente.
        </div>
      );
    }
    return null;
  };

  // ── Sección Representante Legal (solo persona jurídica) ────────────────────
  // Una persona natural (apoderado/representante) consultable en el RUNT. Se guarda embebida en
  // el actor (metadata) y NO participa en biométrica ni en los gates del wizard.
  const rlSection = (index: number) => {
    const actor = actors[index];
    if (!isJuridical(actor)) return null;
    const rl = actor.representanteLegal ?? {};
    const rlState: LookupState = rlRunt[index] ?? { status: 'idle' };
    const rlErrors = showErrors ? validation.byActor[index] : {};
    return (
      <div className="md:col-span-2 rounded-xl border p-4 space-y-3" style={{ background: 'rgba(85,126,255,0.03)' }}>
        <div>
          <p className="text-xs font-bold" style={{ color: '#162744' }}>
            Representante legal y/o apoderado
          </p>
          <p className="text-[10px] opacity-60">
            Persona natural que representa a la empresa. Puedes consultarla en el RUNT o registrarla
            manualmente.
          </p>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {/* Tipo de documento (natural, sin NIT) */}
          <div>
            <label htmlFor={`${index}-rl-tipoDoc`} className="text-xs font-semibold mb-1.5 block">
              Tipo de documento
            </label>
            <select
              id={`${index}-rl-tipoDoc`}
              value={rl.tipoDocumento ?? 'CC'}
              onChange={(e) => updateRepLegal(index, { tipoDocumento: e.target.value as ActorDocumentType })}
              className={INPUT_BASE}
            >
              {RL_DOC_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          {/* Número + Consultar RUNT */}
          <div>
            <label htmlFor={`${index}-rl-numeroDoc`} className="text-xs font-semibold mb-1.5 block">
              Número de documento
            </label>
            <div className="flex gap-2">
              <input
                id={`${index}-rl-numeroDoc`}
                type="text"
                value={rl.numeroDocumento ?? ''}
                onChange={(e) => updateRepLegal(index, { numeroDocumento: e.target.value })}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    void handleRlLookup(index);
                  }
                }}
                className={`${INPUT_BASE} font-mono`}
              />
              {!readOnly && (
                <button
                  type="button"
                  onClick={() => void handleRlLookup(index)}
                  disabled={rlState.status === 'loading' || !(rl.numeroDocumento ?? '').trim() || !instanceId}
                  className="shrink-0 rounded-xl px-3 py-1.5 text-[11px] font-semibold border disabled:opacity-50"
                  style={{ borderColor: '#557EFF', color: '#557EFF' }}
                >
                  {rlState.status === 'loading' ? 'Consultando…' : 'Consultar RUNT'}
                </button>
              )}
            </div>
            {rlState.status === 'found' && (
              <p className="text-[10px] mt-1" style={{ color: '#5a8a1f' }}>
                Representante encontrado en RUNT.
              </p>
            )}
            {rlState.status === 'not_found' && (
              <p className="text-[10px] mt-1 opacity-70">
                No se encontró en RUNT — completa los datos manualmente.
              </p>
            )}
            {rlState.status === 'error' && (
              <p className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                No se pudo consultar RUNT. Puedes registrarlo manualmente.
              </p>
            )}
          </div>
          {/* Nombre */}
          <div className="md:col-span-2">
            <label htmlFor={`${index}-rl-nombre`} className="text-xs font-semibold mb-1.5 block">
              Nombre completo del representante
            </label>
            <input
              id={`${index}-rl-nombre`}
              type="text"
              value={rl.nombreCompleto ?? ''}
              onChange={(e) => updateRepLegal(index, { nombreCompleto: e.target.value })}
              className={INPUT_BASE}
            />
          </div>
          {/* Email — obligatorio en persona jurídica (HU #10688): el RL valida la identidad. */}
          <div>
            <label htmlFor={`${index}-rl-email`} className="text-xs font-semibold mb-1.5 block">
              Correo electrónico <span style={{ color: '#FF4E00' }}>*</span>
            </label>
            <input
              id={`${index}-rl-email`}
              type="email"
              value={rl.email ?? ''}
              onChange={(e) => updateRepLegal(index, { email: e.target.value })}
              placeholder="correo@ejemplo.com"
              className={INPUT_BASE}
              aria-invalid={!!rlErrors.representanteLegal}
            />
            {rlErrors.representanteLegal && (
              <p className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                {rlErrors.representanteLegal}
              </p>
            )}
          </div>
          {/* Teléfono */}
          <div>
            <label htmlFor={`${index}-rl-telefono`} className="text-xs font-semibold mb-1.5 block">
              Teléfono <span className="opacity-50 font-normal">(opcional)</span>
            </label>
            <input
              id={`${index}-rl-telefono`}
              type="tel"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="tel"
              value={rl.telefono ?? ''}
              onChange={(e) => updateRepLegal(index, { telefono: e.target.value })}
              className={INPUT_BASE}
            />
          </div>
        </div>
      </div>
    );
  };

  // ── Campo Fecha de expedición del documento (RNMC, solo persona natural) ───
  // FEATURE 05 — solo se muestra cuando el RNMC aplica al trámite (rnmcEnabled): el OT destino lo
  // exige y la compañía no lo inhabilitó. Si el RNMC no aplica, no se pide la fecha (no se consulta
  // el RNMC ni se genera el certificado). Solo persona natural; RNMC no es bloqueante (opcional).
  const issueDateField = (index: number) => {
    if (!rnmcEnabled) return null;
    const actor = actors[index];
    if (isJuridical(actor)) return null;
    return (
      <div>
        <label htmlFor={`${actor.rol}-fechaExpedicion`} className="text-xs font-semibold mb-1.5 block">
          Fecha de expedición del documento{' '}
          <span className="opacity-50 font-normal">(opcional)</span>
        </label>
        <input
          id={`${actor.rol}-fechaExpedicion`}
          type="date"
          value={issueDates[index] ?? ''}
          onChange={(e) => setIssueDates((prev) => ({ ...prev, [index]: e.target.value }))}
          className={INPUT_BASE}
        />
        <p className="text-[10px] mt-1 opacity-60">
          Requerida para la consulta RNMC (medidas correctivas) cuando el organismo de tránsito la
          exige.
        </p>
      </div>
    );
  };

  // ── HU #10956 (AC1) — el check de Habeas Data de HU #10885 desapareció: la identidad se consulta
  // SIEMPRE en vivo (sin gate de consentimiento), así que ya no hay nada que autorizar aquí. En su
  // lugar, el aviso no intrusivo de abajo (`contactLookupHint`) informa cuándo el contacto se
  // precargó desde un trámite previo de esta persona en la compañía (AC2/AC5) — un origen distinto
  // al de `originBadge` (consulta externa reutilizada, AC1 de HU #10885): por eso usa copy propio y
  // NUNCA el texto "Dato reutilizado" del badge de RUNT/RUES, para no atribuirle a la consulta
  // externa un dato que en realidad viene de los propios registros de la compañía.
  const contactLookupHint = (index: number) => {
    const c = contactLookup[index] ?? { status: 'idle' };
    if (c.status === 'loading') {
      return (
        <p className="text-[10px] opacity-70" role="status" aria-live="polite">
          Buscando datos de contacto conocidos de esta persona…
        </p>
      );
    }
    if (c.status === 'found') {
      return (
        <p className="text-[10px]" style={{ color: '#5a8a1f' }} role="status" aria-live="polite">
          Contacto precargado desde un trámite anterior de esta persona en la compañía — puedes
          editarlo.
        </p>
      );
    }
    if (c.status === 'error') {
      return (
        <p className="text-[10px] opacity-70" role="status" aria-live="polite">
          No se pudo precargar el contacto conocido — completa los datos manualmente.
        </p>
      );
    }
    // idle/empty (AC4: sin antecedentes) — sin aviso, sin error; los campos siguen vacíos y editables.
    return null;
  };

  // ── Layout SPLIT (un comprador): 2 secciones ──────────────────────────────
  if (isSplit && actors.length === 1) {
    const actor = actors[0];
    const errors = showErrors ? validation.byActor[0] : {};
    const runtState: LookupState = runt[0] ?? { status: 'idle' };
    const docLocked = autoConsultRunt && !!actor.numeroDocumento.trim();
    const ciudades = filterCiudades(actor.ciudad ?? '');
    const showCiudades = !!ciudadOpen[0] && ciudades.length > 0;
    const sectionHeader = 'border-b px-4 py-3 flex items-center gap-2';
    return (
      <>
      <form
        onSubmit={handleSubmit}
        aria-label="Captura de actores del trámite"
        noValidate
      >
       <fieldset disabled={readOnly} className="space-y-5 min-w-0 border-0 p-0 m-0">
        {errorBanner}

        {/* Sección A — Identificación */}
        <section className="rounded-2xl border bg-white dark:bg-[#0B0F14] overflow-hidden">
          <div className={sectionHeader} style={{ background: 'rgba(85,126,255,0.04)' }}>
            <UserRound className="h-4 w-4" style={{ color: '#557EFF' }} />
            <span className="text-[11px] font-bold uppercase tracking-wide" style={{ color: '#162744' }}>
              {`Identificación · ${ROL_LABEL[actor.rol]}`}
            </span>
          </div>
          <div className="p-4 space-y-3">
            {personTypeSelector(0)}
            <div className="flex flex-col gap-2 sm:flex-row sm:items-start">
              <div className="flex-1">
                <input
                  id="comprador-numeroDoc"
                  type="text"
                  value={actor.numeroDocumento}
                  readOnly={docLocked}
                  onChange={(e) => updateActor(0, { numeroDocumento: e.target.value })}
                  onKeyDown={(e) => {
                    if (docLocked) return;
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      void handleIdentityLookup(0);
                    }
                  }}
                  aria-label="Número de documento"
                  aria-invalid={!!errors.numeroDocumento}
                  aria-describedby={errors.numeroDocumento ? 'comprador-numeroDoc-err' : undefined}
                  placeholder={`Número de documento del ${actor.rol}…`}
                  className={`${INPUT_BASE} font-mono${docLocked ? ' opacity-80' : ''}`}
                  style={docLocked ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                />
                {errors.numeroDocumento && (
                  <p id="comprador-numeroDoc-err" className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                    {errors.numeroDocumento}
                  </p>
                )}
              </div>
              {!readOnly && !autoConsultRunt && (
                <button
                  type="button"
                  onClick={() => void handleIdentityLookup(0)}
                  disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                  className="flex shrink-0 items-center justify-center gap-2 rounded-xl px-5 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                  style={{ background: GRADIENT }}
                  aria-label={isJuridical(actor) ? 'Consultar RUES' : 'Consultar RUNT'}
                >
                  <Search className="h-3.5 w-3.5" />
                  {runtState.status === 'loading'
                    ? 'Consultando…'
                    : isJuridical(actor)
                      ? 'Consultar RUES'
                      : 'Consultar RUNT'}
                </button>
              )}
            </div>
            {runtResult(0)}
            {rlSection(0)}
          </div>
        </section>

        {/* Sección B — Datos de contacto */}
        <section className="rounded-2xl border bg-white dark:bg-[#0B0F14] overflow-hidden">
          <div className="border-b px-4 py-3 flex flex-col gap-1" style={{ background: 'rgba(85,126,255,0.04)' }}>
            <span className="text-[11px] font-bold uppercase tracking-wide" style={{ color: '#162744' }}>
              Datos de contacto
            </span>
            {contactLookupHint(0)}
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
                style={{ background: 'rgba(223,229,237,0.35)' }}
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
                onChange={(e) => {
                  markContactTouched(0, 'email');
                  updateActor(0, { email: e.target.value });
                }}
                placeholder="correo@ejemplo.com"
                aria-invalid={!!errors.email}
                aria-describedby={errors.email ? 'comprador-email-err' : undefined}
                className={INPUT_BASE}
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
                inputMode="numeric"
                pattern="[0-9]*"
                autoComplete="tel"
                value={actor.telefono ?? ''}
                onChange={(e) => {
                  markContactTouched(0, 'telefono');
                  updateActor(0, { telefono: e.target.value });
                }}
                placeholder="3001234567"
                className={INPUT_BASE}
              />
            </div>
            {/* Fecha de expedición del documento (RNMC, solo persona natural) */}
            {issueDateField(0)}
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
                  markContactTouched(0, 'ciudad');
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
              />
              {showCiudades && (
                <ul
                  className="absolute top-full left-0 right-0 mt-1 z-50 max-h-48 overflow-auto rounded-xl border bg-white dark:bg-[#0B0F14]"
                  aria-label="Sugerencias de ciudad"
                >
                  {ciudades.map((c) => (
                    <li key={c}>
                      <button
                        type="button"
                        onMouseDown={(e) => {
                          e.preventDefault();
                          markContactTouched(0, 'ciudad');
                          updateActor(0, { ciudad: c });
                          setCiudadOpen((p) => ({ ...p, 0: false }));
                        }}
                        className="w-full text-left px-3 py-2 text-xs border-b last:border-0 hover:bg-[rgba(85,126,255,0.06)]"
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
                onChange={(e) => {
                  markContactTouched(0, 'direccion');
                  updateActor(0, { direccion: e.target.value });
                }}
                className={INPUT_BASE}
              />
            </div>
          </div>
        </section>

        {footer}
       </fieldset>
      </form>
      {emailChangeConfirm && (
        <EmailReenvioConfirmModal
          changes={emailChangeConfirm}
          onCancel={() => resolveEmailChangeConfirm(false)}
          onConfirm={() => resolveEmailChangeConfirm(true)}
        />
      )}
      </>
    );
  }

  // ── Layout MULTI (traspaso): un fieldset por actor ────────────────────────
  return (
    <>
    <form
      onSubmit={handleSubmit}
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14] mt-4"
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
          const runtState: LookupState = runt[index] ?? { status: 'idle' };
          // HU #10956 — ciudad con autocomplete, misma lógica que el layout SPLIT pero por índice.
          const ciudadesSuggestions = filterCiudades(actor.ciudad ?? '');
          const showCiudadSuggestions = !!ciudadOpen[index] && ciudadesSuggestions.length > 0;
          return (
            <fieldset key={actor.rol} className="rounded-xl border p-4">
              <legend className="px-1 text-xs font-bold">{ROL_LABEL[actor.rol]}</legend>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* Tipo de persona (HU #10543) */}
                <div className="md:col-span-2">{personTypeSelector(index)}</div>
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
                  />
                  {errors.numeroDocumento && (
                    <p id={`${prefix}-numeroDoc-err`} className="text-[10px] mt-1" style={{ color: '#FF4E00' }}>
                      {errors.numeroDocumento}
                    </p>
                  )}
                  {/* Consultar identidad: RUES (jurídica) o RUNT (natural). Autopobla el actor. */}
                  {!readOnly && (
                    <button
                      type="button"
                      onClick={() => void handleIdentityLookup(index)}
                      disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                      className="mt-2 px-3 py-1.5 rounded-xl text-[11px] font-semibold border disabled:opacity-50"
                      style={{ borderColor: '#557EFF', color: '#557EFF' }}
                    >
                      {runtState.status === 'loading'
                        ? 'Consultando…'
                        : isJuridical(actor)
                          ? 'Consultar RUES'
                          : 'Consultar RUNT'}
                    </button>
                  )}
                </div>

                {/* Resultado de la consulta (RUNT/RUES, autopoblado). */}
                {runt[index] && runt[index].status !== 'idle' && (
                  <div className="md:col-span-2">{runtResult(index)}</div>
                )}

                {/* Representante legal (solo persona jurídica). */}
                {rlSection(index)}

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
                    onChange={(e) => {
                      markContactTouched(index, 'email');
                      updateActor(index, { email: e.target.value });
                    }}
                    aria-invalid={!!errors.email}
                    aria-describedby={errors.email ? `${prefix}-email-err` : undefined}
                    className={INPUT_BASE}
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
                    inputMode="numeric"
                    pattern="[0-9]*"
                    autoComplete="tel"
                    value={actor.telefono ?? ''}
                    onChange={(e) => {
                      markContactTouched(index, 'telefono');
                      updateActor(index, { telefono: e.target.value });
                    }}
                    className={INPUT_BASE}
                  />
                </div>

                {/* Ciudad (autocomplete) — HU #10956, precargable desde el contacto ya conocido. */}
                <div className="relative">
                  <label htmlFor={`${prefix}-ciudad`} className="text-xs font-semibold mb-1.5 block">
                    Ciudad
                  </label>
                  <input
                    id={`${prefix}-ciudad`}
                    type="text"
                    value={actor.ciudad ?? ''}
                    onChange={(e) => {
                      markContactTouched(index, 'ciudad');
                      updateActor(index, { ciudad: e.target.value });
                      setCiudadOpen((p) => ({ ...p, [index]: true }));
                    }}
                    onFocus={() => {
                      if ((actor.ciudad ?? '').trim().length >= 2) {
                        setCiudadOpen((p) => ({ ...p, [index]: true }));
                      }
                    }}
                    onBlur={() => setTimeout(() => setCiudadOpen((p) => ({ ...p, [index]: false })), 150)}
                    autoComplete="off"
                    placeholder="Escribe para buscar…"
                    className={INPUT_BASE}
                  />
                  {showCiudadSuggestions && (
                    <ul
                      className="absolute top-full left-0 right-0 mt-1 z-50 max-h-48 overflow-auto rounded-xl border bg-white dark:bg-[#0B0F14]"
                      aria-label="Sugerencias de ciudad"
                    >
                      {ciudadesSuggestions.map((c) => (
                        <li key={c}>
                          <button
                            type="button"
                            onMouseDown={(e) => {
                              e.preventDefault();
                              markContactTouched(index, 'ciudad');
                              updateActor(index, { ciudad: c });
                              setCiudadOpen((p) => ({ ...p, [index]: false }));
                            }}
                            className="w-full text-left px-3 py-2 text-xs border-b last:border-0 hover:bg-[rgba(85,126,255,0.06)]"
                          >
                            {c}
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>

                {/* Dirección (opcional) — HU #10956, precargable desde el contacto ya conocido. */}
                <div>
                  <label htmlFor={`${prefix}-direccion`} className="text-xs font-semibold mb-1.5 block">
                    Dirección
                  </label>
                  <input
                    id={`${prefix}-direccion`}
                    type="text"
                    value={actor.direccion ?? ''}
                    onChange={(e) => {
                      markContactTouched(index, 'direccion');
                      updateActor(index, { direccion: e.target.value });
                    }}
                    className={INPUT_BASE}
                  />
                </div>

                {/* Aviso de precarga de contacto (HU #10956) — no bloqueante, no reemplaza al badge de
                    origen de la consulta externa (`originBadge`, dentro de runtResult). */}
                {contactLookupHint(index) && (
                  <div className="md:col-span-2">{contactLookupHint(index)}</div>
                )}

                {/* Fecha de expedición del documento (RNMC, solo persona natural) */}
                {issueDateField(index)}
              </div>
            </fieldset>
          );
        })}
      </div>

      {footer}
     </fieldset>
    </form>
    {emailChangeConfirm && (
      <EmailReenvioConfirmModal
        changes={emailChangeConfirm}
        onCancel={() => resolveEmailChangeConfirm(false)}
        onConfirm={() => resolveEmailChangeConfirm(true)}
      />
    )}
    </>
  );
});

/**
 * HU #10886 (AC1) — modal de confirmación del reenvío de validación de identidad al editar el
 * correo del sujeto de identidad de una o más partes. Sin confirmación explícita el PUT de actores
 * NO se envía. Foco atrapado dentro del panel (Tab/Shift+Tab cicla entre Cancelar/Continuar);
 * Escape/backdrop del `Modal` compartido equivalen a "Cancelar".
 */
function EmailReenvioConfirmModal({
  changes,
  onCancel,
  onConfirm,
}: {
  changes: EmailChangeConfirmInfo[];
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const cancelRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    cancelRef.current?.focus();
  }, []);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'Tab' || !containerRef.current) return;
    const focusables = containerRef.current.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    );
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  return (
    <Modal
      open
      onClose={onCancel}
      title="Reenviar validación de identidad"
      icon={AlertTriangle}
      iconBg="#B26A00"
      size="sm"
    >
      <div ref={containerRef} onKeyDown={handleKeyDown} className="space-y-4">
        <div className="space-y-2 text-xs" role="alert">
          {changes.map((c) => (
            <p key={c.rol}>
              Este cambio reenviará el correo de validación de identidad de{' '}
              <span className="font-semibold">{c.roleLabel}</span> a{' '}
              <span className="font-semibold">{c.newEmail}</span> e invalidará el enlace anterior.
            </p>
          ))}
          <p className="opacity-70">¿Continuar?</p>
        </div>
        <div className="flex items-center justify-end gap-2">
          <button
            ref={cancelRef}
            type="button"
            onClick={onCancel}
            className="px-4 py-2 rounded-xl text-xs font-semibold border border-[#DFE5ED] dark:border-white/10"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={onConfirm}
            className="px-4 py-2 rounded-xl text-xs font-semibold text-white"
            style={{ background: GRADIENT }}
          >
            Continuar
          </button>
        </div>
      </div>
    </Modal>
  );
}

'use client';

import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from 'react';
import { AlertTriangle, Info, Loader2, Search } from 'lucide-react';
import { INLINE_ALERT_TONES, type InlineAlertTone } from '@/components/atom/InlineAlert';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { Modal } from '@/components/atom/Modal';
import { usePendingChanges } from './pending-changes';
import { useWizardFocusTrap } from './use-wizard-focus-trap';
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
import {
  WIZARD_INPUT,
  WIZARD_LABEL,
  WIZARD_BTN,
  WIZARD_CARD,
  WIZARD_CTA_GRADIENT,
} from './wizard-field-styles';
import { WizardCardHeader, WizardSegmented } from './wizard-atoms';
import { CarLoaderModal } from '@/components/atom/CarLoader';

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
  /**
   * Gate de avance del wizard: `true` cuando TODOS los actores del formulario tienen consulta de
   * identidad exitosa (RUNT / RUES / directorio). Sin consulta OK, Continuar permanece deshabilitado.
   */
  onConsultationGateChange?: (ready: boolean) => void;
}

/** ¿La consulta de identidad resolvió datos? Gate duro de avance/guardado. */
export function isIdentityConsultationReady(
  status: 'idle' | 'loading' | 'found' | 'not_found' | 'error' | undefined,
): boolean {
  return status === 'found';
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
 * HU #11595 — ciudad, dirección y teléfono son obligatorios (antes opcionales): el organismo
 * devuelve el trámite cuando faltan, así que bloquean "Continuar" igual que nombre/documento/email.
 * Pura: sin estado, testeable de forma aislada.
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
    // HU #11595 — ciudad, dirección y teléfono pasan a obligatorios: el organismo devuelve el
    // trámite cuando faltan (ver ParteCompletaRule en el backend, HU #11593).
    if (!a.ciudad?.trim()) e.ciudad = 'Ciudad requerida';
    if (!a.direccion?.trim()) e.direccion = 'Dirección requerida';
    if (!a.telefono?.trim()) e.telefono = 'Teléfono requerido';
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
    const rest = { ...a };
    delete rest.autorizaReutilizacionDatos;
    return {
      ...rest,
      telefono: blankToUndef(a.telefono),
      ciudad: blankToUndef(a.ciudad),
      direccion: blankToUndef(a.direccion),
    };
  });
}

const INPUT_BASE = WIZARD_INPUT;

const GRADIENT = 'linear-gradient(135deg,#557EFF,#00DBD5)';

/**
 * Marco de las tarjetas de resultado de consulta (precarga del directorio, RUES, RUNT, comparendos).
 *
 * <p>Traían borde y fondo en verde lima `#8CC63F`/`#5a8a1f`, hex que no existen ni en `globals.css`
 * ni en los tokens de diseño y que `docs/plan-alineacion-tablas-y-badges.md` señala como divergencia
 * a erradicar: convivían en el mismo recuadro con los `StatusBadge`, que sí usan tokens.</p>
 *
 * <p>No se usa `InlineAlert` como contenedor porque estas tarjetas llevan dentro grids de datos,
 * selectores y badges, y el componente teñiría todo el cuerpo con el color del tono. Se toma su
 * paleta, que es lo que había que unificar.</p>
 */
/** Color de texto para un valor/estado válido. Antes eran tres verdes distintos. */
const OK_FG = INLINE_ALERT_TONES.success.color;

function cardTone(tone: InlineAlertTone) {
  const { color, background, border } = INLINE_ALERT_TONES[tone];
  return { card: { borderColor: border, background }, title: { color } };
}

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

/** Solo estados `found` son candidatas a rehidratación al volver al paso. */
type FoundLookupState = Extract<LookupState, { status: 'found' }>;

type ActorConsultationCacheEntry = {
  rol: ActorRol;
  tipoDocumento: string;
  numeroDocumento: string;
  state: FoundLookupState;
};

function actorConsultationStorageKey(instanceId: string): string {
  return `flit:actor-identity-consultation:${instanceId}`;
}

function consultationEntryKey(rol: string, tipo: string, numero: string): string {
  return `${rol}|${tipo}|${numero.trim()}`;
}

/** Lee el snapshot de consultas de identidad del trámite (sobrevive a desmontar el paso). */
export function readActorConsultationCache(
  instanceId: string | null,
): Record<string, ActorConsultationCacheEntry> {
  if (!instanceId || typeof sessionStorage === 'undefined') return {};
  try {
    const raw = sessionStorage.getItem(actorConsultationStorageKey(instanceId));
    if (!raw) return {};
    const parsed = JSON.parse(raw) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {};
    return parsed as Record<string, ActorConsultationCacheEntry>;
  } catch {
    return {};
  }
}

function writeActorConsultationCache(
  instanceId: string | null,
  cache: Record<string, ActorConsultationCacheEntry>,
) {
  if (!instanceId || typeof sessionStorage === 'undefined') return;
  try {
    sessionStorage.setItem(actorConsultationStorageKey(instanceId), JSON.stringify(cache));
  } catch {
    // ignore quota / private mode
  }
}

function rememberActorConsultation(
  instanceId: string | null,
  actor: ProcedureActor,
  state: FoundLookupState,
) {
  if (!instanceId || !actor.numeroDocumento.trim()) return;
  const cache = readActorConsultationCache(instanceId);
  const key = consultationEntryKey(actor.rol, actor.tipoDocumento, actor.numeroDocumento);
  // Una sola entrada vigente por rol: si cambió el documento, descarta la anterior.
  for (const [k, entry] of Object.entries(cache)) {
    if (entry.rol === actor.rol && k !== key) delete cache[k];
  }
  cache[key] = {
    rol: actor.rol,
    tipoDocumento: actor.tipoDocumento,
    numeroDocumento: actor.numeroDocumento.trim(),
    state,
  };
  writeActorConsultationCache(instanceId, cache);
}

function forgetActorConsultation(instanceId: string | null, rol: ActorRol) {
  if (!instanceId) return;
  const cache = readActorConsultationCache(instanceId);
  let changed = false;
  for (const [k, entry] of Object.entries(cache)) {
    if (entry.rol === rol) {
      delete cache[k];
      changed = true;
    }
  }
  if (changed) writeActorConsultationCache(instanceId, cache);
}

function restoreActorConsultation(
  instanceId: string | null,
  actor: ProcedureActor,
): FoundLookupState | null {
  if (!instanceId || !actor.numeroDocumento.trim()) return null;
  const key = consultationEntryKey(actor.rol, actor.tipoDocumento, actor.numeroDocumento);
  const entry = readActorConsultationCache(instanceId)[key];
  return entry?.state?.status === 'found' ? entry.state : null;
}

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

/** Normaliza NIT para comparar locks de razón social (solo dígitos). */
function normalizeNitKey(nit: string): string {
  return nit.replace(/\D/g, '');
}

function ruesRazonLockStorageKey(instanceId: string): string {
  return `flit:rues-razon-locked:${instanceId}`;
}

function readRuesRazonLocks(instanceId: string | null): Set<string> {
  if (!instanceId || typeof sessionStorage === 'undefined') return new Set();
  try {
    const raw = sessionStorage.getItem(ruesRazonLockStorageKey(instanceId));
    if (!raw) return new Set();
    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed) ? new Set(parsed.filter((x) => typeof x === 'string')) : new Set();
  } catch {
    return new Set();
  }
}

function writeRuesRazonLocks(instanceId: string | null, locks: Set<string>) {
  if (!instanceId || typeof sessionStorage === 'undefined') return;
  try {
    sessionStorage.setItem(ruesRazonLockStorageKey(instanceId), JSON.stringify([...locks]));
  } catch {
    // ignore quota / private mode
  }
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
 *  - MULTI (traspaso): una tarjeta blanca por actor (vendedor + comprador), lado a lado.
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
    onConsultationGateChange,
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
  /**
   * Bug #11614 — captura del usuario sin persistir. Se marca en las vías de edición (`updateActor`,
   * `updateRepLegal`, fecha de expedición) y NO en la rehidratación desde el backend: así una
   * navegación por el stepper sobre un paso que solo se abrió para mirar no dispara un PUT que,
   * además, chocaría contra el gate de consulta RUNT/RUES. La shell la consulta antes de cambiar
   * de paso, porque al hacerlo este formulario se desmonta y lo capturado se perdía.
   */
  const pending = usePendingChanges();
  const markDirty = pending.markDirty;
  const [showErrors, setShowErrors] = useState(false);
  // Estado de la consulta de identidad por índice de actor (RUNT o RUES, autopoblado).
  const [runt, setRunt] = useState<Record<number, LookupState>>({});
  // NITs cuya razón social vino de RUES (o precarga directorio): no editable mientras aplique.
  const [ruesRazonLockedNits, setRuesRazonLockedNits] = useState<Set<string>>(() =>
    readRuesRazonLocks(instanceId),
  );
  useEffect(() => {
    setRuesRazonLockedNits(readRuesRazonLocks(instanceId));
  }, [instanceId]);
  const lockRuesRazonSocial = (nit: string) => {
    const key = normalizeNitKey(nit);
    if (!key) return;
    setRuesRazonLockedNits((prev) => {
      if (prev.has(key)) return prev;
      const next = new Set(prev);
      next.add(key);
      writeRuesRazonLocks(instanceId, next);
      return next;
    });
  };
  const unlockRuesRazonSocial = (nit: string) => {
    const key = normalizeNitKey(nit);
    if (!key) return;
    setRuesRazonLockedNits((prev) => {
      if (!prev.has(key)) return prev;
      const next = new Set(prev);
      next.delete(key);
      writeRuesRazonLocks(instanceId, next);
      return next;
    });
  };
  const isRazonSocialLocked = (actor: ProcedureActor, index: number): boolean => {
    if (!isJuridical(actor) || !actor.nombreCompleto.trim()) return false;
    const nitKey = normalizeNitKey(actor.numeroDocumento);
    if (nitKey && ruesRazonLockedNits.has(nitKey)) return true;
    const state = runt[index];
    if (state?.status !== 'found') return false;
    if (state.kind === 'rues') return !!state.result.razonSocial?.trim();
    if (state.kind === 'preload') return !!state.result.company.razonSocial?.trim();
    return false;
  };
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
    // Autocompletado del contacto tras la consulta: mismo criterio que el resto del autopoblado
    // (Bug #11614) — no es captura del gestor, así que no obliga a guardar al cambiar de paso.
    if (Object.keys(patch).length > 0)
      updateActor(index, patch, { preserveConsultation: true });
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
  // Novedad nov.41 — la precarga es asíncrona y antes no tenía ningún indicador visible: el
  // campo se veía vacío unos segundos y luego se autocompletaba solo, sin explicación. Se
  // expone el estado para mostrar carga y, si falla, un error explícito con reintento (antes
  // el `.catch` lo silenciaba).
  const [ownerSeedStatus, setOwnerSeedStatus] = useState<'idle' | 'loading' | 'error'>('idle');
  const [ownerSeedRetry, setOwnerSeedRetry] = useState(0);

  // Carga el documento del propietario desde los field_values de la instancia.
  // Solo aplica cuando `seedDocumentoFromOwner` (paso vendedor del traspaso).
  useEffect(() => {
    if (!seedDocumentoFromOwner || !instanceId) return;
    let active = true;
    setOwnerSeedStatus('loading');
    tramitesClient
      .getInstance(instanceId)
      .then((detail) => {
        if (!active) return;
        setOwnerSeedStatus('idle');
        if (!detail?.fieldValues) return;
        const byKey = (key: string) =>
          detail.fieldValues.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';
        const numero = byKey('owner_document_number');
        if (!numero) return;
        const tipoRaw = byKey('owner_document_type') as ActorDocumentType;
        setOwnerSeed({ numero, tipo: DOC_VALUES.has(tipoRaw) ? tipoRaw : 'CC' });
      })
      .catch(() => {
        if (!active) return;
        setOwnerSeedStatus('error');
      });
    return () => {
      active = false;
    };
  }, [seedDocumentoFromOwner, instanceId, ownerSeedRetry]);

  // Aplica el documento del propietario (paso 1) SOLO al vendedor sin documento.
  // No pisa un documento ya escrito/persistido y nunca siembra al comprador (en el
  // formulario unificado vendedor+comprador ambos roles pasan por este helper).
  const withOwnerSeed = (a: ProcedureActor): ProcedureActor =>
    ownerSeed && a.rol === 'vendedor' && !a.numeroDocumento.trim()
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
    const nextActors = roles.map((rol) => {
      const found = state.actors?.find((a) => a.rol === rol);
      // HU #11014 — al rehidratar desde el backend también se deriva el tipo de persona: un actor
      // persistido con NIT y personType 'natural' (creado antes de esta corrección) se corrige solo.
      return withOwnerSeed(
        found ? withDerivedPersonType({ ...emptyActor(rol), ...found }) : emptyActor(rol),
      );
    });
    setActors(nextActors);
    // HU #11595 (AC4) — un trámite en curso que YA tenía un actor persistido (documento propio,
    // no un paso vacío recién abierto) pero quedó sin ciudad/dirección/teléfono debe mostrar esos
    // campos marcados como faltantes al abrir el paso, sin esperar a que el gestor pulse
    // "Continuar" (que es lo único que antes activaba `showErrors`).
    const hasIncompleteExistingActor = state.actors.some((persisted) => {
      if (!persisted.numeroDocumento?.trim()) return false;
      const merged = nextActors.find((a) => a.rol === persisted.rol);
      return !!merged && !validateActors([merged], modalidad).valid;
    });
    if (hasIncompleteExistingActor) setShowErrors(true);
    // Aquí NO se limpia la marca de pendiente, aunque lo rehidratado sea lo persistido: los actores
    // llegan de una carga asíncrona de la shell y esta rama corre cuando aterrizan, que puede ser
    // DESPUÉS de que el gestor empezara a escribir. La marca solo puede estar puesta si él editó,
    // así que limpiarla aquí únicamente podría borrar captura viva (la carrera que reintroducía la
    // pérdida del Bug #11614). Lo persistido limpia la marca donde corresponde: al guardar.
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

  const setRuntFor = (index: number, value: LookupState) =>
    setRunt((prev) => ({ ...prev, [index]: value }));

  const updateActor = (
    index: number,
    patch: Partial<ProcedureActor>,
    opts?: { preserveConsultation?: boolean },
  ) => {
    const prevActor = actors[index];
    const identityChanged =
      patch.numeroDocumento !== undefined ||
      patch.tipoDocumento !== undefined ||
      patch.personType !== undefined;
    // No permitir editar razón social traída por RUES / precarga directorio.
    if (
      patch.nombreCompleto !== undefined &&
      prevActor &&
      isRazonSocialLocked(prevActor, index) &&
      !opts?.preserveConsultation
    ) {
      const rest = { ...patch };
      delete rest.nombreCompleto;
      patch = rest;
      if (Object.keys(patch).length === 0 && !identityChanged) return;
    }
    // El autopoblado que sigue a una consulta (RUNT/RUES/directorio) NO es captura del gestor: si
    // llegó ahí fue porque él escribió el documento (eso ya marcó pendiente) o porque la consulta
    // automática se disparó sola al abrir el paso. Marcar esta vía obligaría a guardar un paso que
    // el gestor solo abrió para mirar — y con un actor persistido incompleto lo dejaría atrapado.
    if (!opts?.preserveConsultation) markDirty();
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
    // Cambio MANUAL de identidad invalida la consulta. El autopoblado post-RUNT/RUES
    // usa preserveConsultation para no disparar un segundo lookup ni perder el `found`.
    if (identityChanged && !opts?.preserveConsultation) {
      if (prevActor?.numeroDocumento) unlockRuesRazonSocial(prevActor.numeroDocumento);
      if (prevActor) forgetActorConsultation(instanceId, prevActor.rol);
      setRuntFor(index, { status: 'idle' });
      autoLookupTriggeredRef.current = null;
    }
  };

  const setRlRuntFor = (index: number, value: LookupState) =>
    setRlRunt((prev) => ({ ...prev, [index]: value }));

  // Actualiza el representante legal (persona jurídica) de un actor, embebido en el propio actor.
  const updateRepLegal = (
    index: number,
    patch: Partial<RepresentanteLegal>,
    opts?: { preserveConsultation?: boolean },
  ) => {
    if (!opts?.preserveConsultation) markDirty();
    return setActors((prev) =>
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
  };

  // HU #10937 — precarga en el actor el representante ELEGIDO (su documento + contacto). El actor
  // guarda este representante embebido; el backend firma/valida identidad por SU documento.
  const applySelectedRep = (
    index: number,
    rep: LegalRepresentativeOption,
    opts?: { preserveConsultation?: boolean },
  ) =>
    updateRepLegal(index, {
      tipoDocumento: (rep.tipoDoc as ActorDocumentType) || 'CC',
      numeroDocumento: rep.documento,
      nombreCompleto: repFullName(rep),
      email: rep.email ?? undefined,
      telefono: rep.telefono ?? undefined,
    }, opts);

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
  // natural → RUNT (conductor). Si encuentra, autopopula el actor. Sin resultado exitoso no se
  // permite guardar ni avanzar (gate de Continuar en el wizard).
  /**
   * Consultas en vuelo que nacieron de un clic. Es un contador y no un booleano porque en traspaso
   * hay dos actores y el gestor puede lanzar la segunda consulta sin esperar a la primera.
   */
  const [consultasManuales, setConsultasManuales] = useState(0);

  /** Envuelve una consulta para que levante el velo de espera; solo la usan los botones. */
  const conVelo = (consulta: Promise<unknown>) => {
    setConsultasManuales((n) => n + 1);
    void consulta.finally(() => setConsultasManuales((n) => Math.max(0, n - 1)));
  };

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
          updateActor(
            index,
            {
              nombreCompleto: preload.company.razonSocial,
              tipoDocumento: 'NIT',
              numeroDocumento: nit,
            },
            { preserveConsultation: true },
          );
          // HU #10937 — si la compañía tiene varios representantes, se precarga el primero por defecto
          // y el gestor puede cambiarlo con el selector (handleSelectRep). Con uno solo, comportamiento
          // previo (auto-seleccionado). El representante elegido queda embebido en el actor.
          const reps = repsOf(preload);
          setSelectedRepIdx((prev) => ({ ...prev, [index]: 0 }));
          if (reps[0]) applySelectedRep(index, reps[0], { preserveConsultation: true });
          const foundPreload = { status: 'found' as const, kind: 'preload' as const, result: preload };
          setRuntFor(index, foundPreload);
          rememberActorConsultation(instanceId, {
            ...actor,
            tipoDocumento: 'NIT',
            numeroDocumento: nit,
            nombreCompleto: preload.company.razonSocial,
          }, foundPreload);
          lockRuesRazonSocial(nit);
          // HU #10956 (AC2) — identidad resuelta (match del directorio): precarga el contacto conocido.
          void runContactLookup(index, 'NIT', nit);
          return;
        }

        const result = await tramitesClient.ruesPersonLookup(instanceId, {
          documentNumber,
        });
        if (result.found) {
          const nit = result.documentNumber || actor.numeroDocumento;
          updateActor(
            index,
            {
              nombreCompleto: result.razonSocial ?? actor.nombreCompleto,
              tipoDocumento: 'NIT',
              numeroDocumento: nit,
            },
            { preserveConsultation: true },
          );
          setRuntFor(index, { status: 'found', kind: 'rues', result });
          rememberActorConsultation(
            instanceId,
            {
              ...actor,
              tipoDocumento: 'NIT',
              numeroDocumento: nit,
              nombreCompleto: result.razonSocial ?? actor.nombreCompleto,
            },
            { status: 'found', kind: 'rues', result },
          );
          if (result.razonSocial?.trim()) lockRuesRazonSocial(nit);
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
        updateActor(
          index,
          {
            nombreCompleto: result.fullName ?? actor.nombreCompleto,
            tipoDocumento: resolvedTipo,
            numeroDocumento: resolvedNumero,
          },
          { preserveConsultation: true },
        );
        setRuntFor(index, { status: 'found', kind: 'runt', result });
        rememberActorConsultation(
          instanceId,
          {
            ...actor,
            tipoDocumento: resolvedTipo,
            numeroDocumento: resolvedNumero,
            nombreCompleto: result.fullName ?? actor.nombreCompleto,
          },
          { status: 'found', kind: 'runt', result },
        );
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

  // Rehidrata consultas de identidad ya hechas en este trámite (Continuar → Anterior) sin
  // volver a llamar RUNT/RUES. El snapshot vive en sessionStorage por instancia.
  useEffect(() => {
    if (!instanceId) return;
    setRunt((prev) => {
      const next = { ...prev };
      let changed = false;
      actors.forEach((actor, index) => {
        const current = next[index];
        if (current && current.status !== 'idle') return;
        const restored = restoreActorConsultation(instanceId, actor);
        if (!restored) return;
        next[index] = restored;
        changed = true;
        if (actor.rol === 'vendedor') {
          autoLookupTriggeredRef.current = `${actor.tipoDocumento}:${actor.numeroDocumento.trim()}`;
        }
      });
      return changed ? next : prev;
    });
  }, [instanceId, actors]);

  // Paso vendedor: dispara la consulta en cuanto el documento está disponible (sembrado desde el
  // paso 1 o rehidratado del backend), sin clic manual. HU #10906 — el cortocircuito de precarga por
  // NIT vive dentro de handleIdentityLookup (rama jurídica).
  // Split (un vendedor) o MULTI unificado (índice del rol vendedor).
  // Si ya hay snapshot restaurado (`found`), no vuelve a consultar.
  const vendedorIndex = actors.findIndex((a) => a.rol === 'vendedor');
  const vendedorDoc = vendedorIndex >= 0 ? actors[vendedorIndex]?.numeroDocumento : undefined;
  const vendedorTipo = vendedorIndex >= 0 ? actors[vendedorIndex]?.tipoDocumento : undefined;
  const vendedorRuntStatus = vendedorIndex >= 0 ? runt[vendedorIndex]?.status : undefined;

  useEffect(() => {
    if (!autoConsultRunt || !instanceId || readOnly) return;
    if (vendedorIndex < 0) return;
    if (isSplit && (actors.length !== 1 || actors[0]?.rol !== 'vendedor')) return;

    const documentNumber = (vendedorDoc ?? '').trim();
    if (!documentNumber) return;
    if ((vendedorRuntStatus ?? 'idle') !== 'idle') return;
    const vendedor = actors[vendedorIndex];
    const cached = vendedor ? restoreActorConsultation(instanceId, vendedor) : null;
    if (cached) {
      setRuntFor(vendedorIndex, cached);
      autoLookupTriggeredRef.current = `${vendedorTipo}:${documentNumber}`;
      return;
    }
    const lookupKey = `${vendedorTipo}:${documentNumber}`;
    if (autoLookupTriggeredRef.current === lookupKey) return;
    autoLookupTriggeredRef.current = lookupKey;
    void handleIdentityLookup(vendedorIndex);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- handleIdentityLookup lee actors actuales
  }, [
    autoConsultRunt,
    instanceId,
    readOnly,
    isSplit,
    actors.length,
    vendedorIndex,
    vendedorDoc,
    vendedorTipo,
    vendedorRuntStatus,
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

    // Gate duro: cada actor debe tener consulta RUNT/RUES/directorio exitosa antes de persistir.
    if (actors.some((_, i) => !isIdentityConsultationReady(runt[i]?.status))) {
      return false;
    }

    const normalized = normalizeActors(actors);

    // AC1 — correo cambiado con validación en curso: pide confirmación antes de persistir. Sin
    // confirmación explícita, NO se envía el PUT.
    const changes = await detectEmailChangesWithActiveValidation(normalized);
    if (changes.length > 0) {
      const confirmed = await requestEmailChangeConfirm(changes);
      if (!confirmed) return false;
    }

    // ANTES del await: lo que el gestor escriba mientras el guardado viaja no va en `normalized`,
    // así que sigue pendiente y la marca debe sobrevivir.
    const settle = pending.beginSettle();
    const ok = await save(normalized);
    if (ok) {
      settle();
      // Fecha de expedición (RNMC) — persistencia best-effort tras guardar los actores.
      await persistIssueDates();
      onSaved?.(normalized);
    }
    return ok;
  };

  useImperativeHandle(ref, () => ({ save: submitActors, hasPendingChanges: pending.hasPendingChanges }));

  // Notifica al wizard si Continuar puede habilitarse (consulta exitosa en todos los actores).
  const consultationReady = actors.every((_, i) =>
    isIdentityConsultationReady(runt[i]?.status),
  );
  useEffect(() => {
    onConsultationGateChange?.(consultationReady);
  }, [consultationReady, onConsultationGateChange]);

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
          className="text-xs font-semibold"
          style={{ color: OK_FG }}
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
        className={`${WIZARD_BTN} text-white disabled:opacity-50 focus-visible:ring-[#557EFF]`}
        style={{ background: WIZARD_CTA_GRADIENT }}
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
        <WizardSegmented
          label="Tipo de persona"
          value={current}
          options={PERSON_TYPE_OPTIONS}
          onChange={(value) => {
            // Jurídica ⇒ documento NIT (RUES). Volver a natural desde NIT ⇒ CC por defecto.
            const patch: Partial<ProcedureActor> = { personType: value };
            if (value === 'juridical') patch.tipoDocumento = 'NIT';
            else if (actors[index].tipoDocumento === 'NIT') patch.tipoDocumento = 'CC';
            updateActor(index, patch);
          }}
        />
        {current === 'natural' && (
          <p className="text-xs mt-1 opacity-60">
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
    mode === 'cache' && <StatusBadge label={`Dato reutilizado · ${source}`} tone="info" />;

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
          <div className="rounded-xl p-3 text-xs border" style={cardTone('info').card}>
            {/* Tono informativo, no de éxito: el aviso dice de DÓNDE salió el dato (no se consultó
                RUES/RUNT), no que algo sea válido. Es el mismo azul del badge «Dato reutilizado». */}
            <p
              className="font-semibold mb-2 flex items-center gap-1.5"
              style={cardTone('info').title}
            >
              <Info className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
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
          <div className="rounded-xl p-3 text-xs border" style={cardTone('info').card}>
            <p
              className="font-semibold mb-2 flex items-center gap-1.5 flex-wrap"
              style={cardTone('info').title}
            >
              <Info className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
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
                  style={{ color: activa ? OK_FG : INLINE_ALERT_TONES.error.color }}
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
          <div className="rounded-xl p-3 text-xs border" style={cardTone('info').card}>
            <p
              className="font-semibold mb-2 flex items-center gap-1.5 flex-wrap"
              style={cardTone('info').title}
            >
              <Info className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              Persona encontrada en RUNT
              {originBadge(r.mode, 'RUNT')}
            </p>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-x-4 gap-y-1.5">
              <div>
                <span className="opacity-60 font-normal">Nombres: </span>
                <span className="font-semibold" style={{ color: '#162744' }}>
                  {/* firstName es solo el PRIMER nombre; los de pila se muestran juntos. */}
                  {[r.firstName, r.secondName].filter(Boolean).join(' ') || r.fullName || '—'}
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
                  style={{ color: r.citizenStatus === 'ACTIVA' ? OK_FG : '#162744' }}
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
                  style={{ color: r.licenseStatus === 'ACTIVO' ? OK_FG : '#162744' }}
                >
                  {r.licenseStatus ?? '—'}
                </span>
              </div>
            </div>
          </div>

          {/* Card B — Multas. Con multas pendientes, bajo la alerta se lista el detalle de cada
              comparendo (SIMIT best-effort); si el detalle no llegó, se conserva solo la alerta.
              A diferencia de las tarjetas de arriba, esta SÍ afirma validez, así que conserva la
              lectura de éxito/alerta en vez de pasar a informativa. */}
          {r.hasPendingFines !== undefined && (
            <div
              className="rounded-xl p-3 text-xs border"
              style={
                r.hasPendingFines
                  ? { ...cardTone('error').card, ...cardTone('error').title }
                  : { ...cardTone('success').card, ...cardTone('success').title }
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
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          No se encontró en {channel}. Corrige el documento e intenta de nuevo; sin datos de{' '}
          {channel} no puedes continuar.
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
          No se pudo consultar {channel} ({runtState.message}). Reintenta la consulta; sin datos de{' '}
          {channel} no puedes continuar.
        </div>
      );
    }
    // Documento capturado pero aún sin consulta exitosa: aviso para habilitar Continuar.
    if (
      actor.numeroDocumento.trim() &&
      (runtState.status === 'idle' || showErrors)
    ) {
      return (
        <div
          className="rounded-xl p-3 text-xs border opacity-90"
          style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)', color: '#162744' }}
          role="status"
          aria-live="polite"
        >
          Consulta {channel} para traer la información. Hasta que la consulta sea exitosa no se
          habilita Continuar.
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
    const runtState: LookupState = runt[index] ?? { status: 'idle' };
    const rlErrors = showErrors ? validation.byActor[index] : {};
    // P5 — si el actor fue precargado desde el directorio, el RL ya está rellenado.
    const isPreloaded = runtState.status === 'found' && runtState.kind === 'preload';

    // Solo el cambio de documento (número/tipo) del RL invalida la consulta RUES/directorio
    // y pide volver a consultar. Nombre, correo, teléfono y demás datos básicos se pueden
    // editar sin perder la consulta vigente.
    const handleRlIdentityDocChange = (patch: Partial<RepresentanteLegal>) => {
      const identityDocChanged =
        patch.numeroDocumento !== undefined || patch.tipoDocumento !== undefined;
      if (isPreloaded && identityDocChanged) {
        updateRepLegal(index, { ...patch, mecanismoFirma: undefined });
        setRuntFor(index, { status: 'idle' });
        setRlRuntFor(index, { status: 'idle' });
        return;
      }
      updateRepLegal(index, patch);
    };

    return (
      <div className="sm:col-span-2 space-y-3">
        <WizardCardHeader
          title="Representante legal y/o apoderado"
          level="h4"
          subtitle={
            isPreloaded
              ? 'Datos precargados desde el directorio / RUES. Puedes editarlos si es necesario.'
              : 'Persona natural que representa a la empresa. Puedes consultarla en el RUNT o registrarla manualmente.'
          }
        />
        {/* Rejilla en el lenguaje de MatriculaInicial.tsx (Step2 → bloque de representante legal):
            documento + botón a la misma altura ("items-end") y un hueco donde el diseño deja aire
            antes del nombre. Los mensajes de estado de la consulta van a ancho completo debajo de la
            fila — con `items-end` pegarlos a la celda del documento habría desalineado el botón. */}
        <div className="grid grid-cols-1 lg:grid-cols-4 gap-4 items-end">
          {/* Tipo de documento (natural, sin NIT) */}
          <div>
            <label htmlFor={`${index}-rl-tipoDoc`} className={WIZARD_LABEL}>
              Tipo de documento
            </label>
            <select
              id={`${index}-rl-tipoDoc`}
              value={rl.tipoDocumento ?? 'CC'}
              onChange={(e) =>
                handleRlIdentityDocChange({ tipoDocumento: e.target.value as ActorDocumentType })
              }
              className={`${INPUT_BASE} mt-1.5`}
            >
              {RL_DOC_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          {/* Número (P5: el botón cambia de texto cuando ya viene precargado desde el directorio) */}
          <div>
            <label htmlFor={`${index}-rl-numeroDoc`} className={WIZARD_LABEL}>
              Número de documento
            </label>
            <input
              id={`${index}-rl-numeroDoc`}
              type="text"
              value={rl.numeroDocumento ?? ''}
              onChange={(e) => handleRlIdentityDocChange({ numeroDocumento: e.target.value })}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault();
                  conVelo(handleRlLookup(index));
                }
              }}
              className={`${INPUT_BASE} mt-1.5 font-mono`}
            />
          </div>
          {!readOnly && !isPreloaded && (
            <button
              type="button"
              onClick={() => conVelo(handleRlLookup(index))}
              disabled={rlState.status === 'loading' || !(rl.numeroDocumento ?? '').trim() || !instanceId}
              className="h-[42px] shrink-0 rounded-xl px-3 text-xs font-semibold border disabled:opacity-50"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
            >
              {rlState.status === 'loading' ? 'Consultando…' : 'Consultar RUNT'}
            </button>
          )}
          {!readOnly && isPreloaded && (
            <button
              type="button"
              onClick={() => conVelo(handleRlLookup(index))}
              disabled={rlState.status === 'loading' || !(rl.numeroDocumento ?? '').trim() || !instanceId}
              // Secundario en navy y a plena opacidad. Antes iba en gris al 60%, que sobre blanco no
              // llega a AA: para restar peso a una acción el sistema usa el botón secundario, no
              // atenuar el texto — atenuado se lee como deshabilitado y encima deja de leerse.
              className="h-[42px] shrink-0 rounded-xl border px-3 text-xs font-semibold disabled:opacity-50"
              style={{ borderColor: '#162744', color: '#162744' }}
              title="Datos ya precargados. Consulta RUNT solo si necesitas actualizar."
            >
              {rlState.status === 'loading' ? 'Consultando…' : 'Actualizar RUNT'}
            </button>
          )}
          <div className="hidden lg:block" aria-hidden="true" />
          {(rlState.status === 'found' || rlState.status === 'not_found' || rlState.status === 'error') && (
            <div className="lg:col-span-4">
              {rlState.status === 'found' && (
                <p className="text-xs" style={{ color: INLINE_ALERT_TONES.info.color }}>
                  Representante encontrado en RUNT.
                </p>
              )}
              {rlState.status === 'not_found' && (
                <p className="text-xs opacity-70">
                  No se encontró en RUNT — completa los datos manualmente.
                </p>
              )}
              {rlState.status === 'error' && (
                <p className="text-xs" style={{ color: '#FF4E00' }}>
                  No se pudo consultar RUNT. Puedes registrarlo manualmente.
                </p>
              )}
            </div>
          )}
          {/* Nombre */}
          <div className="lg:col-span-2">
            <label htmlFor={`${index}-rl-nombre`} className={WIZARD_LABEL}>
              Nombre completo del representante
            </label>
            <input
              id={`${index}-rl-nombre`}
              type="text"
              value={rl.nombreCompleto ?? ''}
              onChange={(e) => updateRepLegal(index, { nombreCompleto: e.target.value })}
              className={`${INPUT_BASE} mt-1.5`}
            />
          </div>
          {/* Email — obligatorio en persona jurídica (HU #10688): el RL valida la identidad. */}
          <div>
            <label htmlFor={`${index}-rl-email`} className={WIZARD_LABEL}>
              Correo electrónico <span style={{ color: '#FF4E00' }}>*</span>
            </label>
            <input
              id={`${index}-rl-email`}
              type="email"
              value={rl.email ?? ''}
              onChange={(e) => updateRepLegal(index, { email: e.target.value })}
              placeholder="correo@ejemplo.com"
              className={`${INPUT_BASE} mt-1.5`}
              aria-invalid={!!rlErrors.representanteLegal}
            />
            {rlErrors.representanteLegal && (
              <p className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                {rlErrors.representanteLegal}
              </p>
            )}
          </div>
          {/* Teléfono */}
          <div>
            <label htmlFor={`${index}-rl-telefono`} className={WIZARD_LABEL}>
              Teléfono <span className="font-normal">(opcional)</span>
            </label>
            <input
              id={`${index}-rl-telefono`}
              type="tel"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="tel"
              value={rl.telefono ?? ''}
              onChange={(e) => updateRepLegal(index, { telefono: e.target.value })}
              className={`${INPUT_BASE} mt-1.5`}
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
        <label htmlFor={`${actor.rol}-fechaExpedicion`} className={`${WIZARD_LABEL} mb-1.5`}>
          Fecha de expedición del documento{' '}
          <span className="font-normal">(opcional)</span>
        </label>
        <input
          id={`${actor.rol}-fechaExpedicion`}
          type="date"
          value={issueDates[index] ?? ''}
          onChange={(e) => {
            markDirty();
            setIssueDates((prev) => ({ ...prev, [index]: e.target.value }));
          }}
          className={INPUT_BASE}
        />
        <p className="text-xs mt-1 opacity-60">
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
        <p className="text-xs opacity-70" role="status" aria-live="polite">
          Buscando datos de contacto conocidos de esta persona…
        </p>
      );
    }
    if (c.status === 'found') {
      return (
        <p
          className="text-xs"
          style={{ color: INLINE_ALERT_TONES.info.color }}
          role="status"
          aria-live="polite"
        >
          Contacto precargado desde un trámite anterior de esta persona en la compañía — puedes
          editarlo.
        </p>
      );
    }
    if (c.status === 'error') {
      return (
        <p className="text-xs opacity-70" role="status" aria-live="polite">
          No se pudo precargar el contacto conocido — completa los datos manualmente.
        </p>
      );
    }
    // idle/empty (AC4: sin antecedentes) — sin aviso, sin error; los campos siguen vacíos y editables.
    return null;
  };

  // Velo de espera SOLO para las consultas que dispara el gestor. Colgarlo de "hay una consulta en
  // curso" tapaba la pantalla al entrar al paso: en traspaso el vendedor se consulta solo al montar
  // (autoConsultRunt), y el velo aparecía en cada visita sin que nadie hubiera pedido nada. Una
  // espera que el usuario no provocó no se anuncia tapándole la pantalla.
  // Se declara ANTES de los dos returns: el layout partido sale antes y también lo necesita.
  const consultandoActor = consultasManuales > 0;

  // ── Layout SPLIT (un comprador): 2 secciones ──────────────────────────────
  if (isSplit && actors.length === 1) {
    const actor = actors[0];
    const errors = showErrors ? validation.byActor[0] : {};
    const runtState: LookupState = runt[0] ?? { status: 'idle' };
    const docLocked = autoConsultRunt && !!actor.numeroDocumento.trim();
    const razonLocked = isRazonSocialLocked(actor, 0);
    const ciudades = filterCiudades(actor.ciudad ?? '');
    const showCiudades = !!ciudadOpen[0] && ciudades.length > 0;
    // Novedad nov.41 — este actor es el que recibe la precarga silenciosa del documento del
    // propietario (paso 1). Solo aplica al vendedor del traspaso.
    const seedingOwnerDoc = seedDocumentoFromOwner && actor.rol === 'vendedor';
    return (
      <>
      {/* El velo va en los DOS layouts. Estaba solo en el de traspaso, que es el que cierra el
          componente, así que al consultar desde matrícula —el layout partido, que retorna aquí—
          no aparecía nunca. */}
      {consultandoActor && <CarLoaderModal mode="runt" />}
      <form
        onSubmit={handleSubmit}
        aria-label="Captura de actores del trámite"
        noValidate
      >
       <fieldset disabled={readOnly} className="space-y-5 min-w-0 border-0 p-0 m-0">
        {errorBanner}

        {/* Sección A — Identificación. Cabecera con el átomo compartido (WizardCardHeader) y la
            rejilla de 4 columnas de MatriculaInicial.tsx (Step2, diseño de referencia): documento +
            botón de consulta a la misma altura ("items-end"), y la nota explicativa a ancho completo
            debajo. Sigue sin ofrecer un selector de tipo de documento: en persona natural el tipo
            viene de la validación de identidad (RUNT), no se captura a mano (nota bajo el
            segmentado); en jurídica el documento siempre es NIT. */}
        <section className={WIZARD_CARD}>
          <WizardCardHeader
            title={`Datos del ${ROL_LABEL[actor.rol].toLowerCase()}`}
            subtitle={
              actor.rol === 'vendedor'
                ? 'Registra la persona natural o jurídica que figura hoy como propietaria del vehículo.'
                : 'Registra la persona natural o jurídica que figurará como propietaria del vehículo.'
            }
          />
          <div className="space-y-3">
            {personTypeSelector(0)}
            <div className="grid grid-cols-1 lg:grid-cols-4 gap-4 items-end">
              {/* Tipo de documento. Faltaba en este layout: el actor nacía con 'CC' fijo y la
                  consulta salía al RUNT con ese tipo, así que un comprador con cédula de
                  extranjería o pasaporte se consultaba como si fuera cédula de ciudadanía. No era
                  una diferencia de diseño entre modalidades: era un dato que en matrícula no se
                  podía capturar. En persona jurídica el documento es siempre NIT y el selector
                  sobra, igual que en el layout de traspaso. */}
              {!isJuridical(actor) && (
                <div>
                  <label htmlFor="comprador-tipoDoc" className={`${WIZARD_LABEL} mb-1.5`}>
                    Tipo de documento
                  </label>
                  <select
                    id="comprador-tipoDoc"
                    value={actor.tipoDocumento}
                    onChange={(e) =>
                      updateActor(0, { tipoDocumento: e.target.value as ActorDocumentType })
                    }
                    disabled={docLocked}
                    className={INPUT_BASE}
                  >
                    {DOC_OPTIONS.filter((o) => o.value !== 'NIT').map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <div className={isJuridical(actor) ? 'lg:col-span-3' : 'lg:col-span-2'}>
                <input
                  id="comprador-numeroDoc"
                  type="text"
                  value={actor.numeroDocumento}
                  readOnly={docLocked || (seedingOwnerDoc && ownerSeedStatus === 'loading')}
                  onChange={(e) => updateActor(0, { numeroDocumento: e.target.value })}
                  onKeyDown={(e) => {
                    if (docLocked) return;
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      conVelo(handleIdentityLookup(0));
                    }
                  }}
                  aria-label="Número de documento"
                  aria-invalid={!!errors.numeroDocumento}
                  aria-describedby={
                    errors.numeroDocumento
                      ? 'comprador-numeroDoc-err'
                      : seedingOwnerDoc && ownerSeedStatus !== 'idle'
                        ? 'comprador-numeroDoc-seed-status'
                        : undefined
                  }
                  aria-busy={seedingOwnerDoc && ownerSeedStatus === 'loading'}
                  placeholder={`Número de documento del ${actor.rol}…`}
                  className={`${INPUT_BASE} font-mono${docLocked ? ' opacity-80' : ''}`}
                  style={docLocked ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                />
                {errors.numeroDocumento && (
                  <p id="comprador-numeroDoc-err" className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                    {errors.numeroDocumento}
                  </p>
                )}
                {/* Novedad nov.41 — indicador de la precarga silenciosa del documento del
                    propietario. Un `role="status"` inline (no modal): es una espera corta de
                    fondo, no una consulta que el gestor disparó a mano (esa usa CarLoaderModal). */}
                {seedingOwnerDoc && ownerSeedStatus === 'loading' && (
                  <p
                    id="comprador-numeroDoc-seed-status"
                    role="status"
                    aria-live="polite"
                    aria-busy="true"
                    className="mt-1 flex items-center gap-1.5 text-xs opacity-70"
                  >
                    <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin" aria-hidden="true" />
                    Cargando el documento del propietario…
                  </p>
                )}
                {seedingOwnerDoc && ownerSeedStatus === 'error' && (
                  <p
                    id="comprador-numeroDoc-seed-status"
                    role="alert"
                    className="mt-1 flex items-center gap-1.5 text-xs"
                    style={{ color: '#FF4E00' }}
                  >
                    <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    No se pudo cargar el documento del propietario. Escríbelo manualmente o{' '}
                    <button
                      type="button"
                      onClick={() => setOwnerSeedRetry((n) => n + 1)}
                      className="font-semibold underline"
                    >
                      reintenta
                    </button>
                    .
                  </p>
                )}
              </div>
              {!readOnly && !autoConsultRunt && (
                <button
                  type="button"
                  onClick={() => conVelo(handleIdentityLookup(0))}
                  disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                  className="flex h-[42px] shrink-0 items-center justify-center gap-2 rounded-xl px-5 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                  style={{ background: WIZARD_CTA_GRADIENT }}
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
              <p className="text-xs opacity-70 lg:col-span-4">
                {isJuridical(actor)
                  ? `Consultamos el RUES para validar el registro mercantil del ${actor.rol} y precargar la razón social.`
                  : `Consultamos el RUNT para validar la identidad del ${actor.rol} y precargar sus datos.`}
              </p>
            </div>
            {runtResult(0)}
            {/* P4 — para persona jurídica el RL se mueve DESPUÉS de los datos de la empresa. */}
          </div>
        </section>

        {/* Sección B — Datos de contacto */}
        <section className={WIZARD_CARD}>
          <WizardCardHeader
            title="Datos de contacto"
            subtitle="Confirma o edita la información de notificación del propietario."
          />
          <div className="text-xs opacity-70">{contactLookupHint(0)}</div>
          <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
            {/* Nombre / razón social */}
            <div className="lg:col-span-2">
              <label htmlFor="comprador-nombre" className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                {isJuridical(actor) ? 'Razón social' : 'Nombre completo'}
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
              </label>
              <input
                id="comprador-nombre"
                type="text"
                value={actor.nombreCompleto}
                onChange={(e) => updateActor(0, { nombreCompleto: e.target.value })}
                readOnly={razonLocked}
                aria-invalid={!!errors.nombreCompleto}
                aria-describedby={errors.nombreCompleto ? 'comprador-nombre-err' : undefined}
                className={`${INPUT_BASE}${razonLocked ? ' opacity-80' : ''}`}
                style={razonLocked ? { background: 'rgba(223,229,237,0.35)' } : undefined}
              />
              {errors.nombreCompleto && (
                <p id="comprador-nombre-err" className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                  {errors.nombreCompleto}
                </p>
              )}
            </div>
            {/* Documento (readonly — viene de la consulta) */}
            <div>
              <label htmlFor="comprador-doc-ro" className={`${WIZARD_LABEL} mb-1.5`}>
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
              <label htmlFor="comprador-email" className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
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
                <p id="comprador-email-err" className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                  {errors.email}
                </p>
              )}
            </div>
            {/* Teléfono */}
            <div>
              <label htmlFor="comprador-telefono" className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                Teléfono
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
              </label>
              <input
                id="comprador-telefono"
                type="tel"
                inputMode="numeric"
                pattern="[0-9]*"
                autoComplete="tel"
                required
                aria-required="true"
                value={actor.telefono ?? ''}
                onChange={(e) => {
                  markContactTouched(0, 'telefono');
                  updateActor(0, { telefono: e.target.value });
                }}
                placeholder="3001234567"
                aria-invalid={!!errors.telefono}
                aria-describedby={errors.telefono ? 'comprador-telefono-err' : undefined}
                className={INPUT_BASE}
              />
              {errors.telefono && (
                <p id="comprador-telefono-err" className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                  {errors.telefono}
                </p>
              )}
            </div>
            {/* Fecha de expedición del documento (RNMC, solo persona natural) */}
            {issueDateField(0)}
            {/* Ciudad (autocomplete) */}
            <div className={`relative ${showCiudades ? 'z-40' : ''}`}>
              <label htmlFor="comprador-ciudad" className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                Ciudad
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
              </label>
              <input
                id="comprador-ciudad"
                type="text"
                required
                aria-required="true"
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
                aria-invalid={!!errors.ciudad}
                aria-describedby={errors.ciudad ? 'comprador-ciudad-err' : undefined}
                className={INPUT_BASE}
              />
              {errors.ciudad && (
                <p id="comprador-ciudad-err" className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                  {errors.ciudad}
                </p>
              )}
              {showCiudades && (
                <ul
                  className="absolute left-0 right-0 top-full z-[100] mt-1 max-h-48 overflow-auto rounded-xl border bg-white shadow-lg dark:bg-[#162744]"
                  style={{ borderColor: '#DFE5ED' }}
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
            <div className="lg:col-span-3">
              <label htmlFor="comprador-direccion" className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                Dirección
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
              </label>
              <input
                id="comprador-direccion"
                type="text"
                required
                aria-required="true"
                value={actor.direccion ?? ''}
                onChange={(e) => {
                  markContactTouched(0, 'direccion');
                  updateActor(0, { direccion: e.target.value });
                }}
                aria-invalid={!!errors.direccion}
                aria-describedby={errors.direccion ? 'comprador-direccion-err' : undefined}
                className={INPUT_BASE}
              />
              {errors.direccion && (
                <p id="comprador-direccion-err" className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                  {errors.direccion}
                </p>
              )}
            </div>
          </div>
        </section>

        {/* Sección C — Representante legal (P4: solo persona jurídica, después de contacto empresa).
            `rlSection` ya monta su propia cabecera (título + nota); esta envoltura solo aporta la
            tarjeta y el espaciado consistentes con las secciones A y B. */}
        {isJuridical(actor) && <section className={WIZARD_CARD}>{rlSection(0)}</section>}

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

  // ── Layout MULTI (traspaso): una tarjeta blanca por actor, lado a lado ────
  return (
    <>
    {consultandoActor && <CarLoaderModal mode="runt" />}
    <form
      onSubmit={handleSubmit}
      className="mt-4"
      aria-label="Captura de actores del trámite"
      noValidate
    >
     <fieldset disabled={readOnly} className="contents">
      {/* Embebido en el wizard el título del paso lo pinta la shell (h2); aquí un segundo
          título sería redundante, así que se omite. Standalone, este título cuelga
          directamente de la shell de la página: arranca en h3 (ver representante legal, h4,
          más abajo — antes invertido: un h4 aquí hacía de padre de ese h3). */}
      {!embeddedInWizard && (
        <WizardCardHeader
          title="Actores del trámite"
          subtitle={
            modalidad === 'matricula_inicial'
              ? 'Registra los datos del comprador (propietario inicial).'
              : 'Registra los datos del vendedor y del comprador.'
          }
        />
      )}

      {errorBanner}

      <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
        {actors.map((actor, index) => {
          const errors = showErrors ? validation.byActor[index] : {};
          const prefix = `actor-${actor.rol}`;
          const runtState: LookupState = runt[index] ?? { status: 'idle' };
          const razonLocked = isRazonSocialLocked(actor, index);
          const docLocked =
            autoConsultRunt &&
            actor.rol === 'vendedor' &&
            !!actor.numeroDocumento.trim();
          // Novedad nov.41 — este actor es el que recibe la precarga silenciosa del documento
          // del propietario (paso 1). Solo aplica al vendedor del traspaso.
          const seedingOwnerDoc = seedDocumentoFromOwner && actor.rol === 'vendedor';
          // HU #10956 — ciudad con autocomplete, misma lógica que el layout SPLIT pero por índice.
          const ciudadesSuggestions = filterCiudades(actor.ciudad ?? '');
          const showCiudadSuggestions = !!ciudadOpen[index] && ciudadesSuggestions.length > 0;
          // Píldora de estado de la cabecera (WizardTramite.tsx, ActorPanel): refleja la consulta de
          // identidad que YA se rastrea en `runt[index]`, sin inventar un dato nuevo (nada de puntaje).
          // Tintada (StatusBadge) y no de relleno sólido: la propuesta la trae sólida con texto
          // blanco, y con los tonos de marca en crudo ninguno de los tres estados de color llega a
          // AA (#8CC63F 2.05:1, #FF4E00 3.31:1, #557EFF 3.61:1). El sistema ya resuelve esto con
          // tonos semánticos, así que el estado se dice por tono Y por texto.
          const statusPill: { text: string; tone: StatusTone } =
            runtState.status === 'found'
              ? { text: 'Verificado', tone: 'success' }
              : runtState.status === 'loading'
                ? { text: 'Consultando…', tone: 'info' }
                : runtState.status === 'not_found' || runtState.status === 'error'
                  ? { text: 'No verificado', tone: 'danger' }
                  : { text: 'Pendiente', tone: 'neutral' };
          return (
            <fieldset
              key={actor.rol}
              className="rounded-2xl border bg-white dark:bg-[#162744]"
              style={{ borderColor: '#DFE5ED' }}
              aria-label={ROL_LABEL[actor.rol]}
            >
              <legend className="sr-only">{ROL_LABEL[actor.rol]}</legend>
              <div
                className="border-b px-4 py-3"
                style={{ borderColor: '#DFE5ED', background: 'rgba(85,126,255,0.04)' }}
              >
                {/* Subordinado al título "Actores del trámite" (h3 standalone, h2 sr-only
                    embebido): arranca en h4, igual que el representante legal que puede vivir
                    dentro de esta misma tarjeta. */}
                <WizardCardHeader
                  title={ROL_LABEL[actor.rol]}
                  level="h4"
                  className=""
                  action={<StatusBadge label={statusPill.text} tone={statusPill.tone} />}
                />
              </div>
              <div className="grid grid-cols-1 gap-3 p-4 sm:grid-cols-2">
                {/* Tipo de persona (HU #10543) */}
                <div className="sm:col-span-2">{personTypeSelector(index)}</div>
                {/* Tipo de documento */}
                <div>
                  <label htmlFor={`${prefix}-tipoDoc`} className={`${WIZARD_LABEL} mb-1.5`}>
                    Tipo de documento
                  </label>
                  <select
                    id={`${prefix}-tipoDoc`}
                    value={actor.tipoDocumento}
                    onChange={(e) => updateActor(index, { tipoDocumento: e.target.value as ActorDocumentType })}
                    disabled={docLocked}
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
                  <label htmlFor={`${prefix}-numeroDoc`} className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                    Número de documento
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-numeroDoc`}
                    type="text"
                    value={actor.numeroDocumento}
                    onChange={(e) => updateActor(index, { numeroDocumento: e.target.value })}
                    readOnly={docLocked || (seedingOwnerDoc && ownerSeedStatus === 'loading')}
                    aria-invalid={!!errors.numeroDocumento}
                    aria-describedby={
                      errors.numeroDocumento
                        ? `${prefix}-numeroDoc-err`
                        : seedingOwnerDoc && ownerSeedStatus !== 'idle'
                          ? `${prefix}-numeroDoc-seed-status`
                          : undefined
                    }
                    aria-busy={seedingOwnerDoc && ownerSeedStatus === 'loading'}
                    className={`${INPUT_BASE}${docLocked ? ' opacity-80' : ''}`}
                    style={docLocked ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                  />
                  {errors.numeroDocumento && (
                    <p id={`${prefix}-numeroDoc-err`} className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                      {errors.numeroDocumento}
                    </p>
                  )}
                  {/* Novedad nov.41 — indicador de la precarga silenciosa del documento del
                      propietario (velo NO modal: es una espera corta de fondo). */}
                  {seedingOwnerDoc && ownerSeedStatus === 'loading' && (
                    <p
                      id={`${prefix}-numeroDoc-seed-status`}
                      role="status"
                      aria-live="polite"
                      aria-busy="true"
                      className="mt-1 flex items-center gap-1.5 text-xs opacity-70"
                    >
                      <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin" aria-hidden="true" />
                      Cargando el documento del propietario…
                    </p>
                  )}
                  {seedingOwnerDoc && ownerSeedStatus === 'error' && (
                    <p
                      id={`${prefix}-numeroDoc-seed-status`}
                      role="alert"
                      className="mt-1 flex items-center gap-1.5 text-xs"
                      style={{ color: '#FF4E00' }}
                    >
                      <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                      No se pudo cargar el documento del propietario. Escríbelo manualmente o{' '}
                      <button
                        type="button"
                        onClick={() => setOwnerSeedRetry((n) => n + 1)}
                        className="font-semibold underline"
                      >
                        reintenta
                      </button>
                      .
                    </p>
                  )}
                  {/* Consultar identidad: RUES (jurídica) o RUNT (natural). Autopobla el actor. */}
                  {!readOnly && !docLocked && (
                    <button
                      type="button"
                      onClick={() => conVelo(handleIdentityLookup(index))}
                      disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                      className="mt-2 px-3 py-1.5 rounded-xl text-xs font-semibold border disabled:opacity-50"
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

                {/* Nombre / razón social */}
                <div className="sm:col-span-2">
                  <label htmlFor={`${prefix}-nombre`} className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                    {isJuridical(actor) ? 'Razón social' : 'Nombre completo'}
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-nombre`}
                    type="text"
                    value={actor.nombreCompleto}
                    onChange={(e) => updateActor(index, { nombreCompleto: e.target.value })}
                    readOnly={razonLocked}
                    aria-invalid={!!errors.nombreCompleto}
                    aria-describedby={errors.nombreCompleto ? `${prefix}-nombre-err` : undefined}
                    className={`${INPUT_BASE}${razonLocked ? ' opacity-80' : ''}`}
                    style={razonLocked ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                  />
                  {errors.nombreCompleto && (
                    <p id={`${prefix}-nombre-err`} className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                      {errors.nombreCompleto}
                    </p>
                  )}
                </div>

                {/* Email */}
                <div>
                  <label htmlFor={`${prefix}-email`} className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
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
                    <p id={`${prefix}-email-err`} className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                      {errors.email}
                    </p>
                  )}
                </div>

                {/* Teléfono */}
                <div>
                  <label htmlFor={`${prefix}-telefono`} className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                    Teléfono
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-telefono`}
                    type="tel"
                    inputMode="numeric"
                    pattern="[0-9]*"
                    autoComplete="tel"
                    required
                    aria-required="true"
                    value={actor.telefono ?? ''}
                    onChange={(e) => {
                      markContactTouched(index, 'telefono');
                      updateActor(index, { telefono: e.target.value });
                    }}
                    aria-invalid={!!errors.telefono}
                    aria-describedby={errors.telefono ? `${prefix}-telefono-err` : undefined}
                    className={INPUT_BASE}
                  />
                  {errors.telefono && (
                    <p id={`${prefix}-telefono-err`} className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                      {errors.telefono}
                    </p>
                  )}
                </div>

                {/* Ciudad (autocomplete) — HU #10956, precargable desde el contacto ya conocido. */}
                <div className={`relative ${showCiudadSuggestions ? 'z-40' : ''}`}>
                  <label htmlFor={`${prefix}-ciudad`} className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                    Ciudad
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-ciudad`}
                    type="text"
                    required
                    aria-required="true"
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
                    aria-invalid={!!errors.ciudad}
                    aria-describedby={errors.ciudad ? `${prefix}-ciudad-err` : undefined}
                    className={INPUT_BASE}
                  />
                  {errors.ciudad && (
                    <p id={`${prefix}-ciudad-err`} className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                      {errors.ciudad}
                    </p>
                  )}
                  {showCiudadSuggestions && (
                    <ul
                      className="absolute left-0 right-0 top-full z-[100] mt-1 max-h-48 overflow-auto rounded-xl border bg-white shadow-lg dark:bg-[#162744]"
                      style={{ borderColor: '#DFE5ED' }}
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

                {/* Dirección — HU #10956 (precargable desde el contacto ya conocido), obligatoria
                    desde la HU #11595. */}
                <div>
                  <label htmlFor={`${prefix}-direccion`} className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                    Dirección
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-direccion`}
                    type="text"
                    required
                    aria-required="true"
                    value={actor.direccion ?? ''}
                    onChange={(e) => {
                      markContactTouched(index, 'direccion');
                      updateActor(index, { direccion: e.target.value });
                    }}
                    aria-invalid={!!errors.direccion}
                    aria-describedby={errors.direccion ? `${prefix}-direccion-err` : undefined}
                    className={INPUT_BASE}
                  />
                  {errors.direccion && (
                    <p id={`${prefix}-direccion-err`} className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                      {errors.direccion}
                    </p>
                  )}
                </div>

                {/* Aviso de precarga de contacto (HU #10956) — no bloqueante, no reemplaza al badge de
                    origen de la consulta externa (`originBadge`, dentro de runtResult). */}
                {contactLookupHint(index) && (
                  <div className="sm:col-span-2">{contactLookupHint(index)}</div>
                )}

                {/* Fecha de expedición del documento (RNMC, solo persona natural) */}
                {issueDateField(index)}

                {/* P4 — Representante legal DESPUÉS de los datos de contacto de la empresa (persona jurídica). */}
                {rlSection(index)}

                {/* Al pie: bloque enmarcado con la validación de identidad (RUNT/RUES) y, cuando
                    aplica, la firma asociada — mismos badges de `runtResult`, sin datos nuevos. */}
                {runt[index] && runt[index].status !== 'idle' && (
                  <div
                    className="sm:col-span-2 rounded-xl border p-3"
                    style={{ borderColor: '#DFE5ED' }}
                  >
                    {runtResult(index)}
                  </div>
                )}
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
 * NO se envía. Foco atrapado dentro del panel (Tab/Shift+Tab cicla entre Cancelar/Continuar) y
 * devuelto al disparador al cerrar (`useWizardFocusTrap`, B5 guardián de diseño — la trampa vivía
 * aquí a mano y le faltaba justamente el retorno de foco); Escape/backdrop del `Modal` compartido
 * equivalen a "Cancelar".
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

  useWizardFocusTrap(containerRef, { active: true, onEscape: onCancel, initialFocusRef: cancelRef });

  return (
    <Modal
      open
      onClose={onCancel}
      title="Reenviar validación de identidad"
      icon={AlertTriangle}
      iconBg="#B26A00"
      size="sm"
    >
      <div ref={containerRef} className="space-y-4">
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

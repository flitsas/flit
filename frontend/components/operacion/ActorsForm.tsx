'use client';

import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from 'react';
import { AlertTriangle, Info, Loader2, Search } from 'lucide-react';
import { InlineAlert, INLINE_ALERT_TONES, type InlineAlertTone } from '@/components/atom/InlineAlert';
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
import { shortRuesRazonSocial } from '@/lib/tramites/rues-razon-social';
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
  LegalRepresentativeLookupCompany,
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
  WIZARD_SELECT,
  WIZARD_LABEL,
  WIZARD_BTN,
  WIZARD_CARD,
  WIZARD_CTA_GRADIENT,
  WIZARD_BTN_SOLID,
} from './wizard-field-styles';
import { EscrituraRepresentanteUpload } from './EscrituraRepresentanteUpload';
import { WizardCardHeader } from './wizard-atoms';
import { cn } from '@/lib/utils';
import { WizardAccordion, WizardAccordionRow } from './WizardAccordion';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { OwnershipTabsBar, OwnershipPercentagePanel, type OwnershipShareItem } from './OwnershipShareControl';
import {
  MAX_OWNERS_PER_SIDE,
  applySolidarioAbsorption,
  defaultPercentageForNewActor,
  duplicateDocumentIndicesWithinSide,
  indicesForRol,
  isFirstOfRol,
  redistributeAfterRemoval,
  round2,
  shiftIndexMapOnInsert,
  shiftIndexMapOnRemove,
  validateOwnershipShares,
  withOwnershipFields,
} from '@/lib/tramites/ownership-share';

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
   * Rol que ES el propietario inscrito en el RUNT y por tanto recibe la siembra y la consulta
   * automática. `vendedor` en el traspaso (el propietario que sale); `comprador` donde no hay parte
   * vendedora y el vehículo ya está matriculado (familia OTROS: el titular no vende ni compra, solo
   * hace cambios sobre su vehículo, y se persiste como comprador porque el modelo no tiene rol
   * 'propietario').
   *
   * ADR-0051 — `null` cuando NINGÚN rol pintado en pantalla es el propietario inscrito: hay parte
   * vendedora pero no se captura por formulario en este paso (`TRASPASO_UNILATERAL`, sincronizada
   * aparte por el backend desde el RUNT). Pasarle un rol aquí que no es de verdad el propietario
   * —p. ej. `comprador` cuando ese comprador es el locatario, no quien figura en el RUNT— sería
   * sembrarle a esa persona el documento de otra: el traspaso encubierto que este componente existe
   * para impedir. Default `vendedor` (comportamiento previo, para quien use este componente fuera
   * del wizard sin declarar la prop). Dentro del wizard, `TramiteWizard.tsx` la pasa siempre
   * explícita —incluida `null`— así que este default no gobierna el paso `actores`.
   */
  rolDelPropietario?: ActorRol | null;
  /**
   * Paso del propietario: si ya hay número de documento (seed o rehidratación), consulta RUNT al
   * montar, oculta el botón manual y deja el documento en solo lectura.
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
  /**
   * Gate de avance del paso: `false` mientras alguna parte jurídica tenga un representante legal que
   * NO está en el módulo de representantes de la compañía y todavía no haya cargado la escritura que
   * lo acredita. Sin esa escritura no se pasa a Requisitos.
   *
   * <p>Es un gate propio y no una variante de {@link onConsultationGateChange}: aquel responde si la
   * identidad de la parte se pudo CONSULTAR, y este si el representante capturado está ACREDITADO.
   * Un representante fuera del directorio puede consultarse en el RUNT sin problema —de hecho es el
   * camino normal para capturarlo— y aun así seguir sin escritura que lo faculte.</p>
   */
  onEscrituraRepresentanteGateChange?: (ready: boolean) => void;
  /**
   * Gate del paso: ¿están completos los campos OBLIGATORIOS de todas las partes que captura este
   * paso? Lo consume la shell para deshabilitar "Continuar y guardar". Antes el botón estaba
   * siempre activo y el bloqueo llegaba DESPUÉS del clic —`save()` devolvía false y marcaba los
   * campos—: el gestor pulsaba, no pasaba nada visible en el pie y tenía que buscar por su cuenta
   * qué faltaba.
   */
  onCamposRequeridosGateChange?: (ready: boolean) => void;
  /**
   * Rótulo del catálogo para un rol concreto: el `label` que el tipo le dio al paso que lo captura.
   *
   * <p>El rol persistido no siempre se llama como la parte real. `TRASPASO_UNILATERAL` guarda al
   * locatario del leasing con el rol `comprador` —no hay rol propio para una única parte entrante, y
   * cambiarlo movería el gate, la biometría y el FUR—, pero «Comprador» describe un contrato que ahí
   * no existe: en un leasing nadie compra. El catálogo ya nombra ese paso «Locatario»; esta prop es
   * lo que hace que ese nombre llegue a la tarjeta.</p>
   *
   * <p>Va por ROL y no como «rótulo de la parte única» porque la pantalla puede traer dos tarjetas y
   * el nombre del paso solo describe a una: en el unilateral con el formulario del propietario
   * revelado, «Locatario» nombra al comprador y el propietario conserva el suyo. Los roles sin
   * entrada caen a {@link ROL_LABEL} (y a la regla de `hayLocatario`), que es el comportamiento
   * previo.</p>
   */
  rotuloPorRol?: Partial<Record<ActorRol, string>>;
}

/** ¿La consulta de identidad resolvió datos? Gate duro de avance/guardado. */
export function isIdentityConsultationReady(
  status: 'idle' | 'loading' | 'found' | 'not_found' | 'error' | undefined,
): boolean {
  return status === 'found';
}

/**
 * Documentos que puede declarar un actor. Es el ÚNICO control de la naturaleza de la persona:
 * NIT ⇒ jurídica (se consulta el RUES), cualquier otro ⇒ natural (se consulta el RUNT).
 *
 * <p>La tarjeta de identidad NO está: ninguno de los dos proveedores de conductor la consulta.
 * Verifik solo admite <c>CC · CE · PA · PPT</c> en <c>/v2/co/runt/conductor</c> (no existe un valor
 * para TI), y aunque Kyverum sí tiene el código <c>T</c>, nunca lo recibe porque el orquestador le
 * entrega el documento ya traducido al dialecto de Verifik — donde TI viaja como <c>PPT</c> y su
 * normalizador lo vuelve <c>P</c>, pasaporte. Ofrecerla solo servía para trabar el paso: la consulta
 * no encuentra a nadie y el gate de avance exige consulta exitosa. Si el negocio la pide, primero
 * hay que decidir qué se le manda a cada proveedor.</p>
 */
const DOC_OPTIONS: { value: ActorDocumentType; label: string }[] = [
  { value: 'CC', label: 'Cédula de ciudadanía (CC)' },
  { value: 'CE', label: 'Cédula de extranjería (CE)' },
  { value: 'NIT', label: 'NIT' },
  { value: 'PAS', label: 'Pasaporte (PAS)' },
];

const ROL_LABEL: Record<ActorRol, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
  locatario: 'Locatario',
};

/** Roles requeridos por modalidad. Matrícula = solo comprador; traspaso = ambos. */
function rolesFor(modalidad: ActorsModalidad): ActorRol[] {
  return modalidad === 'matricula_inicial'
    ? ['comprador']
    : ['vendedor', 'comprador'];
}

/**
 * Naturaleza de la persona DERIVADA del documento. Ya no se teclea: el gestor elige el tipo de
 * documento y esto se deduce, que es lo que el backend cree desde siempre — cinco de las reglas que
 * leen `person_type` caen a «NIT ⇒ jurídica» cuando la columna viene vacía.
 */
function personTypeForDoc(tipoDocumento: ActorDocumentType): ActorPersonType {
  return tipoDocumento === 'NIT' ? 'juridical' : 'natural';
}

/** Rótulo de lectura de la naturaleza de la persona (insignia de la tarjeta). */
function personTypeLabel(actor: ProcedureActor): string {
  return isJuridical(actor) ? 'Persona Jurídica' : 'Persona Natural';
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

  // Regla vendedor≠comprador — Múltiple Propietario (ADR-0053 §4.4, dos niveles):
  //
  // Nivel 2 (entre lados): SOLO se evalúa cuando ambos lados quedan en EXACTAMENTE 1 actor
  // efectivo — comportamiento IDÉNTICO al contrato/UI previos, sin tocar ni un carácter del caso
  // 1-a-1 (mayoritario). Con 2+ actores en cualquiera de los dos lados, esta comparación cruzada
  // se OMITE a propósito: un mismo documento puede figurar como vendedor Y como comprador (un
  // copropietario que concurre a la venta y a la vez compra una cuota adicional).
  if (modalidad === 'traspaso') {
    const vendedores = actors.filter((a) => a.rol === 'vendedor');
    const compradores = actors.filter((a) => a.rol === 'comprador');
    if (vendedores.length === 1 && compradores.length === 1) {
      const [vendedor] = vendedores;
      const [comprador] = compradores;
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

  // Nivel 1 (intra-lado): bloqueada SIEMPRE, sin excepción — dos compradores no comparten
  // documento, dos vendedores tampoco ("nadie es copropietario de sí mismo"). Solo puede disparar
  // con 2+ actores por lado (Múltiple Propietario); con el caso 1-a-1 no encuentra nada que marcar.
  for (const index of duplicateDocumentIndicesWithinSide(actors)) {
    byActor[index].numeroDocumento =
      'Ya existe un propietario con este mismo documento en este lado.';
  }

  const valid = byActor.every((e) => Object.keys(e).length === 0);
  return { valid, byActor };
}

/**
 * Normaliza opcionales vacíos a undefined antes de persistir. Múltiple Propietario (ADR-0053) —
 * también agrega `ordinal`/`porcentaje` frescos por posición dentro del lado (`withOwnershipFields`,
 * `lib/tramites/ownership-share.ts`): con un solo actor por lado el resultado es byte a byte el
 * mismo contrato de siempre (`ordinal:1`, `porcentaje:null`).
 */
function normalizeActors(actors: ProcedureActor[]): ProcedureActor[] {
  const blankToUndef = (v?: string) => (v?.trim() ? v.trim() : undefined);
  const withOwnership = withOwnershipFields(actors);
  return withOwnership.map((a) => {
    // HU #10956 (AC1) — el check de reutilización desapareció: el campo NUNCA viaja en el PUT,
    // ni siquiera si un actor persistido ANTES de esta HU lo trae en `true` desde el backend.
    const rest = { ...a };
    delete rest.autorizaReutilizacionDatos;
    return {
      ...rest,
      telefono: blankToUndef(a.telefono),
      ciudad: blankToUndef(a.ciudad),
      direccion: blankToUndef(a.direccion),
      nombreCompleto: isJuridical(a)
        ? shortRuesRazonSocial(a.nombreCompleto) || a.nombreCompleto
        : a.nombreCompleto,
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
  | {
      status: 'found';
      kind: 'rues';
      result: RuesPersonLookupResult;
      /** Datos básicos empresa/RL del directorio OT; la razón social sigue siendo la de RUES. */
      directory?: LegalRepresentativeLookupResult | null;
    }
  // Snapshot viejo (sessionStorage): precarga que cortaba RUES. Se rehidrata, no se vuelve a escribir.
  | { status: 'found'; kind: 'preload'; result: LegalRepresentativeLookupResult }
  | { status: 'not_found' }
  | { status: 'error'; message: string };

function directoryFromLookup(state: LookupState | undefined): LegalRepresentativeLookupResult | null {
  if (!state || state.status !== 'found') return null;
  if (state.kind === 'preload') return state.result;
  if (state.kind === 'rues') return state.directory ?? null;
  return null;
}

/** Solo estados `found` son candidatas a rehidratación al volver al paso. */
type FoundLookupState = Extract<LookupState, { status: 'found' }>;

/**
 * Novedad 28 (AC6) — línea base del documento del RL en el momento en que empezó a divergir de la
 * precarga del directorio (valores PREVIOS al primer cambio de tipo/número). Se registra UNA sola
 * vez por índice de actor; sirve para: (a) detectar si el operador terminó revirtiendo el
 * documento a su valor original, y (b) restituir `mecanismoFirma` si así fue.
 */
type RlBaselineDoc = {
  tipoDocumento?: ActorDocumentType;
  numeroDocumento?: string;
  mecanismoFirma?: MecanismoFirma;
};

/**
 * Situación del representante legal capturado frente al módulo de representantes de la compañía.
 *
 * <p>`sin_directorio` es la compañía que no está en el módulo, y `desconocido` el dato que todavía
 * no se puede responder (aún no hay representante escrito, o la lectura del directorio falló).
 * Ninguno de los dos exige escritura: son ausencia de respuesta, no una respuesta negativa.</p>
 */
type RlDirectorioEstado = 'desconocido' | 'registrado' | 'no_registrado' | 'sin_directorio';

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

/** Empata cédulas del directorio aunque vengan con ceros a la izquierda o puntuación. */
function personDocKey(tipoDocumento: string, numeroDocumento: string): string | null {
  const digits = digitsOnly(numeroDocumento || '');
  if (!digits) return null;
  const tipo = (tipoDocumento || 'CC').trim().toUpperCase() || 'CC';
  return `${tipo}:${digits.replace(/^0+/, '') || '0'}`;
}

function samePersonDocument(
  tipoA: string,
  numeroA: string,
  tipoB: string,
  numeroB: string,
): boolean {
  const a = personDocKey(tipoA, numeroA);
  const b = personDocKey(tipoB, numeroB);
  return a != null && a === b;
}

function findDirectoryRep(
  directory: LegalRepresentativeLookupResult,
  tipoDocumento: string,
  numeroDocumento: string,
): { rep: LegalRepresentativeOption; index: number } | null {
  if (!personDocKey(tipoDocumento, numeroDocumento)) return null;
  const reps = repsOf(directory);
  const index = reps.findIndex((r) =>
    samePersonDocument(r.tipoDoc || 'CC', r.documento, tipoDocumento, numeroDocumento),
  );
  if (index < 0) return null;
  return { rep: reps[index], index };
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
    // Default `'vendedor'`: comportamiento previo para quien use `ActorsForm` sin pasar la prop
    // (fuera del wizard). Dentro del wizard, `TramiteWizard.tsx` SIEMPRE la pasa explícita —
    // incluida `null`— así que este default nunca decide por el paso `actores`.
    rolDelPropietario = 'vendedor',
    autoConsultRunt = false,
    rnmcEnabled = false,
    onConsultationGateChange,
    onEscrituraRepresentanteGateChange,
    onCamposRequeridosGateChange,
    rotuloPorRol,
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
  const runtRef = useRef(runt);
  useEffect(() => {
    runtRef.current = runt;
  }, [runt]);
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
  // Novedad 28 (AC6) — línea base por índice de actor jurídico: se registra UNA sola vez, la
  // primera vez que el documento del RL cambia sobre una precarga viva (nunca para captura manual,
  // AC4). Mientras exista, el par (tipo, número) ACTUAL se compara contra ella para derivar si el
  // RL "diverge" — y por tanto exige consulta RUNT — o si el operador revirtió al valor original.
  const [rlBaselineDoc, setRlBaselineDoc] = useState<Record<number, RlBaselineDoc>>({});

  // Novedad 28 (AC6) — divergencia derivada: hay línea base Y el documento actual difiere de ella
  // (tipo comparado tal cual por ser enum; número con `trim()`, sin mayúsculas ni separadores).
  const isRlDocDivergent = (index: number): boolean => {
    const baseline = rlBaselineDoc[index];
    if (!baseline) return false;
    const rl = actors[index]?.representanteLegal;
    return (
      (rl?.tipoDocumento ?? undefined) !== baseline.tipoDocumento ||
      (rl?.numeroDocumento ?? '').trim() !== (baseline.numeroDocumento ?? '').trim()
    );
  };

  const rlMatchesDirectoryRep = (index: number): boolean => {
    const directory = directoryFromLookup(runt[index]);
    if (!directory) return false;
    const rl = actors[index]?.representanteLegal;
    return (
      findDirectoryRep(directory, rl?.tipoDocumento ?? 'CC', rl?.numeroDocumento ?? '') !== null
    );
  };

  /** RL del directorio actualmente aplicado (el elegido), no cualquier coincidencia de cédula. */
  const rlMatchesAppliedDirectoryRep = (index: number): boolean => {
    if (directoryAbandoned[index]) return false;
    const directory = directoryFromLookup(runt[index]);
    if (!directory) return false;
    const reps = repsOf(directory);
    if (reps.length === 0) return false;
    const applied = reps[selectedRepIdx[index] ?? 0] ?? reps[0];
    const rl = actors[index]?.representanteLegal;
    return samePersonDocument(
      applied.tipoDoc || 'CC',
      applied.documento,
      rl?.tipoDocumento ?? 'CC',
      rl?.numeroDocumento ?? '',
    );
  };

  /** Cédula de un RL activo distinto al aplicado: hay que confirmar la precarga (sin RUNT). */
  const needsRlDirectoryApply = (index: number): boolean => {
    const directory = directoryFromLookup(runt[index]);
    if (!directory || repsOf(directory).length === 0) return false;
    if (rlMatchesAppliedDirectoryRep(index)) return false;
    return rlMatchesDirectoryRep(index);
  };

  /** Si el NIT trajo RL del directorio y la cédula actual no es de ninguno, hay que consultar RUNT. */
  const needsRlRunt = (index: number): boolean => {
    const directory = directoryFromLookup(runt[index]);
    if (!directory || repsOf(directory).length === 0) return false;
    if (rlMatchesAppliedDirectoryRep(index)) return false;
    return !rlMatchesDirectoryRep(index);
  };
  // HU #10937 — representante ELEGIDO (índice en la lista precargada) por índice de actor jurídico,
  // cuando la compañía tiene varios. Default 0 (el primario). Gobierna qué representante se precarga y
  // firma, y las banderas mostradas.
  const [selectedRepIdx, setSelectedRepIdx] = useState<Record<number, number>>({});
  /** Tras consultar un RL que NO está en el directorio, la precarga de firma deja de usarse. */
  const [directoryAbandoned, setDirectoryAbandoned] = useState<Record<number, boolean>>({});

  /**
   * ¿El representante legal capturado está registrado en el módulo de representantes de la compañía?
   *
   * <p>De esta pregunta cuelga si hay que exigir la escritura: un representante del directorio ya
   * tiene la suya allí y el sistema la apalanca sola; uno que no está no tiene ninguna, y sin ella
   * el trámite se radicaría sin el documento que faculta a quien firma.</p>
   *
   * <p><b>Por qué no se deriva de `needsRlRunt`.</b> Ese predicado —como todo el bloque de precarga—
   * cuelga de `runt[index]`, que es el resultado de una consulta viva rehidratada desde
   * `sessionStorage`. Sirve para pintar avisos, pero no para un gate: basta abrir el trámite en otra
   * pestaña para que el estado nazca vacío y el gate se abriera solo. Aquí la pregunta se responde
   * contra el DIRECTORIO, releyéndolo si hace falta, así que sobrevive a recargar y a cambiar de
   * pestaña.</p>
   *
   * <p><b>`sin_directorio` no exige nada, a propósito.</b> Es la compañía que no está en el módulo:
   * entonces nunca hubo precarga que cambiar, la captura del representante fue manual desde el
   * principio y este gate no aplica (comportamiento de siempre). `desconocido` cubre lo mismo por la
   * vía del dato ausente o de una lectura que falló: el gate no bloquea con una respuesta que no
   * tiene — dejar encerrado al gestor porque el directorio no respondió sería peor que la fuga que
   * este gate evita.</p>
   */
  const [rlDirectorio, setRlDirectorio] = useState<Record<number, RlDirectorioEstado>>({});
  /** Directorios ya leídos en este montaje, por NIT: la respuesta no cambia entre actores. */
  const directorioPorNitRef = useRef<Map<string, LegalRepresentativeLookupResult | null>>(new Map());

  /**
   * Firma estable de lo ÚNICO que cambia la respuesta: por actor jurídico, su NIT y el documento del
   * representante. Sin ella el efecto se dispararía en cada render —`actors` es un arreglo nuevo
   * siempre— y con él la lectura del directorio.
   */
  const rlDirectorioFirma = actors
    .map((a, i) =>
      isJuridical(a)
        ? [
            i,
            normalizeNitKey(a.numeroDocumento),
            a.representanteLegal?.tipoDocumento ?? '',
            (a.representanteLegal?.numeroDocumento ?? '').trim(),
          ].join(':')
        : `${i}:-`,
    )
    .join('|');

  /**
   * Primera evaluación hecha. Gobierna la espera de abajo: la primera va sin ella (el borrador ya
   * trae el representante escrito y el gate debe nacer cerrado si corresponde), y las siguientes
   * con ella (el gestor está tecleando).
   */
  const rlDirectorioEvaluadoRef = useRef(false);

  useEffect(() => {
    const juridicos = actors
      .map((a, i) => ({ a, i }))
      .filter(({ a }) => isJuridical(a));
    if (juridicos.length === 0) {
      setRlDirectorio((prev) => (Object.keys(prev).length === 0 ? prev : {}));
      return;
    }

    let active = true;

    const evaluar = async () => {
      const next: Record<number, RlDirectorioEstado> = {};
      for (const { a, i } of juridicos) {
        const nit = a.numeroDocumento.trim();
        const rlTipo = a.representanteLegal?.tipoDocumento ?? 'CC';
        const rlNumero = (a.representanteLegal?.numeroDocumento ?? '').trim();
        // Sin representante capturado todavía no hay a quién acreditar (ni qué exigir).
        if (!nit || !rlNumero) {
          next[i] = 'desconocido';
          continue;
        }

        // La consulta viva, si está, evita la lectura; si no, se lee el directorio.
        let directory = directoryFromLookup(runtRef.current[i]);
        if (!directory) {
          const key = normalizeNitKey(nit);
          if (directorioPorNitRef.current.has(key)) {
            directory = directorioPorNitRef.current.get(key) ?? null;
          } else {
            try {
              directory = await tramitesClient.lookupLegalRepresentativeByNit(nit);
              directorioPorNitRef.current.set(key, directory);
            } catch {
              // Fallo de lectura: no se cachea (para reintentar) y no se bloquea.
              next[i] = 'desconocido';
              continue;
            }
          }
        }
        if (!active) return;

        next[i] =
          !directory || repsOf(directory).length === 0
            ? 'sin_directorio'
            : findDirectoryRep(directory, rlTipo, rlNumero)
              ? 'registrado'
              : 'no_registrado';
      }
      if (!active) return;
      rlDirectorioEvaluadoRef.current = true;
      setRlDirectorio(next);
    };

    // Cédula A MEDIO ESCRIBIR: cada tecla deja un documento que no está en el directorio, así que sin
    // esta espera el bloque de la escritura aparece y desaparece mientras el gestor teclea —y con él
    // el "Continuar" del pie, parpadeando entre habilitado y no—. Solo se evalúa lo que el gestor
    // terminó de escribir. La PRIMERA evaluación no espera: al abrir un borrador el representante ya
    // está escrito, y retrasarla dejaría el gate abierto durante ese tiempo.
    if (!rlDirectorioEvaluadoRef.current) {
      void evaluar();
      return () => {
        active = false;
      };
    }

    const espera = setTimeout(() => void evaluar(), 400);
    return () => {
      active = false;
      clearTimeout(espera);
    };
    // `actors` entra por `rlDirectorioFirma`, que es su proyección estable (ver arriba).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rlDirectorioFirma]);

  /** El representante de esta parte hay que acreditarlo con escritura (no está en el directorio). */
  const exigeEscrituraRl = (index: number): boolean => rlDirectorio[index] === 'no_registrado';

  /** Escritura del representante ya adjunta, por índice de actor. */
  const [escrituraRlAdjunta, setEscrituraRlAdjunta] = useState<Record<number, boolean>>({});
  const marcarEscrituraRl = useCallback((index: number, satisfied: boolean) => {
    // Se compara antes de escribir: el hijo reporta en cada render y sin esto el ciclo no cerraría.
    setEscrituraRlAdjunta((prev) =>
      prev[index] === satisfied ? prev : { ...prev, [index]: satisfied },
    );
  }, []);

  /** Gate del paso: ninguna parte que deba acreditar a su representante se quedó sin escritura. */
  const escrituraRlGateOk = actors.every(
    (_, i) => !exigeEscrituraRl(i) || escrituraRlAdjunta[i] === true,
  );
  useEffect(() => {
    onEscrituraRepresentanteGateChange?.(escrituraRlGateOk);
  }, [escrituraRlGateOk, onEscrituraRepresentanteGateChange]);
  const [rlSwitchConfirm, setRlSwitchConfirm] = useState<{ variant: 'runt' | 'preload' } | null>(
    null,
  );
  const rlSwitchResolverRef = useRef<((ok: boolean) => void) | null>(null);
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
  // Solo aplica cuando `seedDocumentoFromOwner` (paso del propietario inscrito).
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

  // Aplica el documento del propietario (paso 1) SOLO al rol que ES ese propietario, SOLO a su
  // actor ordinal=1 (el principal/solidario — Múltiple Propietario, ADR-0053) y solo si aún no
  // tiene documento. No pisa un documento ya escrito/persistido, y en el formulario unificado
  // vendedor+comprador —donde ambos roles pasan por este helper— sigue sembrando a uno solo.
  // `isFirst` evita sembrarle el documento del propietario a un copropietario AGREGADO (ordinal
  // 2..4) que todavía no ha escrito el suyo: ese documento se digita y consulta uno a uno.
  const withOwnerSeed = (a: ProcedureActor, isFirst: boolean): ProcedureActor =>
    ownerSeed && isFirst && a.rol === rolDelPropietario && !a.numeroDocumento.trim()
      ? withDerivedPersonType({
          ...a,
          numeroDocumento: ownerSeed.numero,
          tipoDocumento: ownerSeed.tipo,
        })
      : a;

  // Múltiple Propietario (ADR-0053) — el bloque de porcentaje, una vez visible, NO se oculta
  // aunque el lado vuelva a un solo actor (encargo cerrado). Marcado por `rol`, no por índice: no
  // participa del reindexado de los mapas posicionales (§ helpers de abajo).
  const [ownershipRevealed, setOwnershipRevealed] = useState<Partial<Record<ActorRol, boolean>>>(
    {},
  );
  // Pestaña activa por lado, como ORDINAL (1-based) dentro del grupo — no como índice absoluto del
  // array `actors`: el índice absoluto se resuelve en cada render vía `indicesForRol`, así que este
  // estado sobrevive intacto a que otro lado agregue/quite actores antes de él en el array.
  const [activeTabByRol, setActiveTabByRol] = useState<Partial<Record<ActorRol, number>>>({});
  // Lados cuyo ordinal=1 el gestor ya editó a mano: deja de absorber el residuo (§4.5 del diseño).
  const [solidarioManuallyEdited, setSolidarioManuallyEdited] = useState<Set<ActorRol>>(
    () => new Set(),
  );

  // Rehidrata desde el backend cuando llegan actores cargados, respetando los roles de la
  // modalidad (rellena los faltantes con vacíos). Múltiple Propietario (ADR-0053) — un mismo rol
  // puede traer VARIOS actores (ordinal 1..4); se agrupan y ordenan por `ordinal` antes de aplanar
  // de vuelta a un solo array, preservando el invariante "actores del mismo rol contiguos".
  const loadedKey = state.actors
    ? state.actors.map((a) => `${a.rol}:${a.ordinal ?? 1}`).join(',')
    : null;
  const [hydratedKey, setHydratedKey] = useState<string | null>(null);
  if (state.actors && loadedKey !== hydratedKey) {
    setHydratedKey(loadedKey);
    const nextActors: ProcedureActor[] = [];
    const revealedByRol: Partial<Record<ActorRol, boolean>> = {};
    for (const rol of roles) {
      const foundForRol = (state.actors ?? [])
        .filter((a) => a.rol === rol)
        .sort((a, b) => (a.ordinal ?? 1) - (b.ordinal ?? 1));
      if (foundForRol.length === 0) {
        nextActors.push(emptyActor(rol));
        continue;
      }
      if (foundForRol.length > 1) revealedByRol[rol] = true;
      foundForRol.forEach((found, i) => {
        // HU #11014 — al rehidratar desde el backend también se deriva el tipo de persona: un
        // actor persistido con NIT y personType 'natural' (creado antes de esta corrección) se
        // corrige solo.
        nextActors.push(
          withOwnerSeed(withDerivedPersonType({ ...emptyActor(rol), ...found }), i === 0),
        );
      });
    }
    setActors(nextActors);
    if (Object.keys(revealedByRol).length > 0) {
      setOwnershipRevealed((prev) => ({ ...prev, ...revealedByRol }));
    }
    // HU #11595 (AC4) — un trámite en curso que YA tenía un actor persistido (documento propio,
    // no un paso vacío recién abierto) pero quedó sin ciudad/dirección/teléfono debe mostrar esos
    // campos marcados como faltantes al abrir el paso, sin esperar a que el gestor pulse
    // "Continuar" (que es lo único que antes activaba `showErrors`).
    const hasIncompleteExistingActor = nextActors.some(
      (a) => a.numeroDocumento.trim() && !validateActors([a], modalidad).valid,
    );
    if (hasIncompleteExistingActor) setShowErrors(true);
    // Aquí NO se limpia la marca de pendiente, aunque lo rehidratado sea lo persistido: los actores
    // llegan de una carga asíncrona de la shell y esta rama corre cuando aterrizan, que puede ser
    // DESPUÉS de que el gestor empezara a escribir. La marca solo puede estar puesta si él editó,
    // así que limpiarla aquí únicamente podría borrar captura viva (la carrera que reintroducía la
    // pérdida del Bug #11614). Lo persistido limpia la marca donde corresponde: al guardar.
  }

  // El seed puede llegar después de la rehidratación (fetch async). Cuando
  // aterriza, completa el documento del actor ordinal=1 si seguía vacío.
  useEffect(() => {
    if (!ownerSeed) return;
    setActors((prev) => prev.map((a, i) => withOwnerSeed(a, isFirstOfRol(prev, i))));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ownerSeed]);

  // Múltiple Propietario (ADR-0053, §4.5) — auto-absorción del residuo: el ordinal=1 de CADA lado
  // con 2+ actores recalcula su porcentaje como 100 − suma de los demás, mientras ese lado no haya
  // sido editado a mano. `applySolidarioAbsorption` devuelve la MISMA referencia si no hay nada que
  // cambiar, así que este efecto no reentra en bucle al no producir un nuevo `actors`.
  useEffect(() => {
    setActors((prev) => applySolidarioAbsorption(prev, solidarioManuallyEdited));
  }, [actors, solidarioManuallyEdited]);

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

  /** Hay arrendatario en esta pantalla: entonces la contraparte es el arrendador (propietario). */
  const hayLocatario = roles.includes('locatario');
  /**
   * Cómo se llama esta parte en pantalla. Tres fuentes, en orden:
   *
   * <p>1) El nombre que el CATÁLOGO le dio al paso que captura ese rol (`rotuloPorRol`): es la única
   * fuente que sabe que el `comprador` de un `TRASPASO_UNILATERAL` es en realidad el locatario del
   * leasing. 2) La regla del leasing de matrícula: junto a un locatario, la contraparte es el
   * PROPIETARIO (arrendador), no un comprador. 3) El rótulo del rol.</p>
   *
   * <p>Múltiple Propietario (ADR-0053) — `ordinal` es OPCIONAL y a propósito: los llamadores que
   * pintan el caso de un solo actor por lado (el mayoritario, sin pestañas) siguen sin pasarlo, así
   * que el rótulo no cambia ni un carácter frente al comportamiento previo. Los llamadores nuevos
   * (pestañas, título de la tarjeta activa cuando el lado ya tiene 2+) lo pasan explícito y el
   * rótulo pasa a «Comprador 1» / «Comprador 2» — NUNCA un literal fijo «Propietario N».</p>
   */
  const rotuloDelActor = (rol: ActorRol, ordinal?: number): string => {
    const delCatalogo = rotuloPorRol?.[rol]?.trim();
    const base = delCatalogo || (hayLocatario && rol === 'comprador' ? 'Propietario' : ROL_LABEL[rol]);
    return ordinal !== undefined ? `${base} ${ordinal}` : base;
  };

  const validation = validateActors(actors, modalidad);

  /**
   * Publica hacia la shell si el paso tiene ya todos los campos obligatorios. Es la MISMA
   * validación que aplica `save()` (`validateActors`), no una segunda regla en paralelo: si
   * divergieran, el botón podría habilitarse para un guardado que va a fallar.
   */
  useEffect(() => {
    onCamposRequeridosGateChange?.(validation.valid);
  }, [validation.valid, onCamposRequeridosGateChange]);

  // NO se revelan los errores en vivo. Se intentó (para justificar el botón deshabilitado) y el
  // resultado fue peor que el problema: `showErrors` es una bandera del FORMULARIO, no de cada
  // parte, así que se encendía mientras se tecleaba el documento del primer actor y, al añadir un
  // copropietario, su tarjeta recién creada nacía entera en rojo sin que nadie hubiera tocado nada.
  // Los campos obligatorios ya se anuncian con su asterisco; el motivo del bloqueo lo dice el pie.

  /** Pestañas del lado `rol` para `OwnershipShareControl` — el ordinal=1 nunca es removible. */
  const ownershipItemsForRol = (rol: ActorRol): OwnershipShareItem[] => {
    const idxs = indicesForRol(actors, rol);
    // Con un solo actor `porcentaje` es `null` a propósito (regla de negocio: el backend lo exige
    // así, sin cambios). Mostrar "0%" en la píldora sería visualmente incorrecto — el único
    // propietario es, conceptualmente, dueño del 100% — así que el 100% es SOLO el valor de
    // RESPALDO para pintar la pestaña; no se persiste ni participa en la validación (eso sigue
    // gobernado por `actor.porcentaje` real vía `normalizeActors`/`validateOwnershipShares`).
    const soloUno = idxs.length === 1;
    return idxs.map((index, i) => ({
      index,
      ordinal: i + 1,
      label: rotuloDelActor(rol, i + 1),
      percentage: actors[index]?.porcentaje ?? (soloUno ? 100 : 0),
      removable: i > 0,
    }));
  };

  const setRuntFor = (index: number, value: LookupState) =>
    setRunt((prev) => {
      const next = { ...prev, [index]: value };
      runtRef.current = next;
      return next;
    });

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
        // El documento manda sobre la naturaleza de la persona: el selector de tipo de documento es
        // el único control, y todo lo demás (RUES/RUNT autopoblando, siembra del propietario del
        // paso 1) pasa por aquí, así que la coherencia NIT ⇔ jurídica no depende de quién escriba.
        if (patch.tipoDocumento !== undefined)
          next.personType = personTypeForDoc(next.tipoDocumento);
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

  // ── Múltiple Propietario (ADR-0053) — agregar/quitar copropietarios ──────────────────────────
  //
  // `ActorsForm.tsx` guarda estado efímero por actor (consulta RUNT/RUES, representante legal
  // elegido, autocompletar de ciudad, fechas RNMC, contacto…) en 11 `Record<number, X>` indexados
  // por POSICIÓN en `actors`, no por un id estable de actor. `addOwner`/`removeOwner` son el ÚNICO
  // lugar donde `actors` cambia de longitud, y por eso son el único lugar donde estos 11 mapas se
  // reindexan — en el MISMO gesto, con `shiftIndexMapOnInsert`/`shiftIndexMapOnRemove`
  // (`lib/tramites/ownership-share.ts`, testeadas aparte). Sin este reindexado, la consulta de un
  // actor eliminado quedaría reasociada al actor equivocado tras el desplazamiento de índices —el
  // "estado fantasma" señalado en el encargo.
  const shiftAllPositionalMaps = (shift: <T>(m: Record<number, T>) => Record<number, T>) => {
    setRunt((prev) => shift(prev));
    setRlRunt((prev) => shift(prev));
    setRlBaselineDoc((prev) => shift(prev));
    setSelectedRepIdx((prev) => shift(prev));
    setDirectoryAbandoned((prev) => shift(prev));
    setRlDirectorio((prev) => shift(prev));
    setEscrituraRlAdjunta((prev) => shift(prev));
    setCiudadOpen((prev) => shift(prev));
    setIssueDates((prev) => shift(prev));
    setContactLookup((prev) => shift(prev));
    setTouchedContact((prev) => shift(prev));
  };

  /** Agrega un copropietario al final del grupo de `rol` (máximo 4 — ADR-0053). */
  const addOwner = (rol: ActorRol) => {
    if (readOnly) return;
    const idxs = indicesForRol(actors, rol);
    if (idxs.length >= MAX_OWNERS_PER_SIDE) return;
    const insertAt = idxs.length ? idxs[idxs.length - 1] + 1 : actors.length;
    const countAfter = idxs.length + 1;
    const newActor: ProcedureActor = {
      ...emptyActor(rol),
      porcentaje: defaultPercentageForNewActor(countAfter),
    };
    markDirty();
    setActors((prev) => {
      const next = prev.slice();
      next.splice(insertAt, 0, newActor);
      return next;
    });
    shiftAllPositionalMaps((m) => shiftIndexMapOnInsert(m, insertAt));
    setOwnershipRevealed((prev) => ({ ...prev, [rol]: true }));
    setActiveTabByRol((prev) => ({ ...prev, [rol]: countAfter }));
  };

  /** Quita un copropietario (nunca el ordinal=1) y redistribuye su porcentaje entre los que quedan. */
  const removeOwner = (index: number) => {
    if (readOnly) return;
    const rol = actors[index]?.rol;
    if (!rol || isFirstOfRol(actors, index)) return;
    markDirty();
    setActors((prev) => redistributeAfterRemoval(prev, index));
    shiftAllPositionalMaps((m) => shiftIndexMapOnRemove(m, index));
    const remainingCount = indicesForRol(actors, rol).length - 1;
    setActiveTabByRol((prev) => ({
      ...prev,
      [rol]: Math.max(1, Math.min(prev[rol] ?? 1, remainingCount)),
    }));
    // De vuelta a un solo propietario: la próxima vez que se agregue un segundo, el solidario
    // vuelve a absorber desde cero (decisión de UI — el diseño no fija este caso puntual).
    if (remainingCount <= 1) {
      setSolidarioManuallyEdited((prev) => {
        if (!prev.has(rol)) return prev;
        const next = new Set(prev);
        next.delete(rol);
        return next;
      });
    }
  };

  /** Edita el porcentaje del actor de `index`; si es el ordinal=1, deja de absorber el residuo. */
  const updateOwnershipPercentage = (index: number, rawValue: number) => {
    if (readOnly) return;
    const rol = actors[index]?.rol;
    if (!rol) return;
    const value = round2(Number.isFinite(rawValue) ? Math.max(0, rawValue) : 0);
    markDirty();
    if (isFirstOfRol(actors, index)) {
      setSolidarioManuallyEdited((prev) => (prev.has(rol) ? prev : new Set(prev).add(rol)));
    }
    setActors((prev) => prev.map((a, i) => (i === index ? { ...a, porcentaje: value } : a)));
  };

  /** Activa la pestaña del actor real `realIndex` dentro del lado `rol` (guarda su ORDINAL). */
  const selectOwnershipTab = (rol: ActorRol, realIndex: number) => {
    const ordinal = ownershipItemsForRol(rol).find((it) => it.index === realIndex)?.ordinal ?? 1;
    setActiveTabByRol((prev) => ({ ...prev, [rol]: ordinal }));
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
  /**
   * Escribe el RL del directorio y el contacto de su ficha en un solo `setActors`.
   * Encadenar `updateRepLegal` + `updateActor` dejaba el formulario con el RL anterior
   * (el banner/selector sí cambiaban porque `selectedRepIdx` se actualizaba aparte).
   */
  const commitDirectoryRep = (
    actorIndex: number,
    match: { rep: LegalRepresentativeOption; index: number },
    company: LegalRepresentativeLookupCompany | undefined,
    opts?: {
      preserveConsultation?: boolean;
      applyCompanyContact?: boolean;
      mecanismoFirma?: MecanismoFirma;
    },
  ) => {
    if (!opts?.preserveConsultation) markDirty();
    setSelectedRepIdx((prev) => ({ ...prev, [actorIndex]: match.index }));
    setDirectoryAbandoned((prev) => ({ ...prev, [actorIndex]: false }));
    const tipo = (match.rep.tipoDoc as ActorDocumentType) || 'CC';
    setActors((prev) => {
      const nextActors = prev.map((a, i) => {
        if (i !== actorIndex) return a;
        const next: ProcedureActor = {
          ...a,
          representanteLegal: {
            tipoDocumento: tipo,
            numeroDocumento: sanitizeDocNumber(match.rep.documento ?? '', tipo),
            nombreCompleto: sanitizeName(repFullName(match.rep)),
            email: (match.rep.email ?? '').trim(),
            telefono: digitsOnly(match.rep.telefono ?? ''),
            mecanismoFirma: opts?.mecanismoFirma,
          },
        };
        if (opts?.applyCompanyContact && company) {
          next.email = (match.rep.companyEmail ?? company.email ?? '').trim();
          next.direccion = (match.rep.companyAddress ?? company.address ?? '').trim();
          next.ciudad = (match.rep.companyCity ?? company.city ?? '').trim();
          next.telefono = digitsOnly(match.rep.companyPhone ?? company.phone ?? '');
        }
        return next;
      });
      actorsRef.current = nextActors;
      return nextActors;
    });
  };

  const handleSelectRep = (
    index: number,
    repIdx: number,
    directoryOverride?: LegalRepresentativeLookupResult | null,
  ) => {
    const directory =
      directoryOverride ??
      directoryFromLookup(runtRef.current[index]) ??
      directoryFromLookup(runt[index]);
    if (!directory) return;
    const rep = repsOf(directory)[repIdx];
    if (!rep) return;
    commitDirectoryRep(index, { rep, index: repIdx }, directory.company);
  };

  // Consulta de identidad por documento. Bifurca por tipo de persona: jurídica → RUES (por NIT),
  // natural → RUNT (conductor). Si encuentra, autopopula el actor. Sin resultado exitoso no se
  // permite guardar ni avanzar (gate de Continuar en el wizard).
  /**
   * Consultas en vuelo que nacieron de un clic. Es un contador y no un booleano porque en traspaso
   * hay dos actores y el gestor puede lanzar la segunda consulta sin esperar a la primera.
   */
  const [consultasManuales, setConsultasManuales] = useState(0);

  /**
   * Envuelve una consulta para que levante el velo de espera; solo la usan los botones.
   *
   * Genérico, no `Promise<unknown>`: con `unknown` el resultado se estrechaba a `{}` en el
   * `if` de quien lo llamaba, y guardar eso en el estado de la ficha no compilaba (`{}` no es
   * un `LookupState`). El velo no mira lo que devuelve la consulta, así que el tipo pasa de largo.
   */
  const conVelo = <T,>(consulta: Promise<T>): Promise<T> => {
    setConsultasManuales((n) => n + 1);
    return consulta.finally(() => setConsultasManuales((n) => Math.max(0, n - 1)));
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
        const [ruesResult, directory] = await Promise.all([
          tramitesClient.ruesPersonLookup(instanceId, { documentNumber }),
          tramitesClient.lookupLegalRepresentativeByNit(documentNumber).catch(() => null),
        ]);
        if (ruesResult.found) {
          const nit = ruesResult.documentNumber || actor.numeroDocumento;
          const razonSocial =
            shortRuesRazonSocial(ruesResult.razonSocial) || actor.nombreCompleto;
          const reps = directory ? repsOf(directory) : [];
          updateActor(
            index,
            {
              nombreCompleto: razonSocial,
              tipoDocumento: 'NIT',
              numeroDocumento: nit,
            },
            { preserveConsultation: true },
          );
          if (directory && reps[0]) {
            commitDirectoryRep(
              index,
              { rep: reps[0], index: 0 },
              directory.company,
              { preserveConsultation: true, applyCompanyContact: true },
            );
          }
          const foundRues = {
            status: 'found' as const,
            kind: 'rues' as const,
            result: ruesResult,
            directory,
          };
          setRuntFor(index, foundRues);
          rememberActorConsultation(
            instanceId,
            {
              ...actor,
              tipoDocumento: 'NIT',
              numeroDocumento: nit,
              nombreCompleto: razonSocial,
            },
            foundRues,
          );
          if (shortRuesRazonSocial(ruesResult.razonSocial)) lockRuesRazonSocial(nit);
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

  // Consulta RUNT del representante legal (persona natural). Si el NIT ya trajo RL del directorio,
  // pedir confirmación: la firma del anterior deja de apalancarse. Si la cédula es de un RL activo
  // se precarga; si no, se consulta RUNT y se descarta la información básica previa.
  const executeRlLookup = async (index: number) => {
    const rl = actorsRef.current[index]?.representanteLegal;
    const documentNumber = rl?.numeroDocumento?.trim();
    const documentType = rl?.tipoDocumento ?? 'CC';
    if (!instanceId || !documentNumber) return;

    const directory = directoryFromLookup(runtRef.current[index]);
    const match = directory ? findDirectoryRep(directory, documentType, documentNumber) : null;

    if (match && directory) {
      commitDirectoryRep(index, match, directory.company);
      setRlRuntFor(index, { status: 'idle' });
      return;
    }

    if (directory && repsOf(directory).length > 0) {
      setDirectoryAbandoned((prev) => ({ ...prev, [index]: true }));
    }

    updateRepLegal(index, {
      tipoDocumento: documentType,
      numeroDocumento: documentNumber,
      nombreCompleto: '',
      email: '',
      telefono: '',
      mecanismoFirma: undefined,
    });

    setRlRuntFor(index, { status: 'loading' });
    try {
      const result = await tramitesClient.runtPersonLookup(instanceId, {
        documentType,
        documentNumber,
      });
      if (result.found) {
        updateRepLegal(index, {
          nombreCompleto: result.fullName ?? '',
          tipoDocumento: (result.documentType as ActorDocumentType) || documentType,
          numeroDocumento: result.documentNumber || documentNumber,
          email: '',
          telefono: '',
          mecanismoFirma: undefined,
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

  const startRlLookup = async (index: number) => {
    const actor = actorsRef.current[index];
    const rl = actor?.representanteLegal;
    const documentNumber = rl?.numeroDocumento?.trim();
    const documentType = rl?.tipoDocumento ?? 'CC';
    const companyNit = actor?.numeroDocumento?.trim();
    if (!instanceId || !documentNumber || rlRunt[index]?.status === 'loading') {
      return;
    }

    // Consulta el directorio de ESTA compañía (NIT del actor), no RL de otras fichas.
    let directory = directoryFromLookup(runtRef.current[index]);
    if (companyNit) {
      const fresh = await conVelo(
        tramitesClient.lookupLegalRepresentativeByNit(companyNit).catch(() => null),
      );
      if (fresh) {
        directory = fresh;
        setRunt((prev) => {
          const cur = prev[index];
          if (cur?.status !== 'found' || cur.kind !== 'rues') return prev;
          const next = { ...prev, [index]: { ...cur, directory: fresh } };
          runtRef.current = next;
          return next;
        });
      }
    }
    if (!directory) {
      directory = directoryFromLookup(runtRef.current[index]);
    }

    const match = directory ? findDirectoryRep(directory, documentType, documentNumber) : null;
    if (directory && repsOf(directory).length > 0) {
      const confirmed = await requestRlSwitchConfirm(match ? 'preload' : 'runt');
      if (!confirmed) return;
      if (match) {
        commitDirectoryRep(index, match, directory.company);
        setRlRuntFor(index, { status: 'idle' });
        return;
      }
    }
    conVelo(executeRlLookup(index));
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

  // ── Paso del propietario: dispara la consulta en cuanto el documento está disponible (sembrado desde
  // el paso 1 o rehidratado del backend), sin clic manual. La razón social jurídica sale de RUES;
  // el directorio de RL aporta datos básicos de empresa y representante.
  // Split (una sola parte) o MULTI unificado (índice del rol propietario).
  // Si ya hay snapshot restaurado (`found`), no vuelve a consultar.
  const propietarioIndex = actors.findIndex((a) => a.rol === rolDelPropietario);
  const propietarioDoc = propietarioIndex >= 0 ? actors[propietarioIndex]?.numeroDocumento : undefined;
  const propietarioTipo = propietarioIndex >= 0 ? actors[propietarioIndex]?.tipoDocumento : undefined;
  const propietarioRuntStatus = propietarioIndex >= 0 ? runt[propietarioIndex]?.status : undefined;

  useEffect(() => {
    if (!autoConsultRunt || !instanceId || readOnly) return;
    if (propietarioIndex < 0) return;
    // Multiple Propietario (ADR-0053) - el criterio real es UN SOLO LADO (rol), no un solo
    // actor: en SPLIT ahora puede haber 2..4 actores del mismo rol. Si el rol que este layout
    // captura no es el del propietario, la auto-consulta no aplica (mismo criterio de siempre,
    // generalizado por rol en vez de por conteo de actores).
    if (isSplit && roles[0] !== rolDelPropietario) return;

    const documentNumber = (propietarioDoc ?? '').trim();
    if (!documentNumber) return;
    if ((propietarioRuntStatus ?? 'idle') !== 'idle') return;
    const propietario = actors[propietarioIndex];
    const cached = propietario ? restoreActorConsultation(instanceId, propietario) : null;
    if (cached) {
      setRuntFor(propietarioIndex, cached);
      autoLookupTriggeredRef.current = `${propietarioTipo}:${documentNumber}`;
      return;
    }
    const lookupKey = `${propietarioTipo}:${documentNumber}`;
    if (autoLookupTriggeredRef.current === lookupKey) return;
    autoLookupTriggeredRef.current = lookupKey;
    void handleIdentityLookup(propietarioIndex);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- handleIdentityLookup lee actors actuales
  }, [
    autoConsultRunt,
    instanceId,
    readOnly,
    isSplit,
    roles,
    rolDelPropietario,
    propietarioIndex,
    propietarioDoc,
    propietarioTipo,
    propietarioRuntStatus,
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

  const requestRlSwitchConfirm = (variant: 'runt' | 'preload'): Promise<boolean> =>
    new Promise((resolve) => {
      rlSwitchResolverRef.current = resolve;
      setRlSwitchConfirm({ variant });
    });

  const resolveRlSwitchConfirm = (confirmed: boolean) => {
    setRlSwitchConfirm(null);
    rlSwitchResolverRef.current?.(confirmed);
    rlSwitchResolverRef.current = null;
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
    // Múltiple Propietario (ADR-0053) — gate de UX (el backend es SIEMPRE la autoridad, §4.5/§6
    // del diseño): con un solo actor por lado `validateOwnershipShares` siempre es válida (no
    // toca el flujo previo).
    if (!validateOwnershipShares(actors).valid) return false;

    // Gate duro: cada actor debe tener consulta RUNT/RUES/directorio exitosa antes de persistir.
    // Novedad 28 (AC3/AC6) — además, si el documento del RL diverge de la línea base de precarga,
    // ese representante también debe tener una consulta RUNT exitosa (no basta con reconsultar el
    // NIT). Al revertir el documento a su valor original, la divergencia desaparece y el gate se
    // levanta solo, sin exigir una nueva consulta.
    if (
      actors.some(
        (_, i) =>
          !isIdentityConsultationReady(runt[i]?.status) ||
          needsRlDirectoryApply(i) ||
          (needsRlRunt(i) && !isIdentityConsultationReady(rlRunt[i]?.status)),
      )
    ) {
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
  // Novedad 28 (AC3/AC6) — mismo refuerzo del gate de guardado: solo mientras el RL diverja de la
  // línea base exige su consulta RUNT exitosa.
  const consultationReady = actors.every(
    (_, i) =>
      isIdentityConsultationReady(runt[i]?.status) &&
      !needsRlDirectoryApply(i) &&
      (!needsRlRunt(i) || isIdentityConsultationReady(rlRunt[i]?.status)),
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

  /** PDF ajuste P0: consulta RUNT exitosa → nombre bloqueado; vendedor desde placa también bloquea PN/PJ. */
  const isRuntFound = (index: number) => runt[index]?.status === 'found';
  const isNameLockedByRunt = (index: number, actor: ProcedureActor) =>
    isRuntFound(index) && !isJuridical(actor);
  /**
   * Bloqueo de la identidad traída del registro: con el vendedor fijado desde el RUNT por la placa,
   * el documento NO se cambia a mano — cambiarlo dejaría al trámite a nombre de otro. Antes bloqueaba
   * el interruptor de tipo de persona; ahora bloquea el selector de documento, que es su reemplazo.
   */
  const isDocTypeLockedByRunt = (index: number) => autoConsultRunt && isRuntFound(index);

  /**
   * Selector de tipo de documento: el único control de la identidad del actor. Elegir NIT es lo que
   * declara la persona jurídica (y desvía la consulta al RUES); `updateActor` deriva `personType`.
   */
  const docTypeSelector = (
    index: number,
    idPrefix: string,
    locked = false,
    /* Suelto se acota el ancho para no estirar un desplegable de cuatro opciones a toda la fila;
       dentro de una rejilla de identificación se pasa '' y manda la celda. */
    wrapperClassName = 'sm:max-w-xs',
  ) => {
    const id = `${idPrefix}-tipoDoc`;
    return (
      <div className={cn('min-w-0', wrapperClassName)}>
        <label htmlFor={id} className={`${WIZARD_LABEL} mb-1.5`}>
          Tipo de documento
        </label>
        <select
          id={id}
          value={actors[index].tipoDocumento}
          disabled={readOnly || locked}
          onChange={(e) => updateActor(index, { tipoDocumento: e.target.value as ActorDocumentType })}
          className={WIZARD_SELECT}
        >
          {DOC_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
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
      const razonSocial = rep?.razonSocial?.trim() || company.razonSocial;
      const firmaVigente = rep?.firmaVigente ?? false;
      const identidadVigente = rep?.identidadVigente ?? false;
      return (
        <div className="space-y-2" role="status" aria-live="polite">
          {/* Tono informativo, no de éxito: la tarjeta muestra datos que no se consultaron a
              RUES/RUNT, no algo que sea válido. Es el mismo azul del badge «Dato reutilizado». */}
          <div className="rounded-xl p-3 text-xs border" style={cardTone('info').card}>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1.5">
              <div className="col-span-2">
                <span className="opacity-60 font-normal">Razón social: </span>
                <span className="font-semibold" style={{ color: '#162744' }}>
                  {razonSocial}
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
                  onChange={(e) =>
                    handleSelectRep(index, Number(e.target.value), runtState.result)
                  }
                  className={WIZARD_SELECT}
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
                label={firmaVigente ? 'Cuenta con firma digital vigente.' : 'Sin firma vigente'}
              />
              <StatusBadge
                tone={identidadVigente ? 'success' : 'neutral'}
                label={identidadVigente ? 'Identidad vigente' : 'Validación de identidad pendiente (Kyverum).'}
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
                  className={WIZARD_SELECT}
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
                  {shortRuesRazonSocial(r.razonSocial) || '—'}
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
          {runtState.directory ? (
            <div className="rounded-xl p-3 text-xs border" style={cardTone('info').card}>
              {directoryAbandoned[index] ? (
                <p>
                  Consultaste otro representante no registrado. La firma e identidad del RL
                  anterior no se apalancan en este trámite.
                </p>
              ) : (
                (() => {
                const directory = runtState.directory;
                const reps = repsOf(directory);
                const sel = selectedRepIdx[index] ?? 0;
                const rep = reps[sel] ?? reps[0];
                const firmaVigente = rep?.firmaVigente ?? false;
                const identidadVigente = rep?.identidadVigente ?? false;
                return (
                  <>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1.5">
                      <div className="col-span-2">
                        <span className="opacity-60 font-normal">Representante: </span>
                        <span className="font-semibold" style={{ color: '#162744' }}>
                          {rep ? repFullName(rep) || rep.documento : '—'}
                        </span>
                      </div>
                    </div>
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
                          onChange={(e) =>
                            handleSelectRep(index, Number(e.target.value), directory)
                          }
                          className={WIZARD_SELECT}
                        >
                          {reps.map((option, i) => (
                            <option key={`${option.tipoDoc}-${option.documento}`} value={i}>
                              {`${repFullName(option) || option.documento} · ${option.tipoDoc} ${option.documento}`}
                            </option>
                          ))}
                        </select>
                      </div>
                    )}
                    <div className="mt-2 flex flex-wrap gap-2">
                      <StatusBadge
                        tone={firmaVigente ? 'success' : 'neutral'}
                        label={firmaVigente ? 'Cuenta con firma digital vigente.' : 'Sin firma vigente'}
                      />
                      <StatusBadge
                        tone={identidadVigente ? 'success' : 'neutral'}
                        label={identidadVigente ? 'Identidad vigente' : 'Validación de identidad pendiente (Kyverum).'}
                      />
                    </div>
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
                          className={WIZARD_SELECT}
                        >
                          <option value="baul">Firma del baúl</option>
                          <option value="identidad">Sello de validación de identidad</option>
                        </select>
                      </div>
                    )}
                  </>
                );
              })()
              )}
            </div>
          ) : null}
        </div>
      );
    }
    if (runtState.status === 'found' && runtState.kind === 'runt') {
      const r = runtState.result;
      const hasLicenses = r.hasActiveLicense ?? (r.licenseStatus != null);
      const nombres =
        [r.firstName, r.secondName].filter(Boolean).join(' ') || r.fullName || '—';
      const apellidos = r.lastName ?? '—';
      const documento = r.documentNumber ?? '—';
      const estadoCiudadano = r.citizenStatus ?? r.licenseStatus ?? '—';
      const estadoActivo = (r.citizenStatus ?? '').toUpperCase() === 'ACTIVA';
      const licenciasTxt = hasLicenses
        ? `Sí${r.licenseCategories ? ` (${r.licenseCategories})` : ''}`
        : 'No';
      const conductorTxt = r.licenseStatus ?? '—';
      const conductorActivo = (r.licenseStatus ?? '').toUpperCase() === 'ACTIVO';
      // Paleta PDF 20-ago + prototipo Lovable (traspaso-ui): sin hex fuera de esa escala.
      const PROTO = {
        canvas: '#F8FAFC',
        border: '#DFE5ED',
        navy: '#162744',
        green: '#8CC63F',
        white: '#FFFFFF',
        ok: { color: '#4F7A12', background: '#F3FBE8', borderColor: '#CDEB9C' },
        err: { color: '#B91C1C', background: '#FEF2F2', borderColor: '#FECACA' },
      } as const;
      const roStyle = {
        background: PROTO.white,
        borderColor: PROTO.border,
        color: PROTO.navy,
      } as const;
      const pill = (ok: boolean, label: string) => (
        <span
          className="mt-1 inline-block rounded-full px-2.5 py-0.5 text-[11px] font-semibold"
          style={
            ok
              ? { background: PROTO.green, color: PROTO.white }
              : {
                  background: PROTO.ok.background,
                  color: PROTO.ok.color,
                  border: `1px solid ${PROTO.ok.borderColor}`,
                }
          }
        >
          {label}
        </span>
      );
      return (
        <div className="space-y-3" role="status" aria-live="polite">
          <div
            className="rounded-xl border p-4"
            style={{ borderColor: PROTO.border, background: PROTO.canvas }}
          >
            <p
              className="mb-3 flex flex-wrap items-center gap-1.5 text-[13px] font-bold"
              style={{ color: PROTO.navy }}
            >
              Persona encontrada en RUNT
              {originBadge(r.mode, 'RUNT')}
            </p>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <div>
                <label htmlFor={`runt-nombres-${index}`} className={`${WIZARD_LABEL} mb-1.5`}>
                  Nombres
                </label>
                <input
                  id={`runt-nombres-${index}`}
                  type="text"
                  readOnly
                  value={nombres}
                  className={INPUT_BASE}
                  style={roStyle}
                />
              </div>
              <div>
                <label htmlFor={`runt-apellidos-${index}`} className={`${WIZARD_LABEL} mb-1.5`}>
                  Apellidos
                </label>
                <input
                  id={`runt-apellidos-${index}`}
                  type="text"
                  readOnly
                  value={apellidos}
                  className={INPUT_BASE}
                  style={roStyle}
                />
              </div>
              <div>
                <label htmlFor={`runt-documento-${index}`} className={`${WIZARD_LABEL} mb-1.5`}>
                  Documento
                </label>
                <input
                  id={`runt-documento-${index}`}
                  type="text"
                  readOnly
                  value={documento}
                  className={`${INPUT_BASE} font-mono`}
                  style={roStyle}
                />
              </div>
              <div>
                <span className={`${WIZARD_LABEL} mb-1.5 block`}>Estado</span>
                {pill(estadoActivo, estadoCiudadano)}
              </div>
              <div>
                <span className={`${WIZARD_LABEL} mb-1.5 block`}>Licencias</span>
                <p className="text-xs font-semibold" style={{ color: PROTO.navy }}>
                  {licenciasTxt}
                </p>
              </div>
              <div>
                <span className={`${WIZARD_LABEL} mb-1.5 block`}>Conductor</span>
                {pill(conductorActivo, conductorTxt)}
              </div>
            </div>
          </div>

          {r.hasPendingFines !== undefined && (
            <div
              className="rounded-xl border p-4 text-xs"
              style={
                r.hasPendingFines
                  ? {
                      borderColor: PROTO.err.borderColor,
                      background: PROTO.err.background,
                      color: PROTO.err.color,
                    }
                  : {
                      borderColor: PROTO.ok.borderColor,
                      background: PROTO.ok.background,
                      color: PROTO.ok.color,
                    }
              }
              role="status"
            >
              <span className="flex items-center gap-2">
                <span aria-hidden="true">{r.hasPendingFines ? '⚠' : '⊙'}</span>
                <span className="font-semibold">
                  {r.hasPendingFines
                    ? 'ALERTA: Comparendos / Multas Pendientes'
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
    // Novedad 28 (AC6) — `runt[index]` es la consulta CRUDA del ACTOR (RUES/directorio), no la del
    // RL: el efecto de rehidratación de sessionStorage (más arriba, `restoreActorConsultation`) la
    // mantiene en "found" en cuanto el NIT del actor no cambió. Lo que apaga `isPreloaded` ya no es
    // ese estado transitorio sino la divergencia DERIVADA contra la línea base (`rlBaselineDoc`):
    // si el operador revierte el documento del RL a su valor original, deja de divergir y la
    // precarga "revive" sin volver a consultar nada.
    const rawPreloadedLive = directoryFromLookup(runtState) !== null;
    let baseline = rlBaselineDoc[index];
    const isPreloaded = rlMatchesAppliedDirectoryRep(index);

    // Solo el cambio de documento (número/tipo) del RL invalida la consulta RUES/directorio
    // y pide volver a consultar. Nombre, correo, teléfono y demás datos básicos se pueden
    // editar sin perder la consulta vigente.
    const handleRlIdentityDocChange = (patch: Partial<RepresentanteLegal>) => {
      const identityDocChanged =
        patch.numeroDocumento !== undefined || patch.tipoDocumento !== undefined;
      if (!identityDocChanged) {
        updateRepLegal(index, patch);
        return;
      }

      if (!baseline) {
        if (!rawPreloadedLive) {
          // Captura manual (AC4): nunca hubo precarga cruda para este actor — comportamiento de
          // siempre, sin línea base ni gate adicional.
          updateRepLegal(index, patch);
          return;
        }
        // Novedad 28 (AC6) — primera vez que se toca el documento de un RL precargado: fija la
        // línea base con los valores PREVIOS al patch (incluido `mecanismoFirma`, para poder
        // restituirlo si el operador termina revirtiendo al número original).
        // El lookup RUES/directorio del NIT se conserva: hace falta para confirmar un cambio de
        // cédula, precargar otro RL activo o consultar RUNT si no está en el directorio.
        baseline = {
          tipoDocumento: rl.tipoDocumento,
          numeroDocumento: rl.numeroDocumento,
          mecanismoFirma: rl.mecanismoFirma,
        };
        setRlBaselineDoc((prev) => ({ ...prev, [index]: baseline! }));
      }

      const nextTipo = patch.tipoDocumento !== undefined ? patch.tipoDocumento : rl.tipoDocumento;
      const nextNumero =
        patch.numeroDocumento !== undefined ? patch.numeroDocumento : rl.numeroDocumento;
      const staysAtBaseline =
        nextTipo === baseline.tipoDocumento &&
        (nextNumero ?? '').trim() === (baseline.numeroDocumento ?? '').trim();

      // Novedad 28 (AC6 + defecto de `rlRunt` obsoleto) — con línea base viva, CUALQUIER cambio de
      // documento invalida la consulta del RL (no solo el primero): si no, un "found" que
      // corresponde a un número que ya no es el vigente se cuela en el gate, o queda colgado el
      // mensaje "Representante encontrado en RUNT" tras revertir.
      setRlRuntFor(index, { status: 'idle' });
      if (staysAtBaseline) {
        setDirectoryAbandoned((prev) => ({ ...prev, [index]: false }));
      }
      const directory =
        directoryFromLookup(runtRef.current[index]) ?? directoryFromLookup(runtState);
      const match = directory
        ? findDirectoryRep(directory, nextTipo ?? 'CC', nextNumero ?? '')
        : null;
      // Cédula de un RL del directorio: copiar nombre/correo/teléfono ya. Si solo se parchea el
      // documento, el selector puede mostrar al RL correcto y el formulario se queda con el anterior.
      if (match && directory) {
        commitDirectoryRep(index, match, directory.company, {
          mecanismoFirma: staysAtBaseline ? baseline.mecanismoFirma : undefined,
        });
        return;
      }
      updateRepLegal(index, {
        ...patch,
        // Al revertir exactamente al documento de la línea base, se restituye el mecanismo de
        // firma que tenía entonces; mientras diverge, se limpia (comportamiento previo).
        mecanismoFirma: staysAtBaseline ? baseline.mecanismoFirma : undefined,
      });
    };

    return (
      <div className="sm:col-span-2 space-y-3">
        <WizardCardHeader
          title="Representante legal y/o apoderado"
          level="h4"
          subtitle={
            isPreloaded
              ? undefined
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
              className={`${WIZARD_SELECT} mt-1.5`}
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
                  void startRlLookup(index);
                }
              }}
              className={`${INPUT_BASE} mt-1.5 font-mono`}
            />
          </div>
          {!readOnly && !isPreloaded && (
            <button
              type="button"
              onClick={() => void startRlLookup(index)}
              disabled={rlState.status === 'loading' || !(rl.numeroDocumento ?? '').trim() || !instanceId}
              className="h-[42px] shrink-0 rounded-xl bg-[#557EFF] px-3 text-xs font-semibold text-white disabled:opacity-50"
              style={{ backgroundColor: WIZARD_BTN_SOLID, backgroundImage: 'none' }}
            >
              {rlState.status === 'loading' ? 'Consultando…' : 'Consultar RUNT'}
            </button>
          )}
          {!readOnly && isPreloaded && (
            <button
              type="button"
              disabled
              // Novedad 28 (AC1) — nace deshabilitado: los datos del RL ya vienen resueltos desde
              // el directorio y no hace falta reconsultar. Secundario en navy y a plena opacidad
              // (no atenuar el texto al 60%: ya hubo una corrección de contraste por eso en este
              // mismo archivo — ver comentario histórico más abajo).
              className="h-[42px] shrink-0 rounded-xl border px-3 text-xs font-semibold disabled:opacity-50"
              style={{ borderColor: '#162744', color: '#162744' }}
            >
              Actualizar RUNT
            </button>
          )}
          <div className="hidden lg:block" aria-hidden="true" />
          {(rlState.status === 'found' ||
            rlState.status === 'not_found' ||
            rlState.status === 'error') && (
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
          {/* La escritura del representante que NO está en el directorio de la compañía. Va aquí,
              pegada a sus datos, porque es lo que acredita a la persona que se acaba de capturar:
              mandarla a Requisitos obligaría al gestor a recordar dos pasos más allá por qué se la
              piden. Sin ella el pie del asistente no deja pasar al siguiente paso. */}
          {exigeEscrituraRl(index) && (
            <div className="lg:col-span-4">
              <EscrituraRepresentanteUpload
                instanceId={instanceId}
                rol={actor.rol}
                onSatisfiedChange={(satisfied) => marcarEscrituraRl(index, satisfied)}
              />
            </div>
          )}
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
    // found/error/idle/empty — sin aviso; los campos quedan como estén y siguen editables.
    return null;
  };

  // Velo de espera SOLO para las consultas que dispara el gestor. Colgarlo de "hay una consulta en
  // curso" tapaba la pantalla al entrar al paso: en traspaso el vendedor se consulta solo al montar
  // (autoConsultRunt), y el velo aparecía en cada visita sin que nadie hubiera pedido nada. Una
  // espera que el usuario no provocó no se anuncia tapándole la pantalla.
  // Se declara ANTES de los dos returns: el layout partido sale antes y también lo necesita.
  const consultandoActor = consultasManuales > 0;

  // ── Layout SPLIT (un comprador): 2 secciones ──────────────────────────────
  if (isSplit) {
    // Multiple Propietario (ADR-0053) - el criterio de entrada a SPLIT es UN SOLO LADO (rol), no
    // un solo actor: `roles.length === 1` (implícito en `isSplit`) ya garantiza que TODOS los
    // actores en `actors` son del mismo rol. Con 2..4 propietarios de ese rol, SPLIT conserva su
    // presentación de siempre (secciones anchas, no rejilla) y solo cambia CUÁL actor pinta el
    // cuerpo — el de la pestaña activa — exactamente el mismo cálculo que usa el layout MULTI
    // (`activeTabByRol` + `indicesForRol`), reutilizado tal cual.
    const splitRol = roles[0];
    const splitIdxs = indicesForRol(actors, splitRol);
    const splitActiveOrdinal = Math.min(Math.max(activeTabByRol[splitRol] ?? 1, 1), splitIdxs.length || 1);
    const activeIndex = splitIdxs[splitActiveOrdinal - 1] ?? 0;
    const ownershipItems = ownershipItemsForRol(splitRol);
    const hasPercentagePanel = ownershipItems.length >= 2;
    const actor = actors[activeIndex];
    const errors = showErrors ? validation.byActor[activeIndex] : {};
    const runtState: LookupState = runt[activeIndex] ?? { status: 'idle' };
    const docLocked = autoConsultRunt && !!actor.numeroDocumento.trim();
    const razonLocked = isRazonSocialLocked(actor, activeIndex);
    const ciudades = filterCiudades(actor.ciudad ?? '');
    const showCiudades = !!ciudadOpen[activeIndex] && ciudades.length > 0;
    // Novedad nov.41 — este actor es el que recibe la precarga silenciosa del documento del
    // propietario (paso 1). Aplica al rol que ES ese propietario: el vendedor del traspaso, o el
    // titular donde el vehículo ya está inscrito y no hay parte vendedora (familia OTROS).
    const seedingOwnerDoc = seedDocumentoFromOwner && actor.rol === rolDelPropietario;
    // El titular de un trámite sobre vehículo inscrito es QUIEN FIGURA en el RUNT: ni el documento,
    // ni la identidad, ni el tipo de persona son suyos para cambiar. Cambiarlos no sería corregir un
    // dato, sería cambiar de persona — y cambiar de propietario es un traspaso, no una novedad.
    const esPropietarioInscrito = autoConsultRunt && actor.rol === rolDelPropietario;

    /**
     * Descripción de la tarjeta. `null` = no se pinta nada. El comprador NO tiene: el rótulo de la
     * tarjeta y el nombre del paso ya dicen a quién se está capturando, y la frase solo lo repetía
     * en prosa. Los otros tres sí aportan algo que no está en pantalla.
     */
    const descripcionDelActor: string | null = esPropietarioInscrito
      ? 'Los datos son los del propietario inscrito en el RUNT y no se pueden editar. Si el vehículo debe quedar a nombre de otra persona, el trámite es un traspaso.'
      : actor.rol === 'locatario'
        ? 'Registra la persona natural o jurídica que tiene el vehículo en arrendamiento. No firma el trámite: quien autoriza es el propietario.'
        : actor.rol === 'vendedor'
          ? 'Registra la persona natural o jurídica que figura hoy como propietario del vehículo.'
          : null;
    // Nombre / razón social: con la consulta resuelta el dato es el del registro. `razonLocked` solo
    // cubre la razón social que vino de RUES, y `isNameLockedByRunt` solo la persona natural, así
    // que una jurídica resuelta por otra vía quedaba editable — y ahí es donde se cambia de titular.
    const identidadDelRegistro = esPropietarioInscrito && runtState.status === 'found';
    const nombreBloqueado = razonLocked || isNameLockedByRunt(activeIndex, actor) || identidadDelRegistro;
    const rnmcIssueDate = issueDateField(activeIndex);
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

        {/* Sección A — Identificación. Sin selector de tipo de documento en el actor:
            natural → CC por defecto (RUNT puede corregirlo); jurídica → NIT fijo.
            Rejilla: número (span 2) | Consultar (+ hint a lo ancho). */}
        <WizardAccordion
          title={esPropietarioInscrito ? 'Datos del propietario actual' : `Datos del ${rotuloDelActor(actor.rol).toLowerCase()}`}
          defaultOpen
          /* Misma insignia de lectura que las tarjetas del traspaso: dice a qué registro se está
             consultando. Este layout no la tenía porque el interruptor PN/PJ ya lo decía por dentro;
             al quitarlo se quedaba sin ninguna lectura explícita de la naturaleza declarada. */
          badge={
            <StatusBadge
              tone={isJuridical(actor) ? 'info' : 'neutral'}
              label={personTypeLabel(actor)}
            />
          }
        >
          {/* Múltiple Propietario (ADR-0053) — la fila de pestañas va DENTRO de la tarjeta, como
              PRIMER elemento del cuerpo (el usuario lo pidió explícito: "va dentro de la tarjeta y
              no por fuera"). Solo matrícula inicial y traspaso, y solo sobre el actor que SE captura
              por formulario (no el propietario inscrito de la familia OTROS, cuya identidad viene
              fija del RUNT; no el locatario, fuera del alcance cerrado). SPLIT conserva su
              presentación de siempre — secciones anchas, nunca rejilla de tarjetas — y soporta 1..4
              propietarios del mismo modo que MULTI: el cuerpo pinta los datos de la pestaña ACTIVA
              (`activeIndex`), reemplazándolos al cambiar — nunca se apilan tarjetas. */}
          {!esPropietarioInscrito && actor.rol !== 'locatario' && (
            <OwnershipTabsBar
              items={ownershipItems}
              activeIndex={activeIndex}
              onSelectTab={(i) => selectOwnershipTab(actor.rol, i)}
              onAdd={() => addOwner(actor.rol)}
              onRemove={removeOwner}
              maxReached={ownershipItems.length >= MAX_OWNERS_PER_SIDE}
              readOnly={readOnly}
              idPrefix={`actor-${actor.rol}-single`}
              sideLabel={rotuloDelActor(actor.rol)}
              hasPercentagePanel={hasPercentagePanel}
            />
          )}
          {/*
            La descripción solo aparece cuando aclara algo que la pantalla no dice ya: el propietario
            inscrito (por qué no se puede editar), el locatario (que no firma) y el vendedor (que es
            el propietario de HOY, no el resultante). Para el comprador se retiró: el rótulo de la
            tarjeta y el paso ya lo dicen, y la frase solo repetía en prosa lo evidente.
          */}
          {descripcionDelActor ? (
            <p className="text-xs opacity-70 mb-3">{descripcionDelActor}</p>
          ) : null}
          <div className="space-y-3">
            {/* Identificación en UNA fila: tipo | número | Consultar. El selector de tipo estuvo un
                momento suelto en su propio renglón —era el hueco que dejó el interruptor PN/PJ al
                salir— y ahí solo gastaba alto: es un campo más de la misma captura. */}
            {!isJuridical(actor) ? (
              /* Natural: tipo | Número (col-span-2) | Consultar RUNT | hint col-span-4 */
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-4 lg:items-end">
                {docTypeSelector(activeIndex, 'comprador', isDocTypeLockedByRunt(activeIndex), '')}
                <div className="lg:col-span-2">
                  <label htmlFor="comprador-numeroDoc" className={`${WIZARD_LABEL} mb-1.5`}>
                    Número de documento
                  </label>
                  <input
                    id="comprador-numeroDoc"
                    type="text"
                    value={actor.numeroDocumento}
                    readOnly={docLocked || (seedingOwnerDoc && ownerSeedStatus === 'loading')}
                    onChange={(e) => updateActor(activeIndex, { numeroDocumento: e.target.value })}
                    onKeyDown={(e) => {
                      if (docLocked) return;
                      if (e.key === 'Enter') {
                        e.preventDefault();
                        conVelo(handleIdentityLookup(activeIndex));
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
                    onClick={() => conVelo(handleIdentityLookup(activeIndex))}
                    disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                    className="flex h-[42px] shrink-0 items-center justify-center rounded-xl bg-[#557EFF] px-5 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                    style={{ backgroundColor: WIZARD_BTN_SOLID, backgroundImage: 'none' }}
                    aria-label="Consultar RUNT"
                  >
                    {runtState.status === 'loading' ? 'Consultando…' : 'Consultar RUNT'}
                  </button>
                )}
                <p className="text-xs opacity-70 lg:col-span-4">
                  {esPropietarioInscrito
                    ? 'Los datos de identidad se toman de la consulta al RUNT del propietario actual.'
                    : actor.rol === 'comprador'
                      ? 'Consultamos el RUNT para autocompletar la información del comprador.'
                      : `Consultamos el RUNT para autocompletar la información del ${actor.rol}.`}
                </p>
              </div>
            ) : (
              /* Jurídica: tipo | NIT (col-span-2) | Consultar RUES | hint col-span-4 */
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-4 lg:items-end">
                {docTypeSelector(activeIndex, 'comprador', isDocTypeLockedByRunt(activeIndex), '')}
                <div className="lg:col-span-2">
                  <label htmlFor="comprador-numeroDoc" className={`${WIZARD_LABEL} mb-1.5`}>
                    NIT
                  </label>
                  <input
                    id="comprador-numeroDoc"
                    type="text"
                    value={actor.numeroDocumento}
                    readOnly={docLocked}
                    onChange={(e) => updateActor(activeIndex, { numeroDocumento: e.target.value })}
                    onKeyDown={(e) => {
                      if (docLocked) return;
                      if (e.key === 'Enter') {
                        e.preventDefault();
                        conVelo(handleIdentityLookup(activeIndex));
                      }
                    }}
                    aria-label="NIT"
                    aria-invalid={!!errors.numeroDocumento}
                    aria-describedby={errors.numeroDocumento ? 'comprador-numeroDoc-err' : undefined}
                    placeholder={`Número de documento del ${actor.rol}…`}
                    className={`${INPUT_BASE} font-mono${docLocked ? ' opacity-80' : ''}`}
                    style={docLocked ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                  />
                  {errors.numeroDocumento && (
                    <p id="comprador-numeroDoc-err" className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                      {errors.numeroDocumento}
                    </p>
                  )}
                </div>
                {!readOnly && !autoConsultRunt && (
                  <button
                    type="button"
                    onClick={() => conVelo(handleIdentityLookup(activeIndex))}
                    disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                    className="flex h-[42px] shrink-0 items-center justify-center rounded-xl bg-[#557EFF] px-5 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                    style={{ backgroundColor: WIZARD_BTN_SOLID, backgroundImage: 'none' }}
                    aria-label="Consultar RUES"
                  >
                    {runtState.status === 'loading' ? 'Consultando…' : 'Consultar RUES'}
                  </button>
                )}
                <p className="text-xs opacity-70 lg:col-span-4">
                  Validación de registro mercantil en RUES.
                </p>
              </div>
            )}
            {runtResult(activeIndex)}
          </div>
        </WizardAccordion>

        {/* Datos de contacto ANTES del representante legal en todos los trámites. */}
        <WizardAccordion title="Datos de contacto" defaultOpen>
          {/* Art. 5.1.10 — la notificación SÍ es editable aunque la identidad esté bloqueada: el
              RUNT puede traer un correo o una dirección desactualizados, y ahí es donde llegan los
              avisos del trámite. Bloquear quién es no implica bloquear dónde se le notifica. */}
          <p className="text-xs opacity-70 mb-3">
            {esPropietarioInscrito
              ? 'Confirma o corrige los datos de notificación del propietario. Estos sí son editables.'
              : actor.rol === 'locatario'
                // No es decorativo: el locatario recibe los correos de estado del trámite, y su
                // dirección y ciudad se estampan en el FUR.
                ? 'Datos de notificación del locatario: recibirá los avisos del trámite.'
                : 'Confirma o edita la información de notificación del propietario.'}
          </p>
          <div className="text-xs opacity-70">{contactLookupHint(activeIndex)}</div>
          <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
            {/* Fila 1: nombre | documento | correo */}
            <div>
              <label htmlFor="comprador-nombre" className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                {isJuridical(actor) ? 'Razón social' : 'Nombres y apellidos'}
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
              </label>
              <input
                id="comprador-nombre"
                type="text"
                value={
                  isJuridical(actor)
                    ? shortRuesRazonSocial(actor.nombreCompleto) || actor.nombreCompleto
                    : actor.nombreCompleto
                }
                onChange={(e) => updateActor(activeIndex, { nombreCompleto: e.target.value })}
                readOnly={nombreBloqueado}
                aria-invalid={!!errors.nombreCompleto}
                aria-describedby={
                  errors.nombreCompleto
                    ? 'comprador-nombre-err'
                    : (runtState.status === 'error' || runtState.status === 'not_found')
                      ? 'comprador-nombre-hint'
                      : undefined
                }
                className={`${INPUT_BASE}${nombreBloqueado ? ' opacity-80' : ''}`}
                style={nombreBloqueado ? { background: 'rgba(223,229,237,0.35)' } : undefined}
              />
              {errors.nombreCompleto && (
                <p id="comprador-nombre-err" className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                  {errors.nombreCompleto}
                </p>
              )}
              {(runtState.status === 'error' || runtState.status === 'not_found') &&
                !isJuridical(actor) && (
                <p id="comprador-nombre-hint" className="text-xs mt-1 opacity-70">
                  Consulta sin resultado — puedes ingresar el nombre manualmente.
                </p>
              )}
            </div>
            <div>
              <label htmlFor="comprador-doc-ro" className={`${WIZARD_LABEL} mb-1.5`}>
                N° Documento
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
                  markContactTouched(activeIndex, 'email');
                  updateActor(activeIndex, { email: e.target.value });
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
            {/* Fila 2: teléfono | ciudad | dirección */}
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
                  markContactTouched(activeIndex, 'telefono');
                  updateActor(activeIndex, { telefono: e.target.value });
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
                  markContactTouched(activeIndex, 'ciudad');
                  updateActor(activeIndex, { ciudad: e.target.value });
                  setCiudadOpen((p) => ({ ...p, [activeIndex]: true }));
                }}
                onFocus={() => {
                  if ((actor.ciudad ?? '').trim().length >= 2) setCiudadOpen((p) => ({ ...p, [activeIndex]: true }));
                }}
                onBlur={() => setTimeout(() => setCiudadOpen((p) => ({ ...p, [activeIndex]: false })), 150)}
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
                          markContactTouched(activeIndex, 'ciudad');
                          updateActor(activeIndex, { ciudad: c });
                          setCiudadOpen((p) => ({ ...p, [activeIndex]: false }));
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
            <div>
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
                  markContactTouched(activeIndex, 'direccion');
                  updateActor(activeIndex, { direccion: e.target.value });
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
          {/* RNMC: fuera de la grilla 3×2 del prototipo para no romper la composición */}
          {rnmcIssueDate ? <div className="mt-4 max-w-sm">{rnmcIssueDate}</div> : null}
        </WizardAccordion>

        {isJuridical(actor) && (
          <WizardAccordion title="Representante legal" defaultOpen>
            {rlSection(activeIndex)}
          </WizardAccordion>
        )}

        {/* Múltiple Propietario (ADR-0053) — "Porcentaje de propiedad" cierra el formulario,
            después de todos los datos del actor (igual que en el layout MULTI). Solo se monta con
            2+ propietarios EN ESTE MOMENTO — sin memoria histórica: si el lado vuelve a 1, el
            bloque desaparece de nuevo. */}
        {!esPropietarioInscrito && actor.rol !== 'locatario' && hasPercentagePanel && (
          <OwnershipPercentagePanel
            items={ownershipItems}
            activeIndex={activeIndex}
            onPercentageChange={updateOwnershipPercentage}
            readOnly={readOnly}
            idPrefix={`actor-${actor.rol}-single`}
            showErrors={showErrors}
          />
        )}

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
      {rlSwitchConfirm && (
        <RlSwitchConfirmModal
          variant={rlSwitchConfirm.variant}
          onCancel={() => resolveRlSwitchConfirm(false)}
          onConfirm={() => resolveRlSwitchConfirm(true)}
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

      {/* Actores: AccordionRow sincronizado (prototipo Lovable) — un solo open para las dos
          tarjetas; al colapsar/expandir cualquiera se mueven ambas. */}
      <WizardAccordionRow defaultOpen>
      <div className="grid grid-cols-1 gap-3 xl:grid-cols-2 items-stretch">
        {/* Múltiple Propietario (ADR-0053) — una tarjeta POR LADO (rol), no por actor: con 2+
            propietarios de un mismo lado se pinta solo el de la pestaña ACTIVA (`activeTabByRol`);
            el resto de la lógica del actor (identidad, representante legal, contacto…) sigue
            siendo la MISMA de siempre, generalizada por índice real — nada de eso cambió. */}
        {roles
          .map((rol) => {
            const idxs = indicesForRol(actors, rol);
            if (idxs.length === 0) return null;
            const activeOrdinal = Math.min(Math.max(activeTabByRol[rol] ?? 1, 1), idxs.length);
            const index = idxs[activeOrdinal - 1] ?? idxs[0];
            return { actor: actors[index], index };
          })
          .filter((d): d is { actor: ProcedureActor; index: number } => d !== null)
          .map(({ actor, index }) => {
          const errors = showErrors ? validation.byActor[index] : {};
          const prefix = `actor-${actor.rol}`;
          const runtState: LookupState = runt[index] ?? { status: 'idle' };
          const razonLocked = isRazonSocialLocked(actor, index);
          const docLocked =
            autoConsultRunt &&
            actor.rol === rolDelPropietario &&
            !!actor.numeroDocumento.trim();
          // Novedad nov.41 — este actor es el que recibe la precarga silenciosa del documento del
          // propietario (paso 1): el vendedor del traspaso, o el propietario inscrito allí donde no
          // hay parte vendedora (familia OTROS, leasing).
          const seedingOwnerDoc = seedDocumentoFromOwner && actor.rol === rolDelPropietario;
          const ciudadesSuggestions = filterCiudades(actor.ciudad ?? '');
          const showCiudadSuggestions = !!ciudadOpen[index] && ciudadesSuggestions.length > 0;
          // La tarjeta del PROPIETARIO INSCRITO: su identidad la trae el RUNT y no se teclea, frente
          // a la tarjeta de captura libre de la otra parte. Era `rol === 'vendedor'`, que en un
          // leasing dejaba al propietario con la tarjeta libre —la del comprador— y por tanto
          // editable. El vendedor conserva su trato EXACTO (primer término, sin condición añadida):
          // `ActorsForm` también se usa fuera del asistente, sin consulta automática.
          const esPropietarioDelRegistro =
            actor.rol === 'vendedor' || (actor.rol === rolDelPropietario && autoConsultRunt);
          // ADR-0051 — la tarjeta 'vendedor' está en pantalla por `revealSellerForm` (excepción por
          // instancia), NO porque este paso la capture (`rolDelPropietario !== 'vendedor'`): el
          // backend ya sincronizó a este propietario desde el RUNT por su cuenta. Su identidad
          // (documento, nombre, tipo de persona) va en solo lectura — no hay consulta que disparar
          // ni documento que sembrarle aquí — y el formulario se acota a lo que falta para poder
          // solicitarle la firma: representante legal (jurídica) o correo (natural).
          const vendedorSincronizado = actor.rol === 'vendedor' && rolDelPropietario !== 'vendedor';
          // Píldora de estado de la cabecera (Vendedor sin autoConsultRunt) — tintada, no sólida.
          const statusPill: { text: string; tone: StatusTone } =
            runtState.status === 'found'
              ? { text: 'Verificado', tone: 'success' }
              : runtState.status === 'loading'
                ? { text: 'Consultando…', tone: 'info' }
                : runtState.status === 'not_found' || runtState.status === 'error'
                  ? { text: 'No verificado', tone: 'danger' }
                  : { text: 'Pendiente', tone: 'neutral' };
          // Múltiple Propietario (ADR-0053) — pestañas de este lado + posición ordinal de la
          // tarjeta activa. `rotuloDelActor` solo recibe el ordinal cuando el lado está `revealed`
          // (2+ propietarios en algún momento): con un solo actor el rótulo NO cambia (sin "1").
          // `ownershipRevealedForRol` es HISTÓRICA a propósito (gobierna el rótulo de la tarjeta,
          // sin cambios en este ajuste) — NO confundir con `hasPercentagePanel`, que es del
          // MOMENTO (gobierna si se monta el bloque "Porcentaje de propiedad": el usuario cerró que
          // ese bloque se oculta de nuevo si el lado vuelve a un solo propietario).
          const ownershipItems = ownershipItemsForRol(actor.rol);
          const ownershipRevealedForRol = !!ownershipRevealed[actor.rol];
          const hasPercentagePanel = ownershipItems.length >= 2;
          const activeOwnershipItem =
            ownershipItems.find((it) => it.index === index) ?? ownershipItems[0];
          const rotulo =
            ownershipRevealedForRol && activeOwnershipItem
              ? rotuloDelActor(actor.rol, activeOwnershipItem.ordinal)
              : rotuloDelActor(actor.rol);
          // Solo matrícula inicial y traspaso, y solo sobre el lado que SE captura por formulario
          // (no el vendedor sincronizado por el backend, ADR-0051; no el locatario, fuera del
          // alcance cerrado del encargo). Gobierna AMBAS piezas (pestañas arriba, porcentaje abajo).
          const mostrarOwnership = !vendedorSincronizado && actor.rol !== 'locatario';
          return (
            <div
              key={actor.rol}
              role="group"
              aria-label={rotulo}
              className="flex h-full flex-col"
            >
            <WizardAccordion
              title={rotulo}
              level="h3"
              regionLabel={rotulo}
              className="flex h-full flex-col"
              subtitle={
                vendedorSincronizado
                  ? 'Excepción: el propietario ya está sincronizado, pero falta un dato para poder solicitarle la firma.'
                  : esPropietarioDelRegistro && autoConsultRunt
                    ? 'Los datos de identidad se toman automáticamente de la consulta en RUNT.'
                    : undefined
              }
              /* La naturaleza de la persona pasa a ser SIEMPRE lectura: la declara el selector de
                 documento de adentro, y aquí se acusa recibo de lo que quedó declarado. La única
                 excepción es el propietario del registro mientras el RUNT no ha respondido: ahí
                 todavía no hay nada que acusar y manda el estado de la consulta. */
              badge={
                esPropietarioDelRegistro && !vendedorSincronizado && !(autoConsultRunt && isRuntFound(index)) ? (
                  <StatusBadge label={statusPill.text} tone={statusPill.tone} />
                ) : (
                  <StatusBadge
                    tone={isJuridical(actor) ? 'info' : 'neutral'}
                    label={personTypeLabel(actor)}
                  />
                )
              }
            >
              {/* Múltiple Propietario (ADR-0053) — la fila de pestañas va DENTRO de la tarjeta,
                  como PRIMER elemento del cuerpo (el usuario lo pidió explícito: "va dentro de la
                  tarjeta y no por fuera") — antes vivía como hermana del `WizardAccordion`, flotando
                  por encima de la tarjeta en vez de pertenecer a ella. */}
              {mostrarOwnership && (
                <OwnershipTabsBar
                  items={ownershipItems}
                  activeIndex={index}
                  onSelectTab={(i) => selectOwnershipTab(actor.rol, i)}
                  onAdd={() => addOwner(actor.rol)}
                  onRemove={removeOwner}
                  maxReached={ownershipItems.length >= MAX_OWNERS_PER_SIDE}
                  readOnly={readOnly}
                  idPrefix={`actor-${actor.rol}`}
                  sideLabel={rotuloDelActor(actor.rol)}
                  hasPercentagePanel={hasPercentagePanel}
                />
              )}
              <div className="space-y-4">

                {/* ADR-0051 — vendedor sincronizado por el backend (revelado por excepción): la
                    identidad ya está resuelta y no se teclea aquí. Solo se pinta lo que falta para
                    poder solicitarle la firma (representante legal o correo, más abajo). */}
                {vendedorSincronizado && (
                  <div className="space-y-3">
                    <InlineAlert tone="info">
                      Este es el propietario que figura en el RUNT. Sus datos de identidad ya se
                      sincronizaron automáticamente al vehículo; faltan{' '}
                      {isJuridical(actor) ? 'los del representante legal' : 'sus datos de contacto'}{' '}
                      para poder enviarle la validación de identidad y la firma del trámite. Es una
                      excepción de este trámite — en el resto de traspasos el propietario se captura
                      en su propio formulario.
                    </InlineAlert>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                      <div>
                        <label htmlFor={`${prefix}-numeroDoc`} className={`${WIZARD_LABEL} mb-1.5`}>
                          {isJuridical(actor) ? 'NIT' : 'Número de documento'}
                        </label>
                        <input
                          id={`${prefix}-numeroDoc`}
                          type="text"
                          value={actor.numeroDocumento}
                          readOnly
                          disabled
                          aria-label={isJuridical(actor) ? 'NIT del propietario' : 'Número de documento del propietario'}
                          className={`${INPUT_BASE} font-mono opacity-80`}
                          style={{ background: 'rgba(223,229,237,0.35)' }}
                        />
                      </div>
                      <div>
                        <label htmlFor={`${prefix}-nombre`} className={`${WIZARD_LABEL} mb-1.5`}>
                          {isJuridical(actor) ? 'Razón social' : 'Nombres y apellidos'}
                        </label>
                        <input
                          id={`${prefix}-nombre`}
                          type="text"
                          value={
                            isJuridical(actor)
                              ? shortRuesRazonSocial(actor.nombreCompleto) || actor.nombreCompleto || 'No registra'
                              : actor.nombreCompleto || 'No registra'
                          }
                          readOnly
                          disabled
                          aria-label={isJuridical(actor) ? 'Razón social del propietario' : 'Nombre del propietario'}
                          className={`${INPUT_BASE} opacity-80`}
                          style={{ background: 'rgba(223,229,237,0.35)' }}
                        />
                      </div>
                    </div>
                  </div>
                )}

                {/* Vendedor sin RUNT fijo: elige documento. Con RUNT OK la identidad ya vino del
                    registro y solo se lee en el badge de la cabecera. */}
                {!vendedorSincronizado && esPropietarioDelRegistro && !(autoConsultRunt && isRuntFound(index)) && (
                  docTypeSelector(index, prefix, isDocTypeLockedByRunt(index))
                )}

                {/* ── Identificación ── */}
                {!vendedorSincronizado && (!esPropietarioDelRegistro ? (
                  /* Comprador: grid sm:grid-cols-3 — Tipo de doc | Número | Consultar (Lovable P1) */
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end">
                    <div>
                      <label htmlFor={`${prefix}-tipoDoc`} className={`${WIZARD_LABEL} mb-1.5`}>
                        Tipo de documento
                      </label>
                      {/* Sin filtrar ni deshabilitar: este selector ES el control de la naturaleza
                          de la persona. Antes obedecía al interruptor PN/PJ —se apagaba en jurídica
                          y escondía el NIT en natural— y ahora es al revés. */}
                      <select
                        id={`${prefix}-tipoDoc`}
                        value={actor.tipoDocumento}
                        disabled={readOnly}
                        onChange={(e) => updateActor(index, { tipoDocumento: e.target.value as ActorDocumentType })}
                        className={`${WIZARD_SELECT} mt-1.5`}
                      >
                        {DOC_OPTIONS.map((o) => (
                          <option key={o.value} value={o.value}>{o.label}</option>
                        ))}
                      </select>
                    </div>
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
                        aria-invalid={!!errors.numeroDocumento}
                        aria-describedby={errors.numeroDocumento ? `${prefix}-numeroDoc-err` : undefined}
                        className={INPUT_BASE}
                      />
                      {errors.numeroDocumento && (
                        <p id={`${prefix}-numeroDoc-err`} className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                          {errors.numeroDocumento}
                        </p>
                      )}
                    </div>
                    {!readOnly && (
                      <button
                        type="button"
                        onClick={() => conVelo(handleIdentityLookup(index))}
                        disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                        className="flex h-[42px] shrink-0 items-center justify-center rounded-xl bg-[#557EFF] px-5 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                        style={{ backgroundColor: WIZARD_BTN_SOLID, backgroundImage: 'none' }}
                      >
                        {runtState.status === 'loading'
                          ? 'Consultando…'
                          : isJuridical(actor) ? 'Consultar RUES' : 'Consultar RUNT'}
                      </button>
                    )}
                    <p className="text-xs opacity-70 sm:col-span-3">
                      {isJuridical(actor)
                        ? 'Validación de registro mercantil en RUES.'
                        : 'La información se valida directamente con RUNT.'}
                    </p>
                  </div>
                ) : (
                  /* Vendedor: con RUNT OK la identidad vive solo en runtResult (prototipo).
                     Sin consulta / error: número + Consultar para completar. */
                  !(autoConsultRunt && isRuntFound(index)) ? (
                  <div className="sm:max-w-md">
                    <label htmlFor={`${prefix}-numeroDoc`} className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                      {isJuridical(actor) ? 'NIT' : 'Número de documento'}
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
                    {!readOnly && !docLocked && (
                      <button
                        type="button"
                        onClick={() => conVelo(handleIdentityLookup(index))}
                        disabled={runtState.status === 'loading' || !actor.numeroDocumento.trim() || !instanceId}
                        className="mt-2 rounded-xl bg-[#557EFF] px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-50"
                        style={{ backgroundColor: WIZARD_BTN_SOLID, backgroundImage: 'none' }}
                      >
                        {runtState.status === 'loading'
                          ? 'Consultando…'
                          : isJuridical(actor) ? 'Consultar RUES' : 'Consultar RUNT'}
                      </button>
                    )}
                  </div>
                  ) : null
                ))}

                {/* Validación de identidad (RUNT/RUES) — al frente, antes del contacto. */}
                {!vendedorSincronizado && runt[index] && runt[index].status !== 'idle' && (
                  <div>{runtResult(index)}</div>
                )}

                {/* Nombre / razón social — oculto cuando la identidad natural ya viene en runtResult
                    (prototipo: RuntPersona). Comprador PN con RUNT OK no debe repetir
                    «Nombres y apellidos». Jurídica sigue mostrando razón social. Oculto también
                    cuando el nombre ya se pintó en solo lectura arriba (vendedor sincronizado). */}
                {!vendedorSincronizado && !(isRuntFound(index) && !isJuridical(actor)) && (
                <div>
                  <label htmlFor={`${prefix}-nombre`} className={`${WIZARD_LABEL} mb-1.5 flex items-center gap-1.5`}>
                    {isJuridical(actor) ? 'Razón social' : 'Nombres y apellidos'}
                    <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
                  </label>
                  <input
                    id={`${prefix}-nombre`}
                    type="text"
                    value={
                      isJuridical(actor)
                        ? shortRuesRazonSocial(actor.nombreCompleto) || actor.nombreCompleto
                        : actor.nombreCompleto
                    }
                    onChange={(e) => updateActor(index, { nombreCompleto: e.target.value })}
                    readOnly={razonLocked || isNameLockedByRunt(index, actor)}
                    aria-invalid={!!errors.nombreCompleto}
                    aria-describedby={
                      errors.nombreCompleto
                        ? `${prefix}-nombre-err`
                        : (runtState.status === 'error' || runtState.status === 'not_found')
                          ? `${prefix}-nombre-hint`
                          : undefined
                    }
                    className={`${INPUT_BASE}${razonLocked || isNameLockedByRunt(index, actor) ? ' opacity-80' : ''}`}
                    style={razonLocked || isNameLockedByRunt(index, actor) ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                  />
                  {errors.nombreCompleto && (
                    <p id={`${prefix}-nombre-err`} className="text-xs mt-1" style={{ color: '#FF4E00' }}>
                      {errors.nombreCompleto}
                    </p>
                  )}
                  {(runtState.status === 'error' || runtState.status === 'not_found') && !isJuridical(actor) && (
                    <p id={`${prefix}-nombre-hint`} className="text-xs mt-1 opacity-70">
                      Consulta sin resultado — puedes ingresar el nombre manualmente.
                    </p>
                  )}
                </div>
                )}

                {/* ── Datos de contacto (nested bordered box — Lovable P1) ──
                    ADR-0051 — para el vendedor sincronizado (revelado por excepción) esta caja
                    sigue obligatoria para TODO tipo de persona: son campos requeridos del actor
                    (HU #11595) igual que en el resto del formulario, y hoy ya se capturan a mano
                    incluso cuando la identidad viene bloqueada por RUNT (`esPropietarioDelRegistro`
                    con `autoConsultRunt`). En persona jurídica, además, falta el representante
                    legal — capturado aparte en `rlSection` más abajo. */}
                <div className="rounded-xl border p-4 space-y-4" style={{ borderColor: '#DFE5ED' }}>
                  <p className="text-[13px] font-semibold" style={{ color: '#162744' }}>Datos de contacto</p>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
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
                    {/* Teléfono — obligatorio (HU #11595 / validación HEAD) */}
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
                    {/* Ciudad — HU #10956, precargable desde contacto ya conocido */}
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
                    {/* Dirección — HU #10956 / #11595 */}
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
                  </div>
                  <p className="text-xs opacity-70">
                    Puedes editar y actualizar los datos de contacto de este actor.
                  </p>
                  {/* Precarga de contacto: solo vendedor. En comprador el prototipo no muestra
                      este aviso (ContactoBlock = copy de edición únicamente). */}
                  {esPropietarioDelRegistro && contactLookupHint(index) && (
                    <div>{contactLookupHint(index)}</div>
                  )}
                </div>

                {/* Fecha de expedición del documento (RNMC, solo persona natural) */}
                {!vendedorSincronizado && issueDateField(index)}

                {/* Representante legal DESPUÉS del contacto de la empresa (Lovable Traspaso P1) */}
                {rlSection(index)}

                {/* Múltiple Propietario (ADR-0053) — "Porcentaje de propiedad" CIERRA la tarjeta,
                    después de todos los datos del actor (maqueta de referencia: va tras "Datos de
                    contacto"). Solo se monta con 2+ propietarios EN ESTE MOMENTO — no hay memoria
                    histórica: si el lado vuelve a 1, este bloque desaparece de nuevo. */}
                {mostrarOwnership && hasPercentagePanel && (
                  <OwnershipPercentagePanel
                    items={ownershipItems}
                    activeIndex={index}
                    onPercentageChange={updateOwnershipPercentage}
                    readOnly={readOnly}
                    idPrefix={`actor-${actor.rol}`}
                    showErrors={showErrors}
                  />
                )}
              </div>
            </WizardAccordion>
            </div>
          );
        })}
      </div>
      </WizardAccordionRow>

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
    {rlSwitchConfirm && (
      <RlSwitchConfirmModal
        variant={rlSwitchConfirm.variant}
        onCancel={() => resolveRlSwitchConfirm(false)}
        onConfirm={() => resolveRlSwitchConfirm(true)}
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

function RlSwitchConfirmModal({
  variant,
  onCancel,
  onConfirm,
}: {
  variant: 'runt' | 'preload';
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
      title="Cambiar representante legal"
      icon={AlertTriangle}
      iconBg="#B26A00"
      size="sm"
    >
      <div ref={containerRef} className="space-y-4">
        <div className="space-y-2 text-xs" role="alert">
          {variant === 'preload' ? (
            <>
              <p>
                Encontramos un representante legal registrado en esta compañía con ese documento.
              </p>
              <p>
                La firma e identidad del representante anterior dejarán de apalancarse en este
                trámite.
              </p>
            </>
          ) : (
            <>
              <p>
                Vas a consultar otro documento en <span className="font-semibold">RUNT</span>.
              </p>
              <p>Los datos básicos del representante anterior se reemplazarán por el resultado.</p>
            </>
          )}
          <p className="opacity-70">¿Deseas continuar?</p>
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

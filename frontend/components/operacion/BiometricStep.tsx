'use client';

import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import {
  ChevronDown,
  Copy,
  ExternalLink,
  FileSignature,
  RefreshCw,
  RotateCcw,
} from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { tramitesClient, getIdentitySendConflict } from '@/lib/api/tramites-client';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { WizardCardHeader } from './wizard-atoms';
import { WizardAccordion } from './WizardAccordion';
import { WIZARD_CARD, WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { useWizardFocusTrap } from './use-wizard-focus-trap';
import { motivoDeParte, presentarMotivoNoEnvio } from './envio-validacion-motivos';
import { INLINE_ALERT_TONES } from '@/components/atom/InlineAlert';
import type {
  BiometricEstado,
  BiometricParte,
  BiometricValidation,
  EnvioValidacionMotivo,
  MecanismoFirma,
  ProcedureActor,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Re-consulta el estado del wizard tras iniciar/refrescar (server-driven). */
  onRefresh?: () => void;
  /**
   * Oculta el párrafo introductorio cuando el contenedor ya describe el paso
   * (paso `identidad`: el h2 + subtítulo del wizard lo cubren). En `fur` NO se
   * oculta: ahí la biométrica es una subsección dentro de "Resumen del trámite".
   */
  hideIntro?: boolean;
  /**
   * Si se indica, solo renderiza las tarjetas de esas partes (p. ej. embeber
   * Comprador o Vendedor dentro del resumen del trámite).
   */
  onlyPartes?: BiometricParte[];
  /**
   * Paso Identidad: título + subtítulo dentro del mismo panel blanco que la captura.
   */
  heading?: string;
  headingSubtitle?: string;
  /**
   * HU #10646 — partes (NIT/jurídicas) cuya identidad quedó cubierta por la firma electrónica del baúl,
   * capturadas del outcome `firma_baul` de ensureIdentity durante el registro.
   *
   * <p>Refuerzo optimista, NO la fuente de verdad. Lo era hasta que se descubrió que, al reabrir el
   * trámite desde el listado, este estado en memoria llega vacío y la parte se rotulaba como «Identidad
   * verificada» aunque hubiera firmado por el baúl. La fuente es `firmaBaulPartes` del propio estado
   * biométrico, que desde la HU #11014 lo expone por parte y desde el Bug #11141 respeta además el
   * mecanismo elegido por el gestor. Se unen porque durante el registro la respuesta del servidor puede
   * ir un paso por detrás del outcome que el wizard acaba de recibir.</p>
   */
  vaultCoveredPartes?: BiometricParte[];
  /**
   * Se embebe dentro de otra tarjeta ya con chrome propio (`MatriculaResumen` → `ResumenCard`):
   * las tarjetas internas (firma + biométrica) dejan su borde/fondo/radio propios — que antes
   * dibujaban una tarjeta dentro de otra— y pasan a un tratamiento más liviano, sin chrome duplicado.
   * En su propio paso (`onlyPartes` ausente / `heading` presente) esta prop no se usa.
   */
  embedded?: boolean;
  /**
   * HU #11666 — lleva al gestor al paso de actores de esa parte para completar los datos del
   * representante legal. Es la acción correctiva de los motivos de no envío que SÍ dependen de él
   * (`rl_sin_documento`, `rl_sin_correo`, `sujeto_no_es_representante`). Ausente cuando el paso se
   * embebe fuera del asistente (resumen del trámite): entonces el aviso explica el motivo pero no
   * ofrece un botón que no podría navegar a ninguna parte.
   */
  onIrAActores?: (parte: BiometricParte) => void;
}

/**
 * Partes que requieren biométrica por modalidad.
 *
 * HU21 — saliente antes que entrante: el vendedor se muestra primero, igual que el resumen de
 * firmas del paso FUR (HU #11019), el expediente y el listado. Antes el resumen de identidad
 * era el único que invertía el orden.
 */
function partesFor(modalidad: WizardModalidad): BiometricParte[] {
  return modalidad === 'traspaso' ? ['vendedor', 'comprador'] : ['comprador'];
}

const PARTE_LABEL: Record<BiometricParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

const KYVERUM = 'kyverum';

// CF-08 (Feature #11004, HU #11009) — etiquetas del historial (mismo vocabulario que Validaciones/
// Prevalidaciones e IdentityStatusPanel).
const ESTADO_LABEL: Record<BiometricEstado, string> = {
  enviado: 'Enviado',
  en_proceso: 'En proceso',
  aprobado: 'Aprobado',
  rechazado: 'Rechazado',
  expirado: 'Expirado',
  pendiente_envio: 'Pendiente de envío',
  error_envio: 'Error de envío',
};

const ESTADO_TONE: Record<BiometricEstado, StatusTone> = {
  enviado: 'info',
  en_proceso: 'warning',
  aprobado: 'success',
  rechazado: 'danger',
  expirado: 'neutral',
  pendiente_envio: 'info',
  error_envio: 'danger',
};

/**
 * Chip de estado de la cabecera de cada `ParteCard`, en paridad con la referencia del diseño (cada
 * tarjeta trae un badge — «Identidad verificada — 78/100» / «Pendiente»). Se calcula aparte de
 * `ESTADO_LABEL`/`ESTADO_TONE` porque cubre dos casos que esos mapas no representan, ninguno de los
 * dos un `BiometricEstado` real: la parte que TODAVÍA no tiene ninguna validación (no hay "estado" que
 * pedirle al backend) y la cobertura por firma del baúl (HU #10646: la identidad se resuelve fuera de
 * la biométrica, así que tampoco tiene un `BiometricEstado`).
 */
function parteBadge(
  validation: BiometricValidation | null,
  vaultCovered: boolean,
): { label: string; tone: StatusTone } {
  if (vaultCovered) {
    return { label: 'Cubierto por firma del baúl', tone: 'success' };
  }
  if (!validation) {
    // Prototipo ValidacionCard: "Pendiente" (warn). Misma condición: sin fila biométrica aún.
    return { label: 'Pendiente', tone: 'warning' };
  }
  // Un enlace vencido cae a RejectedView aunque el estado crudo siga en `en_proceso` (ver ParteCard);
  // el chip debe reflejar lo que el gestor ve en la tarjeta, no el campo crudo del backend.
  if (validation.expired) {
    return { label: ESTADO_LABEL.expirado, tone: ESTADO_TONE.expirado };
  }
  if (validation.status === 'aprobado') {
    return {
      label:
        validation.score != null
          ? `${ESTADO_LABEL.aprobado} — ${validation.score}/100`
          : ESTADO_LABEL.aprobado,
      tone: ESTADO_TONE.aprobado,
    };
  }
  return { label: ESTADO_LABEL[validation.status], tone: ESTADO_TONE[validation.status] };
}

/** Formatea una fecha ISO a un texto legible (es-CO). Devuelve el ISO crudo si no parsea. */
function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

/**
 * Datos del recuadro de identidad de una `ParteCard`: nombre/razón social + línea de documento.
 * Orden de fuente (ver comentario de `PartyIdentityBox`): 1) la validación biométrica, si ya
 * existe — trae su propio `name`/`documentType`/`documentNumber`, sea cual sea su estado; 2) el
 * actor del trámite, solo cuando NO hay validación todavía (parte "Sin iniciar"). Jurídica con
 * representante legal usa la línea "Rep. Legal: {nombre} · {tipoDoc} {numero}", igual que la
 * referencia del diseño; jurídica sin representante capturado cae al documento del propio actor
 * (el NIT). `null` cuando no hay ningún dato disponible — el recuadro entonces no se pinta.
 */
function personaInfoFor(
  validation: BiometricValidation | null,
  actor: ProcedureActor | null,
): { nombre: string; documentoLine: string } | null {
  if (validation) {
    return {
      nombre: validation.name,
      documentoLine: `${validation.documentType} ${validation.documentNumber}`,
    };
  }
  if (!actor) return null;
  const rep = actor.representanteLegal;
  if (actor.personType === 'juridical' && rep?.nombreCompleto) {
    const doc = [rep.tipoDocumento, rep.numeroDocumento].filter(Boolean).join(' ');
    return {
      nombre: actor.nombreCompleto,
      documentoLine: doc
        ? `Rep. Legal: ${rep.nombreCompleto} · ${doc}`
        : `Rep. Legal: ${rep.nombreCompleto}`,
    };
  }
  return {
    nombre: actor.nombreCompleto,
    documentoLine: `${actor.tipoDocumento} ${actor.numeroDocumento}`,
  };
}


/**
 * Paso de validación de identidad. Es provider-aware (HU #10233): con `kyverum` el clic dispara la
 * validación real (Kyverum captura remota + webhook), tomando los datos del actor del trámite y
 * mostrando el enlace de captura (link + QR) que también se envía por correo al cliente; el estado se
 * refresca solo (polling) hasta aprobado/rechazado. Con `mock` el clic simula la validación (score 95).
 * El status/gating lo decide el wizard server-driven: este paso refresca tras iniciar/simular.
 */
export function BiometricStep({
  instanceId,
  modalidad,
  onRefresh,
  hideIntro = false,
  onlyPartes,
  heading,
  headingSubtitle,
  vaultCoveredPartes = [],
  embedded = false,
  onIrAActores,
}: Props) {
  const partes = onlyPartes?.length
    ? partesFor(modalidad).filter((p) => onlyPartes.includes(p))
    : partesFor(modalidad);
  // Solo lectura (Track C): sin iniciar/simular validación.
  const readOnly = useWizardReadOnly();

  const [validations, setValidations] = useState<BiometricValidation[] | null>(null);
  const [provider, setProvider] = useState<string>('mock');
  // Partes cubiertas por el baúl según el BACKEND. Se consulta en vez de depender solo de la prop
  // porque esta última solo existe durante el registro; al reabrir el trámite llegaba vacía.
  const [firmaBaulServidor, setFirmaBaulServidor] = useState<string[]>([]);
  // HU #11666 — motivos tipificados de NO envío por parte. El backend los calcula al vuelo, así que
  // llegan y desaparecen con cada refresco: nunca se acumulan en este estado.
  const [motivosNoEnvio, setMotivosNoEnvio] = useState<EnvioValidacionMotivo[]>([]);
  const [error, setError] = useState<string | null>(null);
  // Actores del trámite: solo se usan para el recuadro "quién es la persona" cuando una parte
  // TODAVÍA no tiene ninguna validación (la validación, si existe, ya trae name/documentType/
  // documentNumber). Se carga UNA vez por montaje, aparte de `load()` (que sí se repite con el
  // polling): no es dato que cambie mientras el gestor espera Kyverum, y no debe reiniciar el
  // recuadro en cada refresco. Si falla, el recuadro simplemente no se pinta (`actors` se queda en
  // `null`) — nunca bloquea el resto del paso.
  const [actors, setActors] = useState<ProcedureActor[] | null>(null);

  useEffect(() => {
    if (!instanceId) return;
    let cancelled = false;
    tramitesClient
      .getActors(instanceId)
      .then((data) => {
        if (!cancelled) setActors(data);
      })
      .catch(() => {
        /* el recuadro de identidad es un refuerzo visual, no una fuente crítica: sin actores el
           paso sigue funcionando (validación aprobada igual trae su propio nombre/documento). */
      });
    return () => {
      cancelled = true;
    };
  }, [instanceId]);

  const load = useCallback(async () => {
    if (!instanceId) return null;
    try {
      const state = await tramitesClient.getBiometricState(instanceId);
      setValidations(state.validations);
      setProvider(state.provider);
      setFirmaBaulServidor(state.firmaBaulPartes ?? []);
      setMotivosNoEnvio(state.motivosNoEnvio ?? []);
      setError(() => null);
      return state;
    } catch (err) {
      setError(() =>
        err instanceof Error ? err.message : 'Error al cargar las validaciones.',
      );
      return null;
    }
  }, [instanceId]);

  useEffect(() => {
    void load();
  }, [load]);

  // Kyverum es asíncrono: el resultado llega por webhook. Mientras haya una validación en proceso se
  // refresca solo cada 5s para reflejar aprobado/rechazado sin que el gestor tenga que recargar. Al
  // resolverse (ya no queda ninguna en_proceso) se notifica al wizard (onRefresh) para que recomponga
  // el gate server-driven y habilite "Continuar" sin requerir un clic manual en "Actualizar".
  useEffect(() => {
    if (provider !== KYVERUM) return;
    const pending = (validations ?? []).some((v) => v.status === 'en_proceso');
    if (!pending) return;
    const timer = setInterval(async () => {
      const state = await load();
      const stillPending = (state?.validations ?? []).some((v) => v.status === 'en_proceso');
      if (state && !stillPending) onRefresh?.();
    }, 5000);
    return () => clearInterval(timer);
  }, [provider, validations, load, onRefresh]);

  const handleRefresh = async () => {
    await load();
    onRefresh?.();
  };

  // AC8 — 4 estados de la UI a partir de la carga del estado biométrico:
  //  • Cargando: aún no llegó la primera respuesta (validations === null) y no hubo error.
  //  • Error:    `error` con role="alert".
  //  • Lleno/Vacío: ya cargó → se pintan las tarjetas por parte (cada una resuelve su sub-estado:
  //    vacío = acción de iniciar; lleno = verificado/en proceso/rechazado).
  const initialLoading = instanceId != null && validations === null && error === null;

  // Paso Identidad: título + subtítulo en card propia; partes = acordeones separados.
  const pagePanel = Boolean(heading);

  // Feature 05 (rediseño) — la rejilla de partes es de dos columnas SOLO cuando hay dos partes
  // (traspaso: vendedor + comprador); con una sola (matrícula inicial) se queda en una columna para
  // no dejar una mitad de tarjeta vacía.
  const gridClass = partes.length > 1 ? 'lg:grid-cols-2' : '';

  const partesContent = initialLoading ? (
    <BiometricSkeleton partes={partes} gridClass={gridClass} />
  ) : (
    // Feature 05 (rediseño) — cada parte ahora trae DOS tarjetas (firma + biométrica, ver
    // `ParteBlock`), así que la rejilla exterior deja de repartir partes en columnas: cada bloque va
    // a ancho completo y apilado, con su propio encabezado de rol para no confundir vendedor y
    // comprador cuando hay dos (traspaso).
    // Paso Validación (prototipo AccordionRow + ValidacionCard): cuando !embedded, cada parte es
    // un acordeón desplegable separado con badge de estado en la cabecera.
    <div className="space-y-4">
      {partes.map((parte) => {
        const matches = (validations ?? []).filter((v) =>
          modalidad === 'traspaso'
            ? v.partyRole === parte
            : v.partyRole === null || v.partyRole === 'comprador',
        );
        const validation = matches.length > 0 ? matches[matches.length - 1] : null;
        const actor = actors?.find((a) => a.rol === parte) ?? null;
        const vaultCovered =
          firmaBaulServidor.includes(parte) || vaultCoveredPartes.includes(parte);
        const badge = parteBadge(validation, vaultCovered);

        const inner = (
          <ParteBlock
            parte={parte}
            instanceId={instanceId}
            provider={provider}
            validation={validation}
            actor={actor}
            historial={matches}
            vaultCovered={vaultCovered}
            motivoNoEnvio={motivoDeParte(motivosNoEnvio, parte)}
            onIrAActores={onIrAActores}
            onChanged={() => void handleRefresh()}
          />
        );

        return (
          <div
            key={parte}
            role="group"
            aria-label={`Biométrica ${PARTE_LABEL[parte]}`}
          >
            {!embedded ? (
              <WizardAccordion
                title={`Validación del ${PARTE_LABEL[parte]}`}
                defaultOpen
                badge={<StatusBadge label={badge.label} tone={badge.tone} />}
              >
                {inner}
              </WizardAccordion>
            ) : (
              inner
            )}
          </div>
        );
      })}
    </div>
  );

  if (pagePanel) {
    // Paso Identidad: card intro con título + subtítulo + acordeones de partes separados.
    // Sin botón "Actualizar" global (la actualización es automática) y sin banner azul
    // (el polling sigue activo; no hace falta explicarlo en el panel del paso).
    return (
      <div className="space-y-4">
        {(heading || headingSubtitle) && (
          <div className={WIZARD_CARD}>
            <WizardCardHeader title={heading ?? ''} subtitle={headingSubtitle} />
          </div>
        )}
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
        {partesContent}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {!hideIntro && (
        <div
          className={`${WIZARD_CARD}`}
          style={{ borderLeft: '3px solid #557EFF' }}
        >
          <p className="text-xs font-semibold" style={{ color: '#557EFF' }}>
            La validación se inicia automáticamente al continuar
          </p>
          <p className="mt-1 text-xs opacity-70">
            Aquí solo monitoreas el estado. Si la validación se encaminó correctamente, el cliente
            recibirá el enlace de captura por correo; el resultado se actualiza en tiempo real. Si
            algo falla, puedes reiniciarla desde cada tarjeta.
          </p>
        </div>
      )}

      {error && <InlineAlert tone="error">{error}</InlineAlert>}

      {partesContent}
    </div>
  );
}

/**
 * Estado de carga (AC8): placeholder accesible mientras llega la primera respuesta de
 * `getBiometricState`. Anuncia la carga a lectores de pantalla (role="status" + aria-live).
 */
function BiometricSkeleton({
  partes,
  gridClass = '',
}: {
  partes: BiometricParte[];
  gridClass?: string;
}) {
  return (
    // `aria-label` explícito y no solo el texto oculto: el rol `status` NO toma su nombre
    // accesible del contenido, así que sin él este esqueleto era indistinguible de cualquier otro
    // `role="status"` de la pantalla — y eso llegó a costar que se retirara un chip de estado que
    // el diseño pide, por un test que solo sabía contar roles.
    <div
      className={`grid grid-cols-1 gap-4 ${gridClass}`}
      role="status"
      aria-label="Cargando validaciones de identidad"
      aria-live="polite"
      aria-busy="true"
    >
      {partes.map((parte) => (
        <div key={parte} className={WIZARD_CARD} aria-hidden="true">
          <div className="mb-3 h-3 w-24 animate-pulse rounded bg-black/10 dark:bg-white/10" />
          <div className="h-12 w-full animate-pulse rounded-xl bg-black/5 dark:bg-white/5" />
        </div>
      ))}
    </div>
  );
}

/**
 * Bloque de UNA parte del trámite:
 *  - Cubierta por baúl → solo «Método de Firma» / firma electrónica (sin bitácora Kyverum).
 *  - Sin baúl → layout dual: identidad + biométrica (izq.) y método de firma + canvas (der.).
 */
function ParteBlock({
  parte,
  instanceId,
  provider,
  validation,
  actor,
  historial,
  vaultCovered,
  motivoNoEnvio,
  onIrAActores,
  onChanged,
}: {
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  validation: BiometricValidation | null;
  actor: ProcedureActor | null;
  historial: BiometricValidation[];
  vaultCovered: boolean;
  motivoNoEnvio: EnvioValidacionMotivo | null;
  onIrAActores?: (parte: BiometricParte) => void;
  onChanged: () => void;
}) {
  const estado = validation?.status;
  const mecanismoFirma = actor?.representanteLegal?.mecanismoFirma;
  const sigBadge = signatureBadge(vaultCovered, mecanismoFirma);
  const bioBadge = biometricStateBadge(validation, vaultCovered);
  const info = personaInfoFor(validation, actor);

  const sigDetalle = vaultCovered
    ? `${PARTE_LABEL[parte]} firmará con la firma electrónica precargada en el baúl.`
    : mecanismoFirma === 'identidad'
    ? `${PARTE_LABEL[parte]} firmará con el sello de la validación de identidad (biométrica) como mecanismo de firma.`
    : `${PARTE_LABEL[parte]} todavía no tiene un mecanismo de firma electrónica registrado.`;

  // Nombre en el canvas: con blur si la parte NO está validada/firmada.
  const isValidated =
    vaultCovered || (validation?.status === 'aprobado' && !validation?.expired);
  const sigNombre = info?.nombre ?? PARTE_LABEL[parte];

  if (vaultCovered) {
    return (
      <div className="rounded-xl border p-4" style={{ borderColor: '#DFE5ED' }}>
        <p className="mb-3 text-xs font-bold" style={{ color: '#1A2B4C' }}>
          Método de Firma
        </p>
        <div
          className="flex items-center justify-center rounded-xl border p-5 text-center"
          style={{ borderColor: '#DFE5ED', background: '#EEF5FF' }}
        >
          <div>
            <p
              className="text-[11px] font-medium uppercase tracking-wide"
              style={{ color: '#59677D' }}
            >
              Firma electrónica
            </p>
            <p
              className="mt-2 select-none text-2xl font-semibold italic"
              style={{ color: '#1A2B4C' }}
            >
              {sigNombre}
            </p>
            <div className="mt-2">
              <StatusBadge label={sigBadge.label} tone={sigBadge.tone} />
            </div>
            <p className="mt-2 text-xs opacity-70">{sigDetalle}</p>
          </div>
        </div>
        <div className="mt-3">
          <VaultCoveredView />
        </div>
      </div>
    );
  }

  const actionView =
    estado === 'aprobado' ? (
      <VerifiedView validation={validation!} />
    ) : estado === 'en_proceso' && validation?.captureUrl && !validation.expired ? (
      <KyverumPendingView validation={validation} instanceId={instanceId} onChanged={onChanged} />
    ) : estado === 'rechazado' || estado === 'expirado' || validation?.expired ? (
      <RejectedView
        validation={validation!}
        parte={parte}
        instanceId={instanceId}
        provider={provider}
        onChanged={onChanged}
      />
    ) : estado === 'error_envio' ? (
      <SendFailedView
        validation={validation!}
        parte={parte}
        instanceId={instanceId}
        provider={provider}
        onChanged={onChanged}
      />
    ) : estado === 'enviado' || estado === 'pendiente_envio' || estado === 'en_proceso' ? (
      <SentPendingView
        validation={validation!}
        parte={parte}
        instanceId={instanceId}
        provider={provider}
        onChanged={onChanged}
      />
    ) : (
      <StartAction parte={parte} instanceId={instanceId} provider={provider} onStarted={onChanged} />
    );

  const hashLine = signatureHashLabel(validation);

  return (
    <div className="grid grid-cols-1 items-stretch gap-4 lg:grid-cols-2">
      <div className="rounded-xl border p-4" style={{ borderColor: '#DFE5ED' }}>
        <p className="text-xs font-bold" style={{ color: '#1A2B4C' }}>
          Datos de Identificación
        </p>
        {info && (
          <>
            <p className="mt-2 text-xs font-semibold" style={{ color: '#1A2B4C' }}>
              {info.nombre} — {PARTE_LABEL[parte]}
            </p>
            <p className="mt-0.5 text-xs opacity-70">{info.documentoLine}</p>
          </>
        )}
        <div className="mt-4">
          <p
            className="text-[11px] font-medium uppercase tracking-wide"
            style={{ color: '#59677D' }}
          >
            Estado Biométrico
          </p>
          <div className="mt-2">
            <StatusBadge label={bioBadge.label} tone={bioBadge.tone} />
          </div>
        </div>
        {motivoNoEnvio && (
          <div className="mt-3">
            <MotivoNoEnvioAviso
              parte={parte}
              motivo={motivoNoEnvio}
              onIrAActores={onIrAActores}
            />
          </div>
        )}
        <div className="mt-3">{actionView}</div>
      </div>

      <div className="flex flex-col rounded-xl border p-4" style={{ borderColor: '#DFE5ED' }}>
        <p className="mb-3 text-xs font-bold" style={{ color: '#1A2B4C' }}>
          Método de Firma
        </p>
        <div
          className="flex flex-1 items-center justify-center rounded-xl border p-5 text-center"
          style={{ borderColor: '#DFE5ED', background: '#EEF5FF' }}
        >
          <div>
            <p
              className="text-[11px] font-medium uppercase tracking-wide"
              style={{ color: '#59677D' }}
            >
              Firma electrónica
            </p>
            <p
              className="mt-2 select-none text-2xl font-semibold italic"
              style={{ color: '#1A2B4C', filter: isValidated ? undefined : 'blur(4px)' }}
            >
              {sigNombre}
            </p>
            <div className="mt-2">
              <StatusBadge label={sigBadge.label} tone={sigBadge.tone} />
            </div>
            <p className="mt-2 text-xs opacity-70">{sigDetalle}</p>
            {hashLine !== null && (
              <p className="mt-2 text-xs opacity-70">Hash: {hashLine}</p>
            )}
          </div>
        </div>
        <HistorialValidaciones historial={historial} vigenteId={validation?.id ?? null} />
      </div>
    </div>
  );
}

/** Etiqueta de hash de firma: solo con dato real o "—" si aprobado sin hash; pendiente → null. */
function signatureHashLabel(validation: BiometricValidation | null): string | null {
  const hash = validation?.certificateHash?.trim();
  if (hash) {
    return hash;
  }
  if (validation?.status === 'aprobado' && !validation.expired) {
    return '—';
  }
  return null;
}

/**
 * Badge del recuadro de firma en la columna derecha de cada parte, en tres niveles según el dato
 * REAL disponible (nunca se fabrica): 1) cubierta por el baúl (`vaultCovered`); 2) mecanismo
 * elegido explícitamente por el gestor cuando el representante tiene baúl e identidad vigentes a
 * la vez (`actor.representanteLegal.mecanismoFirma === 'identidad'`, HU #11061); 3) sin ninguno
 * de los dos datos, la parte no tiene firma registrada.
 */
function signatureBadge(
  vaultCovered: boolean,
  mecanismoFirma: MecanismoFirma | undefined,
): { label: string; tone: StatusTone } {
  if (vaultCovered) {
    return { label: 'Firma electrónica activa', tone: 'success' };
  }
  if (mecanismoFirma === 'identidad') {
    return { label: 'Firmará con validación de identidad', tone: 'info' };
  }
  return { label: 'Sin firma registrada', tone: 'neutral' };
}

/**
 * Badge de "Estado Biométrico" en la columna izquierda (Datos de Identificación). Complementa al
 * `parteBadge` de la cabecera: usa el mismo conjunto de estados pero con etiquetas descriptivas
 * orientadas al panel de identidad (p. ej. "Biometría aprobada · Firma OK" en vez del score puro).
 */
function biometricStateBadge(
  validation: BiometricValidation | null,
  vaultCovered: boolean,
): { label: string; tone: StatusTone } {
  if (vaultCovered) {
    return { label: 'Biometría cubierta · Firma OK', tone: 'success' };
  }
  if (!validation) {
    return { label: 'Pendiente de validación', tone: 'warning' };
  }
  if (validation.expired) {
    return { label: ESTADO_LABEL.expirado, tone: ESTADO_TONE.expirado };
  }
  if (validation.status === 'aprobado') {
    return { label: 'Biometría aprobada · Firma OK', tone: 'success' };
  }
  return { label: ESTADO_LABEL[validation.status], tone: ESTADO_TONE[validation.status] };
}



/**
 * HU #11666 — aviso del motivo por el que NO se envió la validación de identidad de una parte.
 *
 * El gestor veía la tarjeta en "Sin iniciar" sin ninguna explicación: ni el propio código sabía
 * cuál de las omisiones había ocurrido (HU #11665). Aquí se pinta el motivo tipificado que el
 * backend calcula al vuelo, con dos tratamientos distintos:
 *
 *  • BLOQUEO (`informativo: false`): tono de alerta, se anuncia a lectores de pantalla y —cuando
 *    depende del gestor— ofrece la acción que lleva al paso de actores. `proveedor_no_envia` es la
 *    excepción: es una condición del ambiente y NO se le ofrece corrección, porque no la tiene.
 *  • INFORMACIÓN (`informativo: true`): tono neutro, sin sugerir corrección. No es un error —
 *    `cubierto_por_baul` y `representante_utilizable` explican una ausencia legítima.
 *
 * El significado nunca depende solo del color (WCAG 2.1 AA, 1.4.1): el título nombra la situación
 * y el icono es decorativo.
 */
function MotivoNoEnvioAviso({
  parte,
  motivo,
  onIrAActores,
}: {
  parte: BiometricParte;
  motivo: EnvioValidacionMotivo;
  onIrAActores?: (parte: BiometricParte) => void;
}) {
  const copy = presentarMotivoNoEnvio(motivo.codigo, PARTE_LABEL[parte]);
  // El flag del backend manda sobre el mapa local: si tipifica un motivo nuevo como informativo,
  // no debe pintarse como bloqueo por no estar todavía en el `switch` del copy.
  const bloqueo = !motivo.informativo && copy.naturaleza === 'bloqueo';
  const { color, background, border, Icon } = INLINE_ALERT_TONES[bloqueo ? 'error' : 'info'];
  const ofreceAccion = bloqueo && copy.accion === 'actores' && typeof onIrAActores === 'function';

  return (
    <div
      // `alert` + `aria-live="polite"`: el aviso aparece al cargar el paso, no interrumpe una
      // acción en curso, así que no debe cortar la lectura del gestor.
      role={bloqueo ? 'alert' : 'status'}
      aria-live="polite"
      className="mb-3 flex items-start gap-3 rounded-xl p-3"
      style={{ background, border: `1px solid ${border}` }}
    >
      <Icon className="mt-0.5 h-4 w-4 shrink-0" style={{ color }} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="text-xs font-semibold" style={{ color }}>
          {copy.titulo}
        </p>
        <p className="mt-0.5 text-xs leading-relaxed" style={{ color }}>
          {copy.detalle}
        </p>
        {ofreceAccion && (
          <button
            type="button"
            onClick={() => onIrAActores?.(parte)}
            className="mt-2 inline-flex items-center rounded-xl border px-3 py-1.5 text-xs font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ borderColor: color, color }}
          >
            {copy.accionLabel}
          </button>
        )}
      </div>
    </div>
  );
}

/**
 * CF-08 (Feature #11004, HU #11009) — "Historial de validaciones" de una parte: todas las filas que
 * `GET .../biometric` devuelve para esa parte, no solo la vigente/más reciente (que sigue siendo la
 * única con tarjeta de acción arriba). Cada ítem trae su propio tracking (bitácora) cuando es Kyverum
 * — mock no genera eventos de auditoría. Se omite por completo si la parte aún no tiene ninguna
 * validación (estado vacío, cubierto por `StartAction`).
 */
function HistorialValidaciones({
  historial,
  vigenteId,
}: {
  historial: BiometricValidation[];
  vigenteId: string | null;
}) {
  if (historial.length === 0) return null;

  // Con una sola validación no hay "historial" real que anunciar (es la misma que ya se ve en la
  // tarjeta de acción de arriba): se conserva únicamente el acceso a su tracking, igual que antes de
  // esta HU, sin el encabezado ni la fila de estado/fecha duplicada.
  if (historial.length === 1) {
    const [v] = historial;
    if (v.provider !== KYVERUM) return null;
    return (
      <TrazabilidadDisclosure>
        <IdentityValidationTrackingPanel validationId={v.id} embebido />
      </TrazabilidadDisclosure>
    );
  }

  return (
    <TrazabilidadDisclosure>
        <p className="text-xs font-semibold opacity-70">
          Historial de validaciones ({historial.length})
        </p>
        <ul className="mt-2 space-y-2">
          {historial.map((v) => (
            <li key={v.id} className="rounded-lg border p-2 text-xs">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-1.5">
                  <StatusBadge label={ESTADO_LABEL[v.status] ?? v.status} tone={ESTADO_TONE[v.status] ?? 'neutral'} />
                  {v.id === vigenteId && <StatusBadge label="Vigente" tone="info" />}
                  {v.score != null && <span className="opacity-70">{v.score}/100</span>}
                </div>
                <span className="opacity-70">
                  {formatFecha(v.validatedAt ?? v.expiresAt)}
                </span>
              </div>
              {/* Aquí SÍ conserva su propio desplegable: son varias validaciones en una lista, y
                  sin él se abrirían todas las bitácoras a la vez. El modo embebido es solo para el
                  caso de una sola validación, donde el acordeón de arriba ya hace de desplegable. */}
              {v.provider === KYVERUM && <IdentityValidationTrackingPanel validationId={v.id} />}
            </li>
          ))}
        </ul>
    </TrazabilidadDisclosure>
  );
}

/**
 * Desplegable «Ver trazabilidad de validación».
 *
 * Es un enlace de texto con su chevrón, no una tarjeta: así lo hace la referencia del diseño
 * (`MatriculaInicial` `Step4`, líneas 932-935) y así corresponde aquí, porque vive DENTRO de la
 * tarjeta de la parte. Antes usaba `WizardAccordion`, que dibuja su propia tarjeta blanca con
 * borde: quedaba una tarjeta dentro de otra, el mismo patrón que el guardián ya señaló como ajeno
 * al sistema en el avalúo comercial.
 */
function TrazabilidadDisclosure({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  return (
    <div className="mt-3">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-controls={panelId}
        className="flex items-center gap-1.5 rounded-lg text-xs font-semibold focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
        style={{ color: '#557EFF' }}
      >
        Ver trazabilidad de validación
        <ChevronDown
          className={`h-4 w-4 transition-transform ${open ? 'rotate-180' : ''}`}
          aria-hidden="true"
        />
      </button>
      {open && (
        <div id={panelId} className="mt-2">
          {children}
        </div>
      )}
    </div>
  );
}

/**
 * HU #10646 — estado "cubierto por el baúl": la parte es un actor jurídico (NIT) con firma electrónica
 * vigente en el baúl, así que su identidad queda satisfecha con esa firma y NO requiere biométrica. Se
 * presenta como "Firma electrónica (baúl)" — sin botones de iniciar/simular/reintentar validación.
 */
function VaultCoveredView() {
  return (
    <div
      className="flex items-center gap-3 rounded-xl p-3"
      style={{ background: 'rgba(85,126,255,0.10)', border: '1px solid rgba(85,126,255,0.35)' }}
      role="status"
      aria-live="polite"
    >
      <span
        className="flex h-9 w-9 items-center justify-center rounded-full shrink-0"
        style={{ background: '#557EFF', color: 'white' }}
        aria-hidden
      >
        <FileSignature className="h-5 w-5" />
      </span>
      <div className="space-y-0.5">
        <p className="text-xs font-bold" style={{ color: '#557EFF' }}>
          Firma electrónica (baúl)
        </p>
        <p className="text-xs opacity-70">
          Identidad cubierta por la firma electrónica del baúl. No requiere validación biométrica.
        </p>
      </div>
    </div>
  );
}

/** Tarjeta verde "Identidad verificada — {score}/100" con el nombre de la parte. */
function VerifiedView({ validation: v }: { validation: BiometricValidation }) {
  /*
   * Aprobada NO pinta bloque propio: el estado y el puntaje viven en el badge de la cabecera y el
   * nombre en el recuadro de la persona, justo encima. El bloque verde que había aquí los repetía
   * los dos —el nombre salía dos veces en la misma tarjeta— y en la referencia el estado se dice
   * una sola vez, en el badge.
   *
   * Se conserva la fecha, que no está en ningún otro sitio cuando la parte tiene una sola
   * validación: el historial solo se despliega a partir de la segunda.
   *
   * El certificado oficial (PDF) NO se descarga aquí: vive en el flujo de Generar/Re-generar FUR y
   * de Consolidar (FirmaFurStep), donde es uno de los documentos producidos con su propio botón.
   */
  const cuando = formatFecha(v.validatedAt);
  if (!cuando) return null;
  return (
    <p className="text-xs opacity-70">Validada el {cuando}.</p>
  );
}

/**
 * Validación Kyverum en curso: el enlace de captura ya se envió por correo al cliente. Se muestra
 * también aquí (link copiable + QR) para que el gestor pueda reenviarlo/mostrarlo si el correo no
 * llega. El estado se actualiza solo (polling) cuando llegue el webhook.
 */
function KyverumPendingView({
  validation: v,
  instanceId,
  onChanged,
}: {
  validation: BiometricValidation;
  instanceId: string | null;
  onChanged: () => void;
}) {
  const [copied, setCopied] = useState(false);
  const captureUrl = v.captureUrl!;

  const copy = async () => {
    try {
      await navigator.clipboard?.writeText(captureUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard no disponible: el gestor puede copiar el link manualmente */
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <RefreshCw className="h-3.5 w-3.5 animate-spin" style={{ color: '#557EFF' }} aria-hidden />
        <p className="text-xs font-semibold" style={{ color: '#557EFF' }}>
          Esperando validación de {v.name}
        </p>
      </div>

      {/* Un intento no pasó pero la validación SIGUE abierta (Kyverum permite reintentar): se informa el motivo
          real y que el cliente puede volver a intentar en su móvil, sin marcar la validación como rechazada. */}
      {v.ultimoIntentoMotivo && (
        <InlineAlert tone="warning" title={`Intento ${v.intentos} de ${v.maxIntentos} no pasó.`}>
          {v.ultimoIntentoMotivo}{' '}
          {v.maxIntentos - v.intentos > 0
            ? `Quedan ${v.maxIntentos - v.intentos} intento(s): el cliente puede reintentar en su móvil (aquí se actualiza al aprobar o al agotar los intentos).`
            : ''}
        </InlineAlert>
      )}

      <p className="text-xs opacity-70">
        Enviamos el enlace de captura al correo del cliente ({v.email}). También puedes compartirlo:
      </p>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="rounded-xl border bg-white p-2">
          <QRCodeSVG value={captureUrl} size={120} aria-label="Código QR del enlace de captura" />
        </div>
        <div className="min-w-0 flex-1 space-y-2">
          {/* AC2: CTA explícito para abrir la captura en una pestaña nueva (target=_blank, rel=noopener). */}
          <a
            href={captureUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="flex w-fit items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ background: WIZARD_CTA_GRADIENT }}
          >
            <ExternalLink className="h-3 w-3" aria-hidden />
            Abrir captura Kyverum
          </a>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => void copy()}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold border focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
              aria-label="Copiar enlace de captura"
            >
              <Copy className="h-3 w-3" aria-hidden />
              {copied ? 'Copiado' : 'Copiar enlace'}
            </button>
          </div>
          {/* El enlace literal queda como referencia (también se envió al correo del cliente). */}
          <a
            href={captureUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="block truncate text-xs underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ color: '#557EFF' }}
            title={captureUrl}
          >
            {captureUrl}
          </a>
        </div>
      </div>

      <ReconcileAction instanceId={instanceId} validationId={v.id} onReconciled={onChanged} />
    </div>
  );
}

/**
 * Acción "Actualizar estado": reconcilia la validación consultando su estado real a Kyverum (self-heal
 * si el webhook no llegó). El resultado normalmente llega solo por webhook en segundos, así que el botón
 * aparece únicamente si la validación sigue colgada tras ~15s, para no invitar a consultas innecesarias.
 * Si la consulta la resuelve (`updated`), notifica al wizard para recomponer el gate.
 */
function ReconcileAction({
  instanceId,
  validationId,
  onReconciled,
}: {
  instanceId: string | null;
  validationId: string;
  onReconciled: () => void;
}) {
  const readOnly = useWizardReadOnly();
  const [visible, setVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => setVisible(true), 15000);
    return () => clearTimeout(timer);
  }, []);

  if (readOnly || !visible) return null;

  const handleReconcile = async () => {
    if (!instanceId) return;
    setError(null);
    setInfo(null);
    setSubmitting(true);
    try {
      const res = await tramitesClient.reconcileBiometric(instanceId, validationId);
      if (res.updated) onReconciled();
      else setInfo('El proveedor aún no resuelve la validación. Intenta de nuevo en unos segundos.');
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudo actualizar el estado.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="space-y-1.5 border-t pt-3">
      <p className="text-xs opacity-70">
        ¿La captura ya se completó y sigue en espera? Consulta el estado directamente al proveedor.
      </p>
      <button
        type="button"
        onClick={() => void handleReconcile()}
        disabled={submitting || !instanceId}
        className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold border disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ borderColor: '#557EFF', color: '#557EFF' }}
        aria-label="Actualizar estado de la validación consultando al proveedor"
      >
        <RotateCcw className={`h-3 w-3 ${submitting ? 'animate-spin' : ''}`} aria-hidden />
        {submitting ? 'Consultando…' : 'Actualizar estado'}
      </button>
      {info && (
        <p className="text-xs opacity-70" role="status" aria-live="polite">
          {info}
        </p>
      )}
      {error && <InlineAlert tone="error">{error}</InlineAlert>}
    </div>
  );
}

/** Validación rechazada/expirada: explica el motivo y permite reenviar (nueva validación). */
function RejectedView({
  validation: v,
  parte,
  instanceId,
  provider,
  onChanged,
}: {
  validation: BiometricValidation;
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  onChanged: () => void;
}) {
  const expirado = v.status === 'expirado' || v.expired;
  return (
    <div className="space-y-3">
      <InlineAlert
        tone="error"
        title={
          expirado
            ? 'El enlace de validación expiró.'
            : `Validación no aprobada${v.score != null ? ` (${v.score}/100)` : ''}.`
        }
      >
        {/* AC5 — expiración: muestra cuándo venció el enlace. */}
        {expirado && v.expiresAt && <p>Venció el {formatFecha(v.expiresAt)}.</p>}
        {/* AC4 — rechazo con motivo: detalle sanitizado del proveedor (sin PII). */}
        {!expirado && v.rejectionReason && <p>Motivo: {v.rejectionReason}</p>}
      </InlineAlert>
      <StartAction
        parte={parte}
        instanceId={instanceId}
        provider={provider}
        onStarted={onChanged}
        // AC4: rechazo → "Reintentar validación". AC5: expiración → "Reiniciar validación".
        label={expirado ? 'Reiniciar validación' : 'Reintentar validación'}
      />
    </div>
  );
}

/**
 * Estado "en vuelo": la validación ya se disparó pero el proveedor todavía no la resuelve. Cubre tres
 * `BiometricEstado` que antes caían al `StartAction` genérico como si no hubiera pasado nada —
 * `enviado`, `pendiente_envio` y `en_proceso` SIN `captureUrl` (el caso CON enlace ya tiene su propia
 * vista, `KyverumPendingView`). El riesgo real que esto corrige: el gestor podía disparar una segunda
 * validación sobre una que ya estaba en curso, y si el envío falló nadie se lo decía — solo volvía a
 * ver el botón de arranque.
 *
 * El reenvío es una acción SECUNDARIA y explícita ("Reenviar validación", `variant="secondary"` sobre
 * `StartAction`) — nunca el mismo botón primario relleno del arranque, para que reenviar sin darse
 * cuenta de que ya había una en curso no vuelva a pasar. También ofrece `ReconcileAction` (consultar el
 * estado real al proveedor): es justo el caso en que el webhook puede no haber llegado.
 */
function SentPendingView({
  validation: v,
  parte,
  instanceId,
  provider,
  onChanged,
}: {
  validation: BiometricValidation;
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  onChanged: () => void;
}) {
  const SENT_STATE_TEXT: Partial<Record<BiometricEstado, string>> = {
    enviado: 'Ya se envió la validación',
    pendiente_envio: 'La validación está en cola de envío',
    en_proceso: 'La validación está en proceso con el proveedor',
  };
  const headline = SENT_STATE_TEXT[v.status] ?? 'La validación ya se disparó';

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <RefreshCw className="h-3.5 w-3.5" style={{ color: '#557EFF' }} aria-hidden />
        <p className="text-xs font-semibold" style={{ color: '#557EFF' }}>
          {headline} de {v.name}
        </p>
      </div>
      <p className="text-xs opacity-70">
        {v.createdAt
          ? `Se registró el ${formatFecha(v.createdAt)}. Aún no hay respuesta del proveedor: no hace falta iniciar otra.`
          : 'Aún no hay respuesta del proveedor: no hace falta iniciar otra.'}
      </p>
      <StartAction
        parte={parte}
        instanceId={instanceId}
        provider={provider}
        onStarted={onChanged}
        label="Reenviar validación"
        variant="secondary"
      />
      <ReconcileAction instanceId={instanceId} validationId={v.id} onReconciled={onChanged} />
    </div>
  );
}

/**
 * Estado `error_envio`: el envío al proveedor falló (cola provider-agnostic de reintentos de envío, no
 * el resultado de la biométrica). Es un FALLO, no una espera — se distingue de `SentPendingView` con
 * `InlineAlert tone="error"` y ofrece reintentar el envío como acción explícita.
 */
function SendFailedView({
  validation: v,
  parte,
  instanceId,
  provider,
  onChanged,
}: {
  validation: BiometricValidation;
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  onChanged: () => void;
}) {
  return (
    <div className="space-y-3">
      <InlineAlert tone="error" title="El envío de la validación falló.">
        No se pudo enviar la validación de identidad de {v.name} al proveedor. Reintenta el envío.
      </InlineAlert>
      <StartAction
        parte={parte}
        instanceId={instanceId}
        provider={provider}
        onStarted={onChanged}
        label="Reintentar envío"
      />
    </div>
  );
}

/**
 * Acción provider-aware para (re)iniciar la validación de una parte. Kyverum → validación real (toma
 * los datos del actor del trámite, solo se envía la parte). Mock → simula la validación (score 95).
 */
function StartAction({
  parte,
  instanceId,
  provider,
  onStarted,
  label,
  actorEmail,
  variant = 'primary',
}: {
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  onStarted: () => void;
  label?: string;
  /** HU #11267 AC3 — correo del destinatario mostrado en la confirmación. */
  actorEmail?: string | null;
  /**
   * `secondary` = botón con borde (no relleno), para los reenvíos desde un estado "ya enviado,
   * esperando" (`SentPendingView`): nunca debe verse igual al botón primario del arranque, o el
   * gestor puede confundir "reenviar de nuevo sobre una en curso" con "iniciar la primera".
   */
  variant?: 'primary' | 'secondary';
}) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [conflictMsg, setConflictMsg] = useState<string | null>(null);
  const readOnly = useWizardReadOnly();
  // B5 (guardián de diseño) — el `alertdialog` de confirmación no tenía trampa de foco, retorno de
  // foco ni Escape.
  const confirmDialogRef = useRef<HTMLDivElement>(null);
  useWizardFocusTrap(confirmDialogRef, {
    active: confirmOpen,
    onEscape: () => setConfirmOpen(false),
  });

  const isKyverum = provider === KYVERUM;
  const buttonLabel =
    label ?? (isKyverum ? 'Validar identidad' : 'Simular validación de identidad');

  const doStart = async () => {
    if (!instanceId) return;
    setError(null);
    setConflictMsg(null);
    setSubmitting(true);
    try {
      if (isKyverum) {
        await tramitesClient.iniciarBiometric(instanceId, { parte });
      } else {
        await tramitesClient.simulateBiometric(instanceId, { parte });
      }
      setConfirmOpen(false);
      onStarted();
    } catch (err) {
      const conflict = getIdentitySendConflict(err);
      if (conflict) {
        setConfirmOpen(false);
        const hasta = conflict.validUntil
          ? new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium' }).format(new Date(conflict.validUntil))
          : null;
        setConflictMsg(
          conflict.motivo === 'identidad_vigente'
            ? `Ya validada${hasta ? ` · vigente hasta el ${hasta}` : ''}. Se reutiliza la identidad existente.`
            : 'Ya hay una validación en curso para esta persona. No se envió un correo nuevo.',
        );
      } else {
        setError(
          err instanceof Error ? err.message : 'No se pudo iniciar la validación.',
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleStart = () => {
    if (!instanceId) return;
    // AC3 — confirmación solo en disparadores con UI (no en el auto del wizard).
    setConfirmOpen(true);
  };

  // En solo lectura no se inicia: solo se informa que la identidad quedó pendiente.
  if (readOnly) {
    return (
      <p className="text-xs opacity-70">Validación de identidad pendiente.</p>
    );
  }

  return (
    <div className="space-y-3">
      {!label && (
        <p className="text-xs font-medium opacity-70">
          Aún no se ha iniciado la validación de identidad de esta parte.
        </p>
      )}
      <p className="text-xs opacity-70">
        {isKyverum
          ? 'Inicia la validación: el cliente recibirá el enlace de captura por correo y aquí podrás compartir el enlace/QR.'
          : 'Mock de esta iteración: simula la validación biométrica de esta parte.'}
      </p>
      {conflictMsg && <InlineAlert tone="success">{conflictMsg}</InlineAlert>}
      {error && <InlineAlert tone="error">{error}</InlineAlert>}
      {!conflictMsg && (
        <button
          type="button"
          onClick={handleStart}
          disabled={submitting || !instanceId}
          className={
            variant === 'secondary'
              ? 'flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2'
              : 'rounded-xl px-3 py-2 text-xs font-semibold text-white disabled:opacity-50'
          }
          style={variant === 'secondary' ? { borderColor: '#557EFF', color: '#557EFF' } : { background: WIZARD_CTA_GRADIENT }}
        >
          {buttonLabel}
        </button>
      )}
      {confirmOpen && (
        <div
          ref={confirmDialogRef}
          tabIndex={-1}
          className="rounded-xl border p-3 text-xs outline-none focus:ring-0"
          style={{ borderColor: '#557EFF' }}
          role="alertdialog"
          aria-modal="true"
          aria-label="Confirmar envío de validación"
        >
          <p>
            Se enviará el enlace a{' '}
            <strong>{actorEmail?.trim() || 'el correo registrado de la parte'}</strong>. ¿Continuar?
          </p>
          <div className="mt-2 flex gap-2">
            <button type="button" className="rounded-lg border px-2 py-1" onClick={() => setConfirmOpen(false)} disabled={submitting}>
              Cancelar
            </button>
            <button
              type="button"
              className="rounded-lg px-2 py-1 font-semibold text-white disabled:opacity-50"
              style={{ background: WIZARD_CTA_GRADIENT }}
              onClick={() => void doStart()}
              disabled={submitting}
            >
              {submitting ? 'Enviando…' : 'Confirmar y enviar'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

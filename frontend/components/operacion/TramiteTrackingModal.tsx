'use client';

import { useEffect, useMemo, useState } from 'react';
import { Car, FileText, Users } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { SeccionCargando, SeccionError } from '@/components/operacion/detalle/primitivos';
import { tramitesClient } from '@/lib/api/tramites-client';
import { estadoChipStyle, estadoLabel, estadoLabelConOrigen } from '@/lib/tramites/estados';
import { tramiteLabel, vehiculo } from '@/lib/tramites/tramites-row-labels';
import type {
  InstanceSummary,
  ProcedureInstanceEvent,
  StatusHistoryItem,
} from '@/lib/api/types/procedure-runtime';

import { ZONA_COLOMBIA } from '@/lib/format/date';
/**

 * Panel del trámite: se abre desde el indicador de estado del listado (HU #12185).
 *
 * <p><b>Por qué empieza por una ficha y no por el historial.</b> Un historial suelto obliga a
 * recordar de qué trámite se está hablando: quien lo abre desde una tabla de cien filas tiene que
 * volver a la fila para saber dónde está parado. La ficha responde «qué trámite es» —vehículo,
 * partes, organismo— y el historial responde «por dónde va».</p>
 *
 * <p><b>La ficha no cuesta ninguna consulta.</b> Placa, VIN, marca, línea, comprador, vendedor con
 * sus documentos, organismo, compañía y paso ya viajan en cada fila del listado. Lo que se pide al
 * servidor es el historial de estados y, en paralelo, los eventos administrativos (reasignación de
 * gestor, reenvío de validación) que se mezclan en la misma línea de tiempo.</p>
 *
 * <p><b>No es el tracking del detalle.</b> El que vive dentro del trámite (`ExpedienteTimeline`) no
 * se toca: ese cuenta la cronología del expediente y se lee con el trámite abierto delante. Este
 * responde una pregunta distinta —«¿por dónde va ESTA fila?»— sin salir del listado, y por eso
 * necesita decir además quién movió cada paso y desde qué compañía.</p>
 */
export function TramiteTrackingModal({
  open,
  item,
  tenantId,
  onClose,
}: {
  open: boolean;
  /** La fila del listado: de aquí sale toda la ficha, sin pedir nada más. */
  item: InstanceSummary | null;
  tenantId?: string | null;
  onClose: () => void;
}) {
  const [history, setHistory] = useState<StatusHistoryItem[]>([]);
  const [events, setEvents] = useState<ProcedureInstanceEvent[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const instanceId = item?.id ?? null;

  useEffect(() => {
    if (!open || !instanceId) return;
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      // Historial de estados y eventos administrativos se piden en paralelo y se resuelven de forma
      // independiente: si `getInstance` falla, los eventos degradan a `[]` sin tocar el historial de
      // estados (el dato principal, con su propio manejo de error).
      const [historyResult, instanceResult] = await Promise.allSettled([
        tramitesClient.getStatusHistory(instanceId, 1, 50, tenantId ?? undefined),
        tramitesClient.getInstance(instanceId, tenantId ?? undefined),
      ]);
      if (cancelled) return;
      if (historyResult.status === 'fulfilled') {
        // Tope alto a propósito: un trámite tiene una decena de movimientos, no cien. Paginar el
        // historial dentro de un panel que se abre para ojearlo sería pedirle al gestor que
        // navegue dos veces para responder una sola pregunta.
        setHistory(historyResult.value?.items ?? []);
      } else {
        const reason = historyResult.reason;
        setError(reason instanceof Error ? reason.message : 'No se pudo cargar el historial del trámite.');
        setHistory([]);
      }
      setEvents(instanceResult.status === 'fulfilled' ? instanceResult.value?.events ?? [] : []);
      setLoading(false);
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [open, instanceId, tenantId, reloadKey]);

  // El backend ya ordena de más reciente a más antiguo; se reordena de forma defensiva para que el
  // panel se lea siempre igual aunque el caller entregue los datos desordenados. Los eventos
  // administrativos se mezclan en la misma línea, también por fecha descendente.
  const movimientos = useMemo(() => construirLinea(history, events), [history, events]);

  if (!item) return null;

  const chip = estadoChipStyle(item.estado);

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Trámite ${item.referenceNumber}`}
      size="xl"
      description={
        // La identidad del trámite va en la cabecera, no repartida por la ficha: quien abre el
        // panel desde una tabla larga necesita reconocer la fila de un vistazo, y el estado es
        // justo el dato por el que hizo clic.
        <span className="flex flex-wrap items-center gap-2">
          <span
            className="inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold"
            style={{ background: chip.bg, color: chip.color, borderColor: chip.border }}
          >
            {estadoLabelConOrigen(item.estado, item.rejectedFrom)}
          </span>
          <span className="text-xs text-[#162744]/60 dark:text-white/50">
            {[item.placa?.trim(), tramiteLabel(item)].filter(Boolean).join(' · ')}
          </span>
        </span>
      }
    >
      <div className="space-y-5">
        <FichaTramite item={item} />

        {/* El historial también en tarjeta: suelto sobre el fondo del modal, y con líneas cortas,
            quedaba flotando contra el borde izquierdo mientras la mitad derecha se veía vacía. */}
        <section className="rounded-xl border border-[#DFE5ED] bg-[#F8FAFC] p-3.5 dark:border-white/10 dark:bg-white/[0.03]">
          <h4 className="mb-3 text-[11px] font-bold uppercase tracking-wider text-[#557EFF]">
            Historial
          </h4>

          {loading ? <SeccionCargando etiqueta="Cargando historial" filas={3} /> : null}
          {!loading && error ? (
            <SeccionError
              mensaje={error}
              contexto="el historial del trámite"
              onReintentar={() => setReloadKey((k) => k + 1)}
            />
          ) : null}
          {!loading && !error ? <Historial movimientos={movimientos} /> : null}
        </section>
      </div>
    </Modal>
  );
}

/**
 * Un dato de la ficha: etiqueta ENCIMA del valor.
 *
 * <p>En línea no cabía. La ficha son tres columnas dentro de un modal, así que cada una dispone de
 * unos 190px; con la etiqueta al lado le quedaban 78 al valor y un VIN se partía en cuatro líneas,
 * el nombre de un organismo en siete. Apilada, el valor usa la columna entera.</p>
 *
 * <p>Si no hay valor, lo dice: nunca deja un hueco sin explicar.</p>
 */
function Dato({ etiqueta, valor, mono = false }: { etiqueta: string; valor?: string | null; mono?: boolean }) {
  const texto = valor?.trim();
  return (
    <div className="min-w-0">
      <dt className="text-[10px] uppercase leading-4 tracking-wide text-[#162744]/45 dark:text-white/40">
        {etiqueta}
      </dt>
      <dd
        className={`min-w-0 break-words text-xs leading-5 text-[#162744]/90 dark:text-white/80 ${
          mono && texto ? 'font-mono' : ''
        }`}
      >
        {texto || <span className="text-[#162744]/45 dark:text-white/40">Sin asignar</span>}
      </dd>
    </div>
  );
}

/**
 * Un grupo de la ficha, como TARJETA propia.
 *
 * <p>Antes los tres grupos compartían una sola caja y se repartían en columnas. Como cada grupo
 * tiene un número distinto de datos —el vehículo tres, las partes una o dos, el trámite cuatro—,
 * la caja quedaba con un agujero de aire debajo de la columna más corta y el borde inferior no
 * cerraba con nada. Con una tarjeta por grupo esa diferencia de alturas deja de leerse como un
 * hueco: `h-full` iguala los bordes y el aire sobrante queda DENTRO de una caja que lo justifica.</p>
 */
function Bloque({
  titulo,
  icono: Icono,
  children,
}: {
  titulo: string;
  icono: LucideIcon;
  children: React.ReactNode;
}) {
  return (
    <div className="flex h-full min-w-0 flex-col rounded-xl border border-[#DFE5ED] bg-[#F8FAFC] p-3.5 dark:border-white/10 dark:bg-white/[0.03]">
      <p className="mb-3 flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-[#557EFF]">
        <Icono className="h-3.5 w-3.5 shrink-0" aria-hidden />
        {titulo}
      </p>
      <dl className="space-y-2.5">{children}</dl>
    </div>
  );
}

/**
 * Resumen corto del trámite.
 *
 * <p><b>El rótulo de las partes lo manda la familia.</b> En un traspaso hay vendedor y comprador;
 * en una matrícula inicial no hay vendedor y el comprador ES el propietario. Llamarlo «comprador»
 * ahí sería un error de vocabulario, y dejar una fila «Vendedor: sin asignar» sugeriría un dato
 * pendiente de capturar cuando en ese trámite no existe.</p>
 */
function FichaTramite({ item }: { item: InstanceSummary }) {
  const esMatricula = item.modalidad === 'MATRICULAS';

  return (
    <section
      aria-label="Resumen del trámite"
      className="grid items-stretch gap-3 sm:grid-cols-2 lg:grid-cols-3"
    >
      <Bloque titulo="Vehículo" icono={Car}>
        <Dato etiqueta="Placa" valor={item.placa} mono />
        <Dato etiqueta="VIN" valor={item.vin} mono />
        <Dato etiqueta="Marca y línea" valor={vehiculo(item)} />
      </Bloque>

      <Bloque titulo="Partes" icono={Users}>
        {esMatricula ? (
          <Dato
            etiqueta="Propietario"
            valor={juntar(item.compradorNombre, item.compradorDocumento)}
          />
        ) : (
          <>
            <Dato etiqueta="Vendedor" valor={juntar(item.vendedorNombre, item.vendedorDocumento)} />
            <Dato etiqueta="Comprador" valor={juntar(item.compradorNombre, item.compradorDocumento)} />
          </>
        )}
      </Bloque>

      <Bloque titulo="Trámite" icono={FileText}>
        <Dato etiqueta="Tipo" valor={tramiteLabel(item)} />
        <Dato etiqueta="Organismo" valor={item.organismoTransito} />
        <Dato etiqueta="Gestiona" valor={item.companiaNombre} />
        <Dato
          etiqueta="Paso"
          valor={
            item.totalPasos > 0
              ? `${item.pasoActual} de ${item.totalPasos}${item.pasoNombre ? ` · ${item.pasoNombre}` : ''}`
              : null
          }
        />
      </Bloque>
    </section>
  );
}

/** «Nombre · Documento», o solo lo que haya. */
function juntar(nombre?: string | null, documento?: string | null): string | null {
  const partes = [nombre?.trim(), documento?.trim()].filter(Boolean);
  return partes.length > 0 ? partes.join(' · ') : null;
}

function fecha(iso: string): string {
  try {
    return new Date(iso).toLocaleString('es-CO', { timeZone: ZONA_COLOMBIA, dateStyle: 'medium', timeStyle: 'short' });
  } catch {
    return iso;
  }
}

/** Fila unificada de la línea de tiempo: un movimiento de estado o un evento administrativo. */
interface ItemLinea {
  key: string;
  /** `estado` es un movimiento de `statusHistory`; `evento` es un evento administrativo (Bug #12376). */
  kind: 'estado' | 'evento';
  fecha: string;
  titulo: string;
  quien: string | null;
  motivo: string | null;
}

const PARTE_LABEL: Record<string, string> = {
  vendedor: 'Vendedor',
  comprador: 'Comprador',
};

/**
 * Bug #12376, defectos 3/4 — mezcla `statusHistory` con los eventos administrativos
 * (reasignación de gestor, reenvío de validación) en una sola línea de tiempo, ordenada por fecha
 * descendente. Los eventos NUNCA reemplazan un movimiento de estado, solo se agregan.
 */
function construirLinea(history: StatusHistoryItem[], events: ProcedureInstanceEvent[]): ItemLinea[] {
  const deEstados: ItemLinea[] = history.map((m) => ({
    key: m.id,
    kind: 'estado',
    fecha: m.changedAt,
    titulo: hito(m),
    quien:
      m.changedByName || m.changedByCompania
        ? [m.changedByCompania, m.changedByName].filter(Boolean).join(' · ')
        : null,
    motivo: m.reason?.trim() || null,
  }));
  const deEventos: ItemLinea[] = events.map((e, i) => eventoALinea(e, i));
  return [...deEstados, ...deEventos].sort(
    (a, b) => new Date(b.fecha).getTime() - new Date(a.fecha).getTime(),
  );
}

/** Mismo lenguaje que `mapEventsToTimelineNodes` (`timeline-mappers.ts`), sin acoplarse a él. */
function eventoALinea(e: ProcedureInstanceEvent, index: number): ItemLinea {
  const quien = e.createdByName ? `Ejecutado por ${e.createdByName}` : 'Ejecutado por admin';
  const key = `evento-${e.tipo}-${e.createdAt}-${index}`;

  if (e.tipo === 'reasignar_gestor_admin') {
    return {
      key,
      kind: 'evento',
      fecha: e.createdAt,
      titulo: 'Reasignación de gestor',
      quien,
      motivo: `De ${e.previousAssignedToName || 'sin gestor asignado'} a ${e.newAssignedToName || '—'}`,
    };
  }

  if (e.tipo === 'revocatoria_solicitada') {
    // El ejecutor ES quien solicitó (a diferencia de los otros dos tipos, que hablan de un tercero).
    const solicitadoPor = e.createdByName ? `Solicitado por ${e.createdByName}` : 'Solicitado por administrador';
    return {
      key,
      kind: 'evento',
      fecha: e.createdAt,
      titulo: `Solicitud de revocatoria${e.revocationAttemptNumber ? ` · Intento ${e.revocationAttemptNumber}` : ''}`,
      quien: solicitadoPor,
      motivo: e.revocationReason?.trim() || 'Sin motivo adicional registrado',
    };
  }

  if (e.tipo === 'revocatoria_aprobada' || e.tipo === 'revocatoria_rechazada') {
    // HU #12577 — el ejecutor ES el OT que decidió, igual que en revocatoria_solicitada el ejecutor
    // es quien pidió (a diferencia de reasignar/reenvío, que hablan de un tercero).
    const aprobada = e.tipo === 'revocatoria_aprobada';
    const decididoPor = e.createdByName
      ? `${aprobada ? 'Aprobada' : 'Rechazada'} por ${e.createdByName}`
      : `${aprobada ? 'Aprobada' : 'Rechazada'} por el organismo de tránsito`;
    return {
      key,
      kind: 'evento',
      fecha: e.createdAt,
      titulo: `Revocatoria ${aprobada ? 'aprobada' : 'rechazada'}${e.revocationAttemptNumber ? ` · Intento ${e.revocationAttemptNumber}` : ''}`,
      quien: decididoPor,
      motivo: e.revocationDecisionReason?.trim() || 'Sin motivo adicional registrado',
    };
  }

  // reenvio_validacion_admin
  const parte = e.partyRole ? PARTE_LABEL[e.partyRole] ?? e.partyRole : null;
  // Correo en claro (a pedido del producto): el admin necesita ver la dirección exacta reenviada.
  const correo = e.correoDestino || '—';
  const reenvio = e.emailActualizado
    ? 'Reenviado a un correo distinto del registrado'
    : 'Reenviado al correo actual';
  return {
    key,
    kind: 'evento',
    fecha: e.createdAt,
    titulo: `Reenvío de validación${parte ? ` · ${parte}` : ''}`,
    quien,
    motivo: `Correo: ${correo} · ${reenvio}`,
  };
}

/**
 * Los movimientos, del más reciente al más antiguo.
 *
 * <p>Cada uno dice estado (o evento administrativo), quién y desde qué compañía. La compañía
 * importa porque un trámite lo abre una empresa y lo mueve, después, quien lo revisa: sin ella,
 * «Preparado · Laura Restrepo» no distingue si Laura es de la empresa dueña o del organismo.</p>
 */
function Historial({ movimientos }: { movimientos: ItemLinea[] }) {
  if (movimientos.length === 0) {
    return (
      <p className="text-xs text-[#162744]/60 dark:text-white/50">
        Todavía no hay movimientos registrados.
      </p>
    );
  }

  // El vigente es el movimiento de ESTADO más reciente, no necesariamente el primero de la lista:
  // un evento administrativo (p. ej. una reasignación de gestor) puede ser más reciente que el
  // último cambio de estado, pero no representa el estado actual del trámite.
  const idxVigente = movimientos.findIndex((m) => m.kind === 'estado');

  return (
    <ol
      aria-label="Historial de estados del trámite"
      className="relative space-y-3 border-l pl-4"
      style={{ borderColor: '#DFE5ED' }}
    >
      {movimientos.map((m, i) => (
        <li key={m.key} className="relative">
          {/* El movimiento de estado vigente en verde de marca, los demás en azul. No se usa el
              color del chip de estado (siete tonos) — aquí solo hace falta distinguir «dónde
              está» de «por dónde pasó» (o qué se hizo administrativamente). */}
          <span
            className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full"
            style={{ background: i === idxVigente ? '#8CC63F' : '#557EFF' }}
            aria-hidden="true"
          />
          <p className="text-xs font-semibold text-[#162744] dark:text-white">{m.titulo}</p>
          {m.quien ? (
            <p className="mt-0.5 text-xs text-[#162744]/70 dark:text-white/60">{m.quien}</p>
          ) : null}
          {/* El motivo va DEBAJO y en tono secundario, no dentro del titular.
              Muchos motivos los escribe el propio sistema al radicar («Radicación: entregado;
              placa seleccionada/RUNT y paso gestor omitido (sub-estado terminado)») y son largos
              y técnicos: en negrita, junto al estado, se comían dos renglones de titular y
              tapaban lo único que el gestor busca en esta lista, que es POR DÓNDE VA. */}
          {m.motivo ? (
            <p className="mt-0.5 text-[11px] leading-4 text-[#162744]/55 dark:text-white/45">
              {m.motivo}
            </p>
          ) : null}
          <p className="mt-1 font-mono text-[11px] text-[#162744]/50 dark:text-white/40">
            {fecha(m.fecha)}
          </p>
        </li>
      ))}
    </ol>
  );
}

/** «Rechazado desde Entregado». El motivo se pinta aparte, en su propia línea. */
function hito(m: StatusHistoryItem): string {
  const to = estadoLabel(m.toStatus);
  const from = m.fromStatus ? estadoLabel(m.fromStatus) : null;
  return `${to}${from ? ` desde ${from}` : ''}`;
}

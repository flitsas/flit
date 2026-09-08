'use client';

import { useEffect, useMemo, useState } from 'react';
import { Modal } from '@/components/atom/Modal';
import { SeccionCargando, SeccionError } from '@/components/operacion/detalle/primitivos';
import { tramitesClient } from '@/lib/api/tramites-client';
import { estadoLabel } from '@/lib/tramites/estados';
import { tramiteLabel, vehiculo } from '@/lib/tramites/tramites-row-labels';
import type { InstanceSummary, StatusHistoryItem } from '@/lib/api/types/procedure-runtime';

/**
 * Panel del trámite: se abre desde el indicador de estado del listado (HU #12185).
 *
 * <p><b>Por qué empieza por una ficha y no por el historial.</b> Un historial suelto obliga a
 * recordar de qué trámite se está hablando: quien lo abre desde una tabla de cien filas tiene que
 * volver a la fila para saber dónde está parado. La ficha responde «qué trámite es» —vehículo,
 * partes, organismo— y el historial responde «por dónde va».</p>
 *
 * <p><b>La ficha no cuesta ninguna consulta.</b> Placa, VIN, marca, línea, comprador, vendedor con
 * sus documentos, organismo, compañía y paso ya viajan en cada fila del listado. Lo único que se
 * pide al servidor es el historial.</p>
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
      try {
        // Tope alto a propósito: un trámite tiene una decena de movimientos, no cien. Paginar el
        // historial dentro de un panel que se abre para ojearlo sería pedirle al gestor que
        // navegue dos veces para responder una sola pregunta.
        const page = await tramitesClient.getStatusHistory(instanceId, 1, 50, tenantId ?? undefined);
        if (!cancelled) setHistory(page?.items ?? []);
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'No se pudo cargar el historial del trámite.');
          setHistory([]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [open, instanceId, tenantId, reloadKey]);

  // El backend ya ordena de más reciente a más antiguo; se reordena de forma defensiva para que el
  // panel se lea siempre igual aunque el caller entregue los datos desordenados.
  const movimientos = useMemo(
    () => [...history].sort((a, b) => new Date(b.changedAt).getTime() - new Date(a.changedAt).getTime()),
    [history],
  );

  if (!item) return null;

  return (
    <Modal open={open} onClose={onClose} title={`Trámite ${item.referenceNumber}`} size="lg">
      <FichaTramite item={item} />

      <h4 className="mb-2 mt-5 text-sm font-bold text-[#162744] dark:text-white">Historial</h4>

      {loading ? <SeccionCargando etiqueta="Cargando historial" filas={3} /> : null}
      {!loading && error ? (
        <SeccionError
          mensaje={error}
          contexto="el historial del trámite"
          onReintentar={() => setReloadKey((k) => k + 1)}
        />
      ) : null}
      {!loading && !error ? <Historial movimientos={movimientos} /> : null}
    </Modal>
  );
}

/** Etiqueta de un dato de la ficha. Si no hay valor, lo dice: nunca deja un hueco sin explicar. */
function Dato({ etiqueta, valor, mono = false }: { etiqueta: string; valor?: string | null; mono?: boolean }) {
  const texto = valor?.trim();
  return (
    <div className="flex min-w-0 gap-2">
      <dt className="w-28 shrink-0 text-[11px] leading-5 text-[#162744]/55 dark:text-white/50">
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

function Bloque({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-[#162744]/45 dark:text-white/40">
        {titulo}
      </p>
      <dl className="space-y-0.5">{children}</dl>
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
      className="grid gap-4 rounded-2xl border border-[#DFE5ED] bg-[#F8FAFC] p-4 sm:grid-cols-3 dark:border-white/10 dark:bg-white/[0.03]"
    >
      <Bloque titulo="Vehículo">
        <Dato etiqueta="Placa" valor={item.placa} mono />
        <Dato etiqueta="VIN" valor={item.vin} mono />
        <Dato etiqueta="Marca y línea" valor={vehiculo(item)} />
      </Bloque>

      <Bloque titulo="Partes">
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

      <Bloque titulo="Trámite">
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
    return new Date(iso).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' });
  } catch {
    return iso;
  }
}

/**
 * Los movimientos, del más reciente al más antiguo.
 *
 * <p>Cada uno dice estado, quién y desde qué compañía. La compañía importa porque un trámite lo
 * abre una empresa y lo mueve, después, quien lo revisa: sin ella, «Preparado · Laura Restrepo» no
 * distingue si Laura es de la empresa dueña o del organismo.</p>
 */
function Historial({ movimientos }: { movimientos: StatusHistoryItem[] }) {
  if (movimientos.length === 0) {
    return (
      <p className="text-xs text-[#162744]/60 dark:text-white/50">
        Todavía no hay movimientos registrados.
      </p>
    );
  }

  return (
    <ol
      aria-label="Historial de estados del trámite"
      className="relative space-y-3 border-l pl-4"
      style={{ borderColor: '#DFE5ED' }}
    >
      {movimientos.map((m, i) => (
        <li key={m.id} className="relative">
          {/* El movimiento vigente en verde de marca, los anteriores en azul: la lista está en
              orden inverso, así que el vigente es el PRIMERO. No se usa el color del chip de
              estado (siete tonos) — aquí solo hace falta distinguir «dónde está» de «por dónde
              pasó». */}
          <span
            className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full"
            style={{ background: i === 0 ? '#8CC63F' : '#557EFF' }}
            aria-hidden="true"
          />
          <p className="text-xs font-semibold text-[#162744] dark:text-white">
            {hito(m)}
          </p>
          {m.changedByName || m.changedByCompania ? (
            <p className="mt-0.5 text-xs text-[#162744]/70 dark:text-white/60">
              {[m.changedByCompania, m.changedByName].filter(Boolean).join(' · ')}
            </p>
          ) : null}
          <p className="mt-0.5 font-mono text-[11px] text-[#162744]/50 dark:text-white/40">
            {fecha(m.changedAt)}
          </p>
        </li>
      ))}
    </ol>
  );
}

/** «Rechazado desde Entregado (motivo)». `fromStatus` y `reason` solo si el backend los trae. */
function hito(m: StatusHistoryItem): string {
  const to = estadoLabel(m.toStatus);
  const from = m.fromStatus ? estadoLabel(m.fromStatus) : null;
  const motivo = m.reason?.trim();
  return `${to}${from ? ` desde ${from}` : ''}${motivo ? ` (${motivo})` : ''}`;
}

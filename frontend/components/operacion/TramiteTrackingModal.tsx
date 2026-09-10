'use client';

import { useEffect, useMemo, useState } from 'react';
import { Car, FileText, Users } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { SeccionCargando, SeccionError } from '@/components/operacion/detalle/primitivos';
import { tramitesClient } from '@/lib/api/tramites-client';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
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
            {estadoLabel(item.estado)}
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
          {/* El motivo va DEBAJO y en tono secundario, no dentro del titular.
              Muchos motivos los escribe el propio sistema al radicar («Radicación: entregado;
              placa seleccionada/RUNT y paso gestor omitido (sub-estado terminado)») y son largos
              y técnicos: en negrita, junto al estado, se comían dos renglones de titular y
              tapaban lo único que el gestor busca en esta lista, que es POR DÓNDE VA. */}
          {m.reason?.trim() ? (
            <p className="mt-0.5 text-[11px] leading-4 text-[#162744]/55 dark:text-white/45">
              {m.reason.trim()}
            </p>
          ) : null}
          <p className="mt-1 font-mono text-[11px] text-[#162744]/50 dark:text-white/40">
            {fecha(m.changedAt)}
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

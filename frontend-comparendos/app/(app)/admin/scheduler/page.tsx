"use client";

import { useState } from "react";
import { History } from "lucide-react";
import { DataTable, InlineAlert, Modal } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PRIMARIO_CLS, controlCls, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { TIPO_COMUNICACION } from "@/lib/etiquetas";
import { formatoFechaHora } from "@/lib/format/date";
import { cambiosDeAuditoria } from "@/lib/parametrizador";
import type { AuditoriaConfiguracion } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

interface Horario {
  tipo: string;
  horaEnvio: string;
  activo: boolean;
}

/** Una fila editable: hora (HH:mm, hora de Colombia) y si el scheduler corre para ese aviso. */
function FilaHorario({ h, onGuardado }: { h: Horario; onGuardado: () => void }) {
  const toast = useToast();
  const [hora, setHora] = useState(h.horaEnvio);
  const [activo, setActivo] = useState(h.activo);
  const [ocupado, setOcupado] = useState(false);
  const cambiado = hora !== h.horaEnvio || activo !== h.activo;

  const guardar = async () => {
    setOcupado(true);
    try {
      await apiFetch(`/admin/scheduler-avisos/${h.tipo}`, { method: "PUT", body: { horaEnvio: hora, activo } });
      toast.show(`Horario del ${TIPO_COMUNICACION[h.tipo] ?? h.tipo} guardado.`, "success");
      onGuardado();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo guardar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-4">
      <input type="time" className={`${INPUT_CLS} w-32`} value={hora} onChange={(e) => setHora(e.target.value)} aria-label={`Hora de envío del ${h.tipo}`} />
      <Interruptor activo={activo} onCambio={setActivo} etiqueta={activo ? "Activo" : "Pausado"} />
      <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!cambiado || ocupado || !hora} onClick={guardar}>
        {ocupado ? "Guardando…" : "Guardar"}
      </button>
    </div>
  );
}

/** Historial de cambios del scheduler (§7.23): quién cambió la hora o lo pausó, cuándo y de qué a qué. */
function Historial({ onCerrar }: { onCerrar: () => void }) {
  const lista = useCarga("auditoria:scheduler_aviso", (signal) =>
    apiFetch<{ items: AuditoriaConfiguracion[] }>("/admin/auditoria", { signal, query: { entidad: "scheduler_aviso", global: true, tamano: 100 } }),
  );
  const items = lista.datos?.items ?? [];
  return (
    <Modal open onClose={onCerrar} title="Historial de cambios del scheduler" size="lg">
      {lista.estado === "loading" && <p className="text-sm opacity-60">Cargando…</p>}
      {lista.estado === "error" && <InlineAlert tone="error">{lista.error}</InlineAlert>}
      {lista.estado === "ready" && items.length === 0 && <p className="text-sm opacity-70">Sin cambios registrados.</p>}
      <ol className="flex flex-col gap-3">
        {items.map((a) => (
          <li key={a.id} className="border-l-2 border-[#557EFF] pl-3 text-xs">
            <p className="font-semibold">{TIPO_COMUNICACION[a.entidadId] ?? a.entidadId}</p>
            <p className="opacity-70">
              {formatoFechaHora(a.registradoEn)} · {a.usuarioNombre}
            </p>
            <ul className="mt-1">
              {cambiosDeAuditoria(a.cambios).map((c) => (
                <li key={c.campo}>
                  <span className="font-semibold">{c.campo}:</span> {c.anterior} → {c.nuevo}
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ol>
    </Modal>
  );
}

function Scheduler() {
  const horarios = useCarga("scheduler-avisos", (signal) => apiFetch<Horario[]>("/admin/scheduler-avisos", { signal }));
  const [historial, setHistorial] = useState(false);
  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <Encabezado
          titulo="Horario de avisos"
          descripcion="A qué hora (de Colombia) sale cada aviso automático al conductor y si está activo. Pausar un aviso no afecta los envíos manuales; el aviso 4 sale con el cambio de contraventor."
        />
        <button type="button" className={controlCls(false)} onClick={() => setHistorial(true)}>
          <History className="h-3.5 w-3.5" aria-hidden />
          Historial de cambios
        </button>
      </div>
      <DataTable
        ariaLabel="Horarios de avisos"
        rows={horarios.datos ?? []}
        getRowKey={(h) => `${h.tipo}:${h.horaEnvio}:${h.activo}`}
        status={horarios.estado}
        errorMessage={horarios.error ?? undefined}
        onRetry={horarios.recargar}
        emptyMessage="No hay horarios configurados."
        columns={[
          {
            key: "tipo",
            header: "Aviso",
            render: (h) => <span className="font-semibold">{TIPO_COMUNICACION[h.tipo] ?? h.tipo}</span>,
          },
          { key: "horario", header: "Hora de envío y estado", render: (h) => <FilaHorario h={h} onGuardado={horarios.recargar} /> },
        ]}
      />
      {historial && <Historial onCerrar={() => setHistorial(false)} />}
    </section>
  );
}

/** Scheduler de avisos al conductor (HU #13485; §3.11). Solo SuperAdmin. */
export default function SchedulerAvisos() {
  return (
    <Requiere superAdmin>
      <Scheduler />
    </Requiere>
  );
}

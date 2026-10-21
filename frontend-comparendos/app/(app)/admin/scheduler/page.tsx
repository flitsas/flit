"use client";

import { useState } from "react";
import { History } from "lucide-react";
import { DataTable } from "@flit/ui";
import { HistorialAuditoria } from "@/components/HistorialAuditoria";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PRIMARIO_CLS, controlCls, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { TIPO_COMUNICACION } from "@/lib/etiquetas";
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
      {historial && (
        <HistorialAuditoria
          entidad="scheduler_aviso"
          titulo="Historial de cambios del scheduler"
          nombre={(id) => TIPO_COMUNICACION[id] ?? id}
          onCerrar={() => setHistorial(false)}
        />
      )}
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

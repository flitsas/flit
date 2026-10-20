"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { DataTable, InlineAlert, StatusBadge, activeTone } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { partirCorreos } from "@/lib/correos";
import { TIPO_ALERTA } from "@/lib/etiquetas";
import type { ConfigAlertas, DestinatarioAlerta } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

function Configuracion({ inicial, onGuardado }: { inicial: ConfigAlertas; onGuardado: () => void }) {
  const toast = useToast();
  const [c, setC] = useState(inicial);
  const [internos, setInternos] = useState((inicial.correosInternosReportes ?? []).join(", "));
  const [ocupado, setOcupado] = useState(false);
  const listaInternos = partirCorreos(internos);

  const guardar = async () => {
    if (listaInternos.invalidos.length > 0) {
      toast.show(`Correo inválido en la lista interna: ${listaInternos.invalidos.join(", ")}`, "error");
      return;
    }
    setOcupado(true);
    try {
      await apiFetch("/admin/alertas", { method: "PUT", body: { ...c, correosInternosReportes: listaInternos.validos } });
      toast.show("Configuración guardada.", "success");
      onGuardado();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo guardar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <div className="grid grid-cols-1 gap-3 rounded-2xl border border-[#DFE5ED] p-4 sm:grid-cols-2 dark:border-white/10">
      <label className="flex items-center gap-2 text-sm font-semibold sm:col-span-2">
        <input type="checkbox" checked={c.alertasHabilitadas} onChange={(e) => setC({ ...c, alertasHabilitadas: e.target.checked })} />
        Enviar alertas (interruptor general)
      </label>
      <label className={ETIQUETA_CLS}>
        Umbral de fallos por ciclo
        <input type="number" min={0} className={INPUT_CLS} value={c.umbralFallosCiclo} onChange={(e) => setC({ ...c, umbralFallosCiclo: Number(e.target.value) })} />
        <span className="font-normal opacity-70">0 = un correo por cada clave que falle. Un número N = un solo correo al final del ciclo si fallan más de N.</span>
      </label>
      <label className={ETIQUETA_CLS}>
        Nombre del remitente
        <input className={INPUT_CLS} value={c.remitenteNombre} onChange={(e) => setC({ ...c, remitenteNombre: e.target.value })} />
      </label>
      <label className={`${ETIQUETA_CLS} sm:col-span-2`}>
        Asunto
        <input className={INPUT_CLS} value={c.plantillaAsunto} onChange={(e) => setC({ ...c, plantillaAsunto: e.target.value })} />
        <span className="font-normal opacity-70">Puedes usar {"{FUENTE}"} y {"{TIMESTAMP}"}.</span>
      </label>
      <label className={`${ETIQUETA_CLS} sm:col-span-2`}>
        Correos internos para reportes
        <input className={INPUT_CLS} value={internos} placeholder="cuentas@flit.com.co, gerencia@flit.com.co" onChange={(e) => setInternos(e.target.value)} />
        <span className="font-normal opacity-70">
          Reciben en copia oculta los cierres semanal y mensual de todas las empresas con reportes. Separa con coma.
          {listaInternos.invalidos.length > 0 && <b className="text-[#B42318]"> Inválidos: {listaInternos.invalidos.join(", ")}</b>}
        </span>
      </label>
      <div className="sm:col-span-2">
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={guardar}>
          {ocupado ? "Guardando…" : "Guardar"}
        </button>
      </div>
    </div>
  );
}

/** Alertas por fallo de integración (HU #13463): interruptor, umbral, asunto y destinatarios. Solo SuperAdmin. */
export default function Alertas() {
  const toast = useToast();
  const config = useCarga("alertas", (signal) => apiFetch<ConfigAlertas>("/admin/alertas", { signal }));
  const destinatarios = useCarga("alertas-dest", (signal) => apiFetch<DestinatarioAlerta[]>("/admin/alertas/destinatarios", { signal }));
  const [correo, setCorreo] = useState("");
  const [nombre, setNombre] = useState("");
  const [tipo, setTipo] = useState("Todas");
  const [ocupado, setOcupado] = useState(false);

  const agregar = async () => {
    setOcupado(true);
    try {
      await apiFetch("/admin/alertas/destinatarios", { method: "POST", body: { correo, nombre, tipoAlerta: tipo } });
      setCorreo("");
      setNombre("");
      destinatarios.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo agregar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  const cambiar = async (d: DestinatarioAlerta, cambio: Partial<{ tipoAlerta: string; activo: boolean }>) => {
    try {
      await apiFetch(`/admin/alertas/destinatarios/${d.id}`, { method: "PATCH", body: cambio });
      destinatarios.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cambiar.", "error");
    }
  };

  return (
    <Requiere superAdmin>
      <section className="flex flex-col gap-4">
        <Encabezado titulo="Alertas de integración" descripcion="Correos cuando SIMIT o la fuente municipal fallan. Los datos de las empresas nunca se tocan por un fallo." />
        {config.estado === "error" && <InlineAlert tone="error">{config.error}</InlineAlert>}
        {config.datos && <Configuracion key={JSON.stringify(config.datos)} inicial={config.datos} onGuardado={config.recargar} />}

        <h2 className="text-lg font-semibold">Destinatarios</h2>
        <div className="flex flex-wrap items-end gap-2">
          <label className={ETIQUETA_CLS}>
            Correo
            <input className={`${INPUT_CLS} w-64`} value={correo} onChange={(e) => setCorreo(e.target.value)} />
          </label>
          <label className={ETIQUETA_CLS}>
            Nombre
            <input className={`${INPUT_CLS} w-48`} value={nombre} onChange={(e) => setNombre(e.target.value)} />
          </label>
          <label className={ETIQUETA_CLS}>
            Recibe
            <select className={`${INPUT_CLS} w-44`} value={tipo} onChange={(e) => setTipo(e.target.value)}>
              {Object.entries(TIPO_ALERTA).map(([v, t]) => (
                <option key={v} value={v}>
                  {t}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !correo.trim()} onClick={agregar}>
            <Plus className="h-3.5 w-3.5" aria-hidden />
            Agregar
          </button>
        </div>
        <DataTable
          ariaLabel="Destinatarios de alertas"
          rows={destinatarios.datos ?? []}
          getRowKey={(d) => d.id}
          status={destinatarios.estado}
          errorMessage={destinatarios.error ?? undefined}
          onRetry={destinatarios.recargar}
          emptyMessage="Nadie recibe alertas todavía."
          columns={[
            { key: "nombre", header: "Nombre", render: (d) => d.nombre },
            { key: "correo", header: "Correo", render: (d) => d.correo },
            {
              key: "tipo",
              header: "Recibe",
              render: (d) => (
                <select className={`${INPUT_CLS} w-44`} value={d.tipoAlerta} onChange={(e) => cambiar(d, { tipoAlerta: e.target.value })} aria-label={`Qué recibe ${d.correo}`}>
                  {Object.entries(TIPO_ALERTA).map(([v, t]) => (
                    <option key={v} value={v}>
                      {t}
                    </option>
                  ))}
                </select>
              ),
            },
            { key: "estado", header: "Estado", render: (d) => <StatusBadge label={d.activo ? "Activo" : "Inactivo"} tone={activeTone(d.activo)} /> },
            {
              key: "accion",
              header: "",
              align: "right",
              render: (d) => (
                <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => cambiar(d, { activo: !d.activo })}>
                  {d.activo ? "Desactivar" : "Activar"}
                </button>
              ),
            },
          ]}
        />
      </section>
    </Requiere>
  );
}

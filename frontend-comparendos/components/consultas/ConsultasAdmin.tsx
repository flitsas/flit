"use client";

import { useState } from "react";
import { Pause, Play, X } from "lucide-react";
import { InlineAlert, Modal, StatusBadge } from "@flit/ui";
import { useToast } from "@/components/Toast";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import {
  agregarHora,
  cuerpoCanal,
  erroresCanal,
  ESTADO_CONSULTAS,
  formDeCanal,
  horaColombia,
  INTERVALO_MINIMO,
  proveedoresDelCanal,
  RESULTADO_CICLO,
  type CanalConsulta,
  type ConfiguracionConsultasVista,
  type FormCanal,
  type ProveedorConsulta,
} from "@/lib/consultas";
import { formatoFechaHora } from "@/lib/format/date";
import { useCarga } from "@/lib/useCarga";

function Horas({ horas, onCambio }: { horas: string[]; onCambio: (h: string[]) => void }) {
  const [nueva, setNueva] = useState("");
  const [error, setError] = useState<string | null>(null);
  const agregar = () => {
    const r = agregarHora(horas, nueva);
    if (!r) {
      setError("Escribe la hora como HH:mm (por ejemplo 07:00).");
      return;
    }
    onCambio(r);
    setNueva("");
    setError(null);
  };
  return (
    <div className="flex flex-col gap-1">
      <div className="flex flex-wrap items-center gap-1.5">
        {horas.map((h) => (
          <span key={h} className="inline-flex items-center gap-1 rounded-full bg-[#E7EEFF] px-2 py-0.5 text-xs font-semibold text-[#3B4FD6] dark:bg-white/10 dark:text-white">
            {h}
            <button type="button" aria-label={`Quitar ${h}`} onClick={() => onCambio(horas.filter((x) => x !== h))}>
              <X className="h-3 w-3" />
            </button>
          </span>
        ))}
        <input
          className={`${INPUT_CLS} w-24`}
          value={nueva}
          placeholder="07:00"
          aria-label="Nueva hora"
          onChange={(e) => setNueva(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              agregar();
            }
          }}
        />
        <button type="button" className={controlCls(false)} onClick={agregar}>
          Agregar
        </button>
      </div>
      {error && <span className="text-[11px] text-[#B42318]">{error}</span>}
    </div>
  );
}

function Canal({ empresaId, canal, proveedores, onGuardado }: { empresaId: string; canal: CanalConsulta; proveedores: ProveedorConsulta[]; onGuardado: () => void }) {
  const toast = useToast();
  const base = formDeCanal(canal);
  const clave = JSON.stringify(base);
  const [form, setForm] = useState<{ base: string; valores: FormCanal } | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const f = form && form.base === clave ? form.valores : base;
  const cambiar = (parcial: Partial<FormCanal>) => setForm({ base: clave, valores: { ...f, ...parcial } });
  const opciones = proveedoresDelCanal(proveedores, canal.canal);
  const errores = erroresCanal(f, proveedores, canal.canal);
  const sucio = JSON.stringify(f) !== clave;

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/admin/empresas/${empresaId}/consultas/canales/${canal.canal}`, { method: "PUT", body: cuerpoCanal(f) });
      setForm(null);
      toast.show(`${canal.etiqueta}: guardado.`, "success");
      onGuardado();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar el canal.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <fieldset className="flex flex-col gap-3 border-t border-[#E2E8F0] pt-4 first:border-t-0 first:pt-0 dark:border-white/10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-0.5">
          <legend className="text-sm font-semibold">{canal.etiqueta}</legend>
          <span className="text-[11px] opacity-70">
            Última: {canal.ultimaConsulta ? formatoFechaHora(canal.ultimaConsulta) : "nunca"}
            {canal.ultimoResultado && ` (${RESULTADO_CICLO[canal.ultimoResultado] ?? canal.ultimoResultado})`} · Próxima:{" "}
            {canal.proximaConsulta ? horaColombia(canal.proximaConsulta) : "—"}
          </span>
        </div>
        <Interruptor activo={f.encendido} onCambio={(v) => cambiar({ encendido: v })} etiqueta="Consultar este canal" />
      </div>

      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <label className={ETIQUETA_CLS}>
          Proveedor
          <select className={INPUT_CLS} value={f.proveedor} onChange={(e) => cambiar({ proveedor: e.target.value })}>
            <option value="">Elige el proveedor</option>
            {opciones.map((p) => (
              <option key={p.clave} value={p.clave} disabled={!p.conectado}>
                {p.nombre}
                {p.conectado ? "" : " (no conectado)"}
              </option>
            ))}
          </select>
          {/* B2-7: por qué un proveedor no está disponible, solo mientras se elige o si el elegido dejó de estarlo. */}
          {opciones
            .filter((p) => !p.conectado && (!f.proveedor || p.clave === f.proveedor))
            .map((p) => (
              <span key={p.clave} className="font-normal opacity-70">
                {p.nombre}: {p.motivo ?? "no conectado"}
              </span>
            ))}
        </label>
        <label className={ETIQUETA_CLS}>
          Agenda
          <select className={INPUT_CLS} value={f.modo} onChange={(e) => cambiar({ modo: e.target.value as FormCanal["modo"] })}>
            <option value="HorasFijas">A horas fijas</option>
            <option value="Intervalo">Cada cierto tiempo</option>
          </select>
        </label>
        <label className="flex items-center gap-2 self-end pb-2 text-xs font-semibold">
          <input type="checkbox" checked={f.soloDiasHabiles} onChange={(e) => cambiar({ soloDiasHabiles: e.target.checked })} />
          Solo días hábiles
        </label>
      </div>

      {f.modo === "HorasFijas" ? (
        <div className={ETIQUETA_CLS}>
          Horas de consulta (hora de Colombia)
          <Horas horas={f.horas} onCambio={(horas) => cambiar({ horas })} />
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <label className={ETIQUETA_CLS}>
            Cada (minutos, mínimo {INTERVALO_MINIMO})
            <input className={INPUT_CLS} inputMode="numeric" value={f.intervaloMinutos} onChange={(e) => cambiar({ intervaloMinutos: e.target.value })} />
          </label>
          <label className={ETIQUETA_CLS}>
            Desde
            <input className={INPUT_CLS} type="time" value={f.desde} onChange={(e) => cambiar({ desde: e.target.value })} />
          </label>
          <label className={ETIQUETA_CLS}>
            Hasta
            <input className={INPUT_CLS} type="time" value={f.hasta} onChange={(e) => cambiar({ hasta: e.target.value })} />
          </label>
        </div>
      )}

      {sucio && errores.length > 0 && <InlineAlert tone="warning" compact>{errores.join(" ")}</InlineAlert>}
      {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
      <div className="flex justify-end gap-2">
        <button type="button" className={controlCls(false)} disabled={!sucio || ocupado} onClick={() => setForm(null)}>
          Descartar
        </button>
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!sucio || ocupado || errores.length > 0} onClick={guardar}>
          {ocupado ? "Guardando…" : "Guardar canal"}
        </button>
      </div>
    </fieldset>
  );
}

/**
 * Configurador de consultas de una empresa (HU #13522, SuperAdmin). Encender el producto no consulta nada: se consulta
 * con la configuración ACTIVA, el producto habilitado y el canal encendido con un proveedor conectado.
 */
export function ConsultasAdmin({ empresaId }: { empresaId: string }) {
  const toast = useToast();
  const vista = useCarga(`consultas-admin:${empresaId}`, (signal) => apiFetch<ConfiguracionConsultasVista>(`/admin/empresas/${empresaId}/consultas`, { signal }));
  const [confirmar, setConfirmar] = useState<"activar" | "pausar" | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (vista.estado === "loading") return <p className="text-sm opacity-60">Cargando configuración de consultas…</p>;
  if (vista.estado === "error" || !vista.datos) return <InlineAlert tone="error">{vista.error ?? "No se pudo cargar."}</InlineAlert>;

  const v = vista.datos;
  const estado = ESTADO_CONSULTAS[v.estado] ?? ESTADO_CONSULTAS.Borrador;
  const encendidos = v.canales.filter((c) => c.encendido && c.configurado).length;

  const ejecutar = async () => {
    if (!confirmar) return;
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/admin/empresas/${empresaId}/consultas/${confirmar}`, { method: "POST" });
      toast.show(confirmar === "activar" ? "Consultas activadas." : "Consultas pausadas.", "success");
      setConfirmar(null);
      vista.recargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo cambiar el estado.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-semibold">Consultas automáticas</span>
          <StatusBadge label={estado.etiqueta} tone={estado.tono} />
          {v.activadaEn && <span className="text-[11px] opacity-70">Activada el {formatoFechaHora(v.activadaEn)}</span>}
        </div>
        <div className="flex gap-2">
          {v.estado === "Activa" ? (
            <button type="button" className={controlCls(false)} onClick={() => setConfirmar("pausar")}>
              <Pause className="h-3.5 w-3.5" aria-hidden />
              Pausar
            </button>
          ) : (
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={v.productoHabilitado === false || encendidos === 0} onClick={() => setConfirmar("activar")}>
              <Play className="h-3.5 w-3.5" aria-hidden />
              Activar
            </button>
          )}
        </div>
      </div>

      {v.productoHabilitado === false && (
        <InlineAlert tone="warning" title="La empresa no tiene Comparendos habilitado">
          Mientras el producto no esté habilitado en la plataforma (Compañías → Productos) no se consulta nada y no se puede activar.
        </InlineAlert>
      )}
      {v.estado !== "Activa" && v.productoHabilitado !== false && (
        <p className="text-xs opacity-70">
          Configura y guarda cada canal; al activar empiezan las consultas en la agenda de cada uno. Tener el producto encendido no consulta nada por sí solo.
        </p>
      )}

      <div className="flex flex-col gap-4">
        {v.canales.map((c) => (
          <Canal key={`${c.canal}:${JSON.stringify(c)}`} empresaId={empresaId} canal={c} proveedores={v.proveedores ?? []} onGuardado={vista.recargar} />
        ))}
      </div>

      {confirmar && (
        <Modal
          open
          onClose={() => setConfirmar(null)}
          busy={ocupado}
          zClassName="z-[120]"
          title={confirmar === "activar" ? "¿Activar las consultas?" : "¿Pausar las consultas?"}
          description={
            confirmar === "activar"
              ? `Desde ahora se consultan los ${encendidos} canal${encendidos === 1 ? "" : "es"} encendido${encendidos === 1 ? "" : "s"} en su agenda. Queda en el historial.`
              : "No se consulta nada hasta volver a activar. Los datos de la empresa no se tocan. Queda en el historial."
          }
          footer={
            <div className="flex justify-end gap-2">
              <button type="button" className={controlCls(false)} onClick={() => setConfirmar(null)} disabled={ocupado}>
                Cancelar
              </button>
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={ejecutar}>
                {ocupado ? "Aplicando…" : confirmar === "activar" ? "Sí, activar" : "Sí, pausar"}
              </button>
            </div>
          }
        >
          {error ? <InlineAlert tone="error">{error}</InlineAlert> : <span className="sr-only">Confirmación</span>}
        </Modal>
      )}
    </div>
  );
}

/** Vista de la empresa (HU #13522): la agenda de sus consultas, sin proveedores. */
export function ConsultasEmpresa({ empresa }: { empresa?: string | null }) {
  const vista = useCarga(`consultas-empresa:${empresa ?? "propia"}`, (signal) =>
    apiFetch<ConfiguracionConsultasVista>("/configuracion/consultas", { signal, query: { empresa: empresa ?? undefined } }),
  );
  if (vista.estado === "loading") return <p className="text-sm opacity-60">Cargando consultas…</p>;
  if (vista.estado === "error" || !vista.datos) return <InlineAlert tone="error">{vista.error ?? "No se pudo cargar."}</InlineAlert>;
  const v = vista.datos;
  const estado = ESTADO_CONSULTAS[v.estado] ?? ESTADO_CONSULTAS.Borrador;
  const canales = v.canales.filter((c) => c.configurado);

  return (
    <section className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="text-sm font-semibold">Consultas automáticas de comparendos</h2>
        <StatusBadge label={estado.etiqueta} tone={estado.tono} />
      </div>
      <p className="text-[11px] text-[#5B6B82] dark:text-slate-300">
        Cuándo se consultan tus comparendos. La agenda la define FLIT: si necesitas cambiarla, solicítalo a operaciones.
      </p>
      {canales.length === 0 ? (
        <p className="text-sm opacity-70">Todavía no hay consultas automáticas configuradas para tu empresa.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="bg-[#DFE5ED] dark:bg-white/10">
                <th className="px-3 py-2 font-semibold">Consulta</th>
                <th className="px-3 py-2 font-semibold">Agenda</th>
                <th className="px-3 py-2 font-semibold">Última consulta</th>
                <th className="px-3 py-2 font-semibold">Próxima consulta</th>
              </tr>
            </thead>
            <tbody>
              {canales.map((c) => (
                <tr key={c.canal} className="border-b border-[#E2E8F0] dark:border-white/10">
                  <td className="px-3 py-2 font-semibold">{c.etiqueta}</td>
                  <td className="px-3 py-2">{c.encendido ? (c.descripcion ?? "—") : "Apagada"}</td>
                  <td className="px-3 py-2">
                    {c.ultimaConsulta ? formatoFechaHora(c.ultimaConsulta) : "Nunca"}
                    {c.ultimoResultado && <span className="opacity-70"> · {RESULTADO_CICLO[c.ultimoResultado] ?? c.ultimoResultado}</span>}
                  </td>
                  <td className="px-3 py-2">{c.proximaConsulta ? horaColombia(c.proximaConsulta) : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

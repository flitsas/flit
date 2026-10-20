"use client";

import { useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { InlineAlert, Modal } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, ETIQUETA_CLS, INPUT_CLS, controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { accionDia, DIAS_SEMANA, semanasDelMes, tipoDia, type AnioCalendario, type DiaCalendario } from "@/lib/calendario";
import { diaColombia, formatoFecha } from "@/lib/format/date";
import { nombreMes } from "@/lib/reportes";
import { useCarga } from "@/lib/useCarga";

const COLOR: Record<ReturnType<typeof tipoDia>, string> = {
  habil: "bg-white text-[#162744] hover:border-[#557EFF] dark:bg-transparent dark:text-white",
  finDeSemana: "bg-[#F1F4F9] text-[#8A97AB] dark:bg-white/5 dark:text-slate-500",
  festivo: "bg-[#FFE8DC] font-semibold text-[#B03A00] hover:border-[#FF4E00] dark:bg-[#FF4E00]/20 dark:text-[#FFB38F]",
};

/** Confirmación de un cambio: muestra cuántos comparendos se recalcularán antes de guardar y cuántos se recalcularon después. */
function CambiarDia({ dia, onCerrar, onGuardado }: { dia: DiaCalendario; onCerrar: () => void; onGuardado: () => void }) {
  const toast = useToast();
  const marcar = accionDia(dia) === "marcar";
  const [descripcion, setDescripcion] = useState("");
  const [ocupado, setOcupado] = useState(false);
  const [recalculados, setRecalculados] = useState<number | null>(null);
  const impacto = useCarga(`impacto:${dia.fecha}`, (signal) => apiFetch<{ comparendos: number }>("/admin/calendario/impacto", { signal, query: { fecha: dia.fecha } }));
  const faltaDescripcion = marcar && descripcion.trim().length === 0;

  const guardar = async () => {
    setOcupado(true);
    try {
      const r = await apiFetch<{ recalculados: number }>(`/admin/calendario/${dia.fecha}`, {
        method: "PUT",
        body: { esFestivo: marcar, descripcion: marcar ? descripcion.trim() : null },
      });
      setRecalculados(r.recalculados);
      toast.show(marcar ? "Festivo agregado." : "Festivo quitado.", "success");
      onGuardado();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo guardar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={ocupado}
      title={marcar ? `Marcar el ${formatoFecha(dia.fecha)} como festivo` : `Quitar el festivo del ${formatoFecha(dia.fecha)}`}
      description={!marcar && dia.descripcionFestivo ? `${dia.descripcionFestivo} · origen ${dia.origen === "MANUAL" ? "manual" : "calculado"}` : undefined}
      footer={
        recalculados === null ? (
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={onCerrar} disabled={ocupado}>
              Cancelar
            </button>
            <button type="button" className={BOTON_PRIMARIO_CLS} onClick={guardar} disabled={ocupado || faltaDescripcion || impacto.estado === "loading"}>
              {ocupado ? "Guardando y recalculando…" : "Guardar"}
            </button>
          </div>
        ) : (
          <div className="flex justify-end">
            <button type="button" className={BOTON_PRIMARIO_CLS} onClick={onCerrar}>
              Cerrar
            </button>
          </div>
        )
      }
    >
      <div className="flex flex-col gap-3">
        {recalculados === null ? (
          <>
            {marcar && (
              <label className={ETIQUETA_CLS}>
                Descripción
                <input className={INPUT_CLS} maxLength={120} value={descripcion} placeholder="Día cívico decretado" onChange={(e) => setDescripcion(e.target.value)} autoFocus />
              </label>
            )}
            {impacto.estado === "loading" && <p className="text-xs opacity-60">Calculando cuántos comparendos se afectan…</p>}
            {impacto.estado === "error" && <InlineAlert tone="error" compact>{impacto.error}</InlineAlert>}
            {impacto.datos && (
              <InlineAlert tone={impacto.datos.comparendos > 0 ? "warning" : "info"} compact>
                {impacto.datos.comparendos > 0
                  ? `Se recalcularán los plazos de hasta ${impacto.datos.comparendos.toLocaleString("es-CO")} comparendos activos con fechas desde este día. Cada uno queda con su evento en el timeline.`
                  : "Ningún comparendo activo tiene fechas desde este día: no hay plazos que recalcular."}
              </InlineAlert>
            )}
            <p className="text-xs opacity-70">El cambio queda en la auditoría de configuración.</p>
          </>
        ) : (
          <InlineAlert tone="success" title="Calendario actualizado">
            {recalculados === 0 ? "Ningún plazo cambió." : `Se recalcularon los plazos de ${recalculados.toLocaleString("es-CO")} comparendos.`}
          </InlineAlert>
        )}
      </div>
    </Modal>
  );
}

function Mes({ datos, mes, onElegir }: { datos: AnioCalendario; mes: number; onElegir: (d: DiaCalendario) => void }) {
  const semanas = semanasDelMes(datos.dias, datos.anio, mes);
  return (
    <div className="flex flex-col gap-1">
      <h3 className="text-xs font-semibold capitalize text-[#162744] dark:text-white">{nombreMes(mes)}</h3>
      <div className="grid grid-cols-7 gap-0.5 text-center text-[10px] text-[#8A97AB]">
        {DIAS_SEMANA.map((d) => (
          <span key={d}>{d}</span>
        ))}
      </div>
      {semanas.map((s, i) => (
        <div key={i} className="grid grid-cols-7 gap-0.5">
          {s.map((d, j) =>
            d ? (
              <button
                key={d.fecha}
                type="button"
                disabled={accionDia(d) === null}
                title={d.esFestivo ? `${d.descripcionFestivo ?? "Festivo"}${d.origen === "MANUAL" ? " · manual" : ""}` : d.esFinDeSemana ? "Fin de semana" : "Hábil · clic para marcarlo festivo"}
                onClick={() => onElegir(d)}
                className={`relative h-7 rounded-md border border-transparent text-[11px] transition disabled:cursor-default ${COLOR[tipoDia(d)]}`}
              >
                {Number(d.fecha.slice(8))}
                {d.origen === "MANUAL" && <span className="absolute right-0.5 top-0.5 h-1.5 w-1.5 rounded-full bg-[#557EFF]" aria-label="Cambio manual" />}
              </button>
            ) : (
              <span key={`v${j}`} />
            ),
          )}
        </div>
      ))}
    </div>
  );
}

function Calendario() {
  const [anio, setAnio] = useState(() => Number(diaColombia().slice(0, 4)));
  const [elegido, setElegido] = useState<DiaCalendario | null>(null);
  const datos = useCarga(`calendario:${anio}`, (signal) => apiFetch<AnioCalendario>("/admin/calendario", { signal, query: { anio } }));
  const d = datos.datos;
  const manuales = (d?.dias ?? []).filter((x) => x.origen === "MANUAL");

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Calendario hábil"
        descripcion="Los plazos cuentan días hábiles: sin sábados, domingos ni festivos. Haz clic en un día hábil para marcarlo festivo, o en un festivo para quitarlo."
        acciones={
          <div className="flex items-center gap-2">
            <button type="button" className={controlCls(false)} onClick={() => setAnio(anio - 1)} aria-label="Año anterior">
              <ChevronLeft className="h-3.5 w-3.5" aria-hidden />
            </button>
            <span className="w-12 text-center text-sm font-semibold">{anio}</span>
            <button type="button" className={controlCls(false)} onClick={() => setAnio(anio + 1)} aria-label="Año siguiente">
              <ChevronRight className="h-3.5 w-3.5" aria-hidden />
            </button>
          </div>
        }
      />
      {datos.estado === "loading" && <p className="text-sm opacity-60">Cargando el año…</p>}
      {datos.estado === "error" && (
        <InlineAlert tone="error" title={`No hay calendario para ${anio}`}>
          {datos.error} El calendario va de 2015 a 2040.
        </InlineAlert>
      )}
      {d && datos.estado === "ready" && (
        <>
          <div className="flex flex-wrap items-center gap-4 text-xs text-[#5B6B82] dark:text-slate-300">
            <span>
              <b>{d.habiles}</b> días hábiles · <b>{d.festivos}</b> festivos
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="h-3 w-3 rounded-sm bg-[#FFE8DC]" aria-hidden /> Festivo
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="h-3 w-3 rounded-sm bg-[#F1F4F9]" aria-hidden /> Fin de semana
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="h-1.5 w-1.5 rounded-full bg-[#557EFF]" aria-hidden /> Cambio manual
            </span>
          </div>
          <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            {Array.from({ length: 12 }, (_, m) => (
              <Mes key={m} datos={d} mes={m} onElegir={setElegido} />
            ))}
          </div>
          {manuales.length > 0 && (
            <div className="flex flex-col gap-1 text-xs">
              <h2 className="text-sm font-semibold">Cambios manuales del año</h2>
              {manuales.map((x) => (
                <span key={x.fecha}>
                  {formatoFecha(x.fecha)}: {x.esFestivo ? `festivo · ${x.descripcionFestivo ?? ""}` : "hábil (se quitó el festivo)"}
                </span>
              ))}
            </div>
          )}
        </>
      )}
      {elegido && <CambiarDia key={elegido.fecha} dia={elegido} onCerrar={() => setElegido(null)} onGuardado={datos.recargar} />}
    </section>
  );
}

/** Calendario hábil (HU #13456; §7.25, RN-075). Solo SuperAdmin. */
export default function CalendarioPagina() {
  return (
    <Requiere superAdmin>
      <Calendario />
    </Requiere>
  );
}

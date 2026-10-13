"use client";

import { useState } from "react";
import { apiFetch } from "@/lib/api";
import {
  CAMBIO_CONTRAVENTOR,
  ESTADO_LEGAL,
  ESTADO_PROCESO,
  FUENTE,
  ULTIMO_AVISO,
  opciones,
} from "@/lib/etiquetas";
import type { FiltrosBandeja, TriEstado } from "@/lib/filtros";
import type { Nit, Organismo, Pagina } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";
import { MultiSeleccion, type Opcion } from "@/components/ui/MultiSeleccion";
import { ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";

function Tri({ etiqueta, valor, onCambio, si, no }: { etiqueta: string; valor: TriEstado; onCambio: (v: TriEstado) => void; si: string; no: string }) {
  return (
    <label className={ETIQUETA_CLS}>
      {etiqueta}
      <select className={INPUT_CLS} value={valor} onChange={(e) => onCambio(e.target.value as TriEstado)}>
        <option value="">Todos</option>
        <option value="true">{si}</option>
        <option value="false">{no}</option>
      </select>
    </label>
  );
}

function Rango({ etiqueta, desde, hasta, onDesde, onHasta }: { etiqueta: string; desde: string; hasta: string; onDesde: (v: string) => void; onHasta: (v: string) => void }) {
  return (
    <fieldset className={ETIQUETA_CLS}>
      <legend className="mb-1">{etiqueta}</legend>
      <div className="flex gap-2">
        <input type="date" className={INPUT_CLS} value={desde} onChange={(e) => onDesde(e.target.value)} aria-label={`${etiqueta} desde`} />
        <input type="date" className={INPUT_CLS} value={hasta} onChange={(e) => onHasta(e.target.value)} aria-label={`${etiqueta} hasta`} />
      </div>
    </fieldset>
  );
}

/** Panel de filtros de la bandeja (§7.1). Cambia los filtros al momento; la bandeja vuelve a la página 1. */
export function PanelFiltros({ filtros, onCambio, empresa }: { filtros: FiltrosBandeja; onCambio: (f: FiltrosBandeja) => void; empresa: string | null }) {
  const set = <K extends keyof FiltrosBandeja>(k: K, v: FiltrosBandeja[K]) => onCambio({ ...filtros, [k]: v });
  const [buscarOt, setBuscarOt] = useState("");
  const [nombresOt, setNombresOt] = useState<Record<string, string>>({});

  const nits = useCarga(`nits:${empresa ?? ""}`, (signal) => apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa } }));
  const ots = useCarga(`ots:${buscarOt}`, (signal) => apiFetch<Pagina<Organismo>>("/organismos", { signal, query: { buscar: buscarOt, tamano: 30 } }));
  const opcionesNit: Opcion[] = (nits.datos ?? []).map((n) => ({ valor: n.id, texto: `${n.numero}${n.nombreTitular ? ` · ${n.nombreTitular}` : ""}` }));
  const opcionesOt: Opcion[] = (ots.datos?.items ?? []).map((o) => ({ valor: o.id, texto: o.nombre }));

  return (
    <div className="grid grid-cols-1 gap-3 rounded-2xl border border-[#DFE5ED] p-4 sm:grid-cols-2 lg:grid-cols-4 dark:border-white/10">
      <label className={ETIQUETA_CLS}>
        NIT
        <MultiSeleccion etiqueta="NIT" opciones={opcionesNit} valores={filtros.nits} onCambio={(v) => set("nits", v)} />
      </label>
      <label className={ETIQUETA_CLS}>
        Placa
        <input className={INPUT_CLS} value={filtros.placa} onChange={(e) => set("placa", e.target.value)} placeholder="ABC123" />
      </label>
      <label className={ETIQUETA_CLS}>
        Número de comparendo
        <input className={INPUT_CLS} value={filtros.numero} onChange={(e) => set("numero", e.target.value)} />
      </label>
      <label className={ETIQUETA_CLS}>
        Organismo de tránsito
        <MultiSeleccion
          etiqueta="Organismo"
          opciones={opcionesOt}
          valores={filtros.organismos}
          textosElegidos={nombresOt}
          onBuscar={setBuscarOt}
          onCambio={(v) => {
            setNombresOt((prev) => ({ ...prev, ...Object.fromEntries(opcionesOt.map((o) => [o.valor, o.texto])) }));
            set("organismos", v);
          }}
        />
      </label>
      <Rango etiqueta="Fecha de imposición" desde={filtros.imposicionDesde} hasta={filtros.imposicionHasta}
        onDesde={(v) => set("imposicionDesde", v)} onHasta={(v) => set("imposicionHasta", v)} />
      <Rango etiqueta="Fecha de notificación" desde={filtros.notificacionDesde} hasta={filtros.notificacionHasta}
        onDesde={(v) => set("notificacionDesde", v)} onHasta={(v) => set("notificacionHasta", v)} />
      <Rango etiqueta="Fecha de registro" desde={filtros.registroDesde} hasta={filtros.registroHasta}
        onDesde={(v) => set("registroDesde", v)} onHasta={(v) => set("registroHasta", v)} />
      <label className={ETIQUETA_CLS}>
        Estado legal
        <MultiSeleccion etiqueta="Estado legal" opciones={opciones(ESTADO_LEGAL)} valores={filtros.estadoLegal} onCambio={(v) => set("estadoLegal", v)} />
      </label>
      <label className={ETIQUETA_CLS}>
        Estado del proceso
        <MultiSeleccion etiqueta="Estado del proceso" opciones={opciones(ESTADO_PROCESO)} valores={filtros.estadoProceso} onCambio={(v) => set("estadoProceso", v)} />
      </label>
      <label className={ETIQUETA_CLS}>
        Último aviso
        <MultiSeleccion etiqueta="Último aviso" opciones={opciones(ULTIMO_AVISO)} valores={filtros.ultimoAviso} onCambio={(v) => set("ultimoAviso", v)} />
      </label>
      <label className={ETIQUETA_CLS}>
        Cambio de contraventor
        <MultiSeleccion etiqueta="Cambio de contraventor" opciones={opciones(CAMBIO_CONTRAVENTOR)} valores={filtros.cambioContraventor} onCambio={(v) => set("cambioContraventor", v)} />
      </label>
      <label className={ETIQUETA_CLS}>
        Fuente
        <MultiSeleccion etiqueta="Fuente" opciones={opciones(FUENTE).filter((o) => o.valor !== "MigracionV1")} valores={filtros.fuente} onCambio={(v) => set("fuente", v)} />
      </label>
      <Tri etiqueta="Contraventor" valor={filtros.conContraventor} onCambio={(v) => set("conContraventor", v)} si="Con contraventor" no="Sin contraventor" />
      <Tri etiqueta="Pago" valor={filtros.pagado} onCambio={(v) => set("pagado", v)} si="Pagados" no="Sin pago" />
      <Tri etiqueta="Derecho de petición" valor={filtros.conDp} onCambio={(v) => set("conDp", v)} si="Con DP" no="Sin DP" />
      <label className={ETIQUETA_CLS}>
        Resultado del DP
        <select className={INPUT_CLS} value={filtros.resultadoDp} onChange={(e) => set("resultadoDp", e.target.value as FiltrosBandeja["resultadoDp"])}>
          <option value="">Todos</option>
          <option value="Favorable">Favorable</option>
          <option value="Desfavorable">Desfavorable</option>
          <option value="SinRespuesta">Enviado, sin respuesta</option>
        </select>
      </label>
      <Tri etiqueta="Cobro coactivo" valor={filtros.cobroCoactivo} onCambio={(v) => set("cobroCoactivo", v)} si="En cobro coactivo" no="Sin cobro coactivo" />
      <label className={ETIQUETA_CLS}>
        Estado
        <select className={INPUT_CLS} value={filtros.activos} onChange={(e) => set("activos", e.target.value as FiltrosBandeja["activos"])}>
          <option value="activos">Activos</option>
          <option value="inactivos">Inactivos</option>
          <option value="todos">Todos</option>
        </select>
      </label>
    </div>
  );
}

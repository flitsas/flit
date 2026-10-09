"use client";

import { useState } from "react";
import { Play } from "lucide-react";
import { DataTable, InlineAlert, Modal, StatusBadge, enabledTone, usePaginacion } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { CANAL, canal, opciones, RESULTADO_CICLO, resultadoCiclo, TRABAJO } from "@/lib/etiquetas";
import { formatoFechaHora } from "@/lib/format/date";
import type { Ciclo, DetalleCiclo, Pagina, Trabajo } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/** «2026-10-10T07:00» (hora de Colombia, sin zona) → «10 oct 2026, 07:00». */
function siguiente(valor: string | null): string {
  if (!valor) return "—";
  return formatoFechaHora(`${valor}:00-05:00`);
}

function EditarTrabajo({ trabajo, onCerrar, onGuardado }: { trabajo: Trabajo | null; onCerrar: () => void; onGuardado: () => void }) {
  const [cron, setCron] = useState(trabajo?.expresionCron ?? "");
  const [activo, setActivo] = useState(trabajo?.activo ?? true);
  const [habiles, setHabiles] = useState(trabajo?.soloDiasHabiles ?? false);
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  const guardar = async () => {
    if (!trabajo) return;
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/admin/ingesta/trabajos/${trabajo.tipo}`, { method: "PATCH", body: { expresionCron: cron, activo, soloDiasHabiles: habiles } });
      onGuardado();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal
      open={trabajo !== null}
      onClose={onCerrar}
      busy={ocupado}
      title={trabajo ? TRABAJO[trabajo.tipo] ?? trabajo.tipo : "Trabajo"}
      description="La expresión cron va en hora de Colombia: minuto hora día mes día-de-la-semana. Ejemplo: «0 7,13 * * *» = todos los días a las 7:00 y 13:00."
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={guardar}>
            {ocupado ? "Guardando…" : "Guardar"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <label className={ETIQUETA_CLS}>
          Expresión cron
          <input className={`${INPUT_CLS} font-mono`} value={cron} onChange={(e) => setCron(e.target.value)} />
        </label>
        <label className="flex items-center gap-2 text-xs font-semibold">
          <input type="checkbox" checked={activo} onChange={(e) => setActivo(e.target.checked)} />
          Activo
        </label>
        <label className="flex items-center gap-2 text-xs font-semibold">
          <input type="checkbox" checked={habiles} onChange={(e) => setHabiles(e.target.checked)} />
          Solo en días hábiles
        </label>
        {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
      </div>
    </Modal>
  );
}

function EjecutarAhora({ abierto, onCerrar }: { abierto: boolean; onCerrar: () => void }) {
  const toast = useToast();
  const [empresa, setEmpresa] = useState<string | null>(null);
  const [canalElegido, setCanal] = useState("SimitNit");
  const [ocupado, setOcupado] = useState(false);

  const ejecutar = async () => {
    setOcupado(true);
    try {
      await apiFetch("/admin/ingesta/ejecutar", { method: "POST", body: { canal: canalElegido, empresaId: empresa } });
      toast.show("Ejecución en curso. El ciclo aparecerá en la tabla en unos segundos.", "success");
      onCerrar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo ejecutar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal
      open={abierto}
      onClose={onCerrar}
      busy={ocupado}
      title="Ejecutar ahora"
      description="La ejecución manual es por empresa y por canal; no se ejecuta por un NIT o una placa sueltos."
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !empresa} onClick={ejecutar}>
            {ocupado ? "Ejecutando…" : "Ejecutar"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <SelectorEmpresa valor={empresa} onCambio={setEmpresa} />
        <label className={ETIQUETA_CLS}>
          Canal
          <select className={INPUT_CLS} value={canalElegido} onChange={(e) => setCanal(e.target.value)}>
            {opciones(CANAL).map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
        </label>
      </div>
    </Modal>
  );
}

function DetalleDeCiclo({ id, onCerrar }: { id: string | null; onCerrar: () => void }) {
  const toast = useToast();
  const carga = useCarga(id ? `ciclo:${id}` : null, (signal) => apiFetch<DetalleCiclo>(`/admin/ingesta/ciclos/${id}`, { signal }));
  const d = carga.datos;
  return (
    <Modal open={id !== null} onClose={onCerrar} title="Detalle del ciclo" size="2xl">
      {carga.estado === "loading" && <p className="text-sm opacity-60">Cargando…</p>}
      {carga.estado === "error" && <InlineAlert tone="error">{carga.error}</InlineAlert>}
      {d && (
        <div className="flex flex-col gap-4 text-xs">
          <div className="flex flex-wrap items-center gap-2">
            <Chip e={canal(d.ciclo.canal)} />
            <Chip e={resultadoCiclo(d.ciclo.resultado)} />
            <span>
              {formatoFechaHora(d.ciclo.inicioEn)} → {formatoFechaHora(d.ciclo.finEn)} · {d.ciclo.clavesTotal} claves, {d.ciclo.clavesError} con error
            </span>
          </div>
          {d.ciclo.mensajeError && <InlineAlert tone="error" compact>{d.ciclo.mensajeError}</InlineAlert>}
          <h3 className="text-sm font-semibold">Por empresa y clave</h3>
          <DataTable
            ariaLabel="Resultado por empresa"
            rows={d.logs}
            getRowKey={(l) => l.id}
            emptyMessage="No había claves monitoreadas."
            minWidth={1100}
            columns={[
              { key: "empresa", header: "Empresa", render: (l) => l.empresa ?? l.empresaId },
              { key: "clave", header: "Clave", render: (l) => `${l.clave ?? "—"}${l.municipioCodigo ? ` · ${l.municipioCodigo}` : ""}` },
              { key: "obt", header: "Obtenidos", align: "right", render: (l) => l.cantidadObtenidas },
              { key: "nue", header: "Nuevos", align: "right", render: (l) => l.cantidadNuevas },
              { key: "act", header: "Actualizados", align: "right", render: (l) => l.cantidadActualizadas },
              { key: "otra", header: "De otra fuente", align: "right", render: (l) => l.cantidadDuplicadasOtraFuente },
              { key: "ina", header: "Inactivados", align: "right", render: (l) => l.cantidadInactivadas },
              { key: "rea", header: "Reactivados", align: "right", render: (l) => l.cantidadReactivadas },
              { key: "dp", header: "DP favorables", align: "right", render: (l) => l.cantidadDpFavorableAutomatico },
              { key: "res", header: "Resultado", render: (l) => <Chip e={resultadoCiclo(l.resultado)} title={l.mensajeError ?? undefined} /> },
            ]}
          />
          <h3 className="text-sm font-semibold">Consultas a la fuente</h3>
          <DataTable
            ariaLabel="Consultas a la fuente"
            rows={d.consultas}
            getRowKey={(c) => c.id}
            emptyMessage="Sin consultas."
            minWidth={900}
            columns={[
              { key: "clave", header: "Clave consultada", render: (c) => `${c.claveConsultada}${c.municipioCodigo ? ` · ${c.municipioCodigo}` : ""}` },
              { key: "http", header: "HTTP", render: (c) => c.httpStatus ?? "—" },
              { key: "res", header: "Resultado", render: (c) => (c.resultado === "Error" ? <StatusBadge label="Error" tone="danger" /> : <StatusBadge label={c.resultado === "ExitosaVacia" ? "Sin comparendos" : "Con datos"} tone="success" />) },
              { key: "err", header: "Error", render: (c) => (c.tipoError ? `${c.tipoError}: ${c.mensajeError ?? ""}` : "—") },
              { key: "items", header: "Ítems", align: "right", render: (c) => c.cantidadItems },
              { key: "ms", header: "Duración", align: "right", render: (c) => `${c.duracionMs} ms` },
              {
                key: "crudo",
                header: "",
                align: "right",
                render: (c) =>
                  c.tieneRespuesta ? (
                    <button
                      type="button"
                      className="font-semibold text-[#3B4FD6] hover:underline"
                      onClick={() => apiDownload(`/admin/ingesta/consultas/${c.id}/respuesta`, "respuesta.txt").catch((e: unknown) => toast.show(e instanceof Error ? e.message : "No se pudo descargar.", "error"))}
                    >
                      Respuesta
                    </button>
                  ) : null,
              },
            ]}
          />
        </div>
      )}
    </Modal>
  );
}

/** Trabajos programados e ingesta (HU #13463, §7.19). Solo SuperAdmin. */
export default function Ingesta() {
  const toast = useToast();
  const [editar, setEditar] = useState<Trabajo | null>(null);
  const [ejecutar, setEjecutar] = useState(false);
  const [ciclo, setCiclo] = useState<string | null>(null);
  const [filtroCanal, setFiltroCanal] = useState("");
  const [filtroResultado, setFiltroResultado] = useState("");
  const pg = usePaginacion(25);
  const trabajos = useCarga("trabajos", (signal) => apiFetch<Trabajo[]>("/admin/ingesta/trabajos", { signal }));
  const ciclos = useCarga(`ciclos:${filtroCanal}:${filtroResultado}:${pg.page}:${pg.pageSize}`, (signal) =>
    apiFetch<Pagina<Ciclo>>("/admin/ingesta/ciclos", { signal, query: { canal: filtroCanal, resultado: filtroResultado, pagina: pg.page, tamano: pg.pageSize } }),
  );

  return (
    <Requiere superAdmin>
      <section className="flex flex-col gap-4">
        <Encabezado
          titulo="Trabajos e ingesta"
          descripcion="Cuándo se consultan SIMIT y la fuente municipal, y qué pasó en cada ciclo."
          acciones={
            <button type="button" className={BOTON_PRIMARIO_CLS} onClick={() => setEjecutar(true)}>
              <Play className="h-3.5 w-3.5" aria-hidden />
              Ejecutar ahora
            </button>
          }
        />
        <DataTable
          ariaLabel="Trabajos programados"
          rows={trabajos.datos ?? []}
          getRowKey={(t) => t.tipo}
          status={trabajos.estado}
          errorMessage={trabajos.error ?? undefined}
          onRetry={trabajos.recargar}
          onRowClick={(t) => setEditar(t)}
          columns={[
            { key: "tipo", header: "Trabajo", render: (t) => <span className="font-semibold">{TRABAJO[t.tipo] ?? t.tipo}</span> },
            { key: "cron", header: "Programación", render: (t) => <span className="font-mono">{t.expresionCron}</span> },
            { key: "sig", header: "Siguiente ejecución", render: (t) => (t.activo ? siguiente(t.siguiente) : "—") },
            { key: "habiles", header: "Solo días hábiles", render: (t) => (t.soloDiasHabiles ? "Sí" : "No") },
            { key: "activo", header: "Estado", render: (t) => <StatusBadge label={t.activo ? "Activo" : "Apagado"} tone={enabledTone(t.activo)} /> },
            { key: "ultima", header: "Última ejecución", render: (t) => formatoFechaHora(t.ultimaEjecucionEn) },
            { key: "res", header: "Último resultado", render: (t) => (t.ultimoResultado ? <Chip e={resultadoCiclo(t.ultimoResultado)} /> : "—") },
          ]}
        />

        <h2 className="mt-2 text-lg font-semibold">Ciclos</h2>
        <div className="flex flex-wrap gap-2">
          <select className={`${INPUT_CLS} w-56`} value={filtroCanal} onChange={(e) => { setFiltroCanal(e.target.value); pg.setPage(1); }} aria-label="Canal">
            <option value="">Todos los canales</option>
            {opciones(CANAL).map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
          <select className={`${INPUT_CLS} w-56`} value={filtroResultado} onChange={(e) => { setFiltroResultado(e.target.value); pg.setPage(1); }} aria-label="Resultado">
            <option value="">Todos los resultados</option>
            {opciones(RESULTADO_CICLO).map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
          <button type="button" className={controlCls(false)} onClick={ciclos.recargar}>
            Actualizar
          </button>
        </div>
        <DataTable
          ariaLabel="Ciclos de ingesta"
          rows={ciclos.datos?.items ?? []}
          getRowKey={(c) => c.id}
          status={ciclos.estado}
          errorMessage={ciclos.error ?? undefined}
          onRetry={ciclos.recargar}
          onRowClick={(c) => setCiclo(c.id)}
          emptyMessage="Todavía no hay ciclos."
          columns={[
            { key: "canal", header: "Canal", render: (c) => <Chip e={canal(c.canal)} /> },
            { key: "disparo", header: "Disparo", render: (c) => (c.disparo === "Manual" ? "Manual" : "Programado") },
            { key: "inicio", header: "Inicio", render: (c) => formatoFechaHora(c.inicioEn) },
            { key: "fin", header: "Fin", render: (c) => formatoFechaHora(c.finEn) },
            { key: "claves", header: "Claves", align: "right", render: (c) => c.clavesTotal },
            { key: "errores", header: "Con error", align: "right", render: (c) => c.clavesError },
            { key: "res", header: "Resultado", render: (c) => <Chip e={resultadoCiclo(c.resultado)} title={c.mensajeError ?? undefined} /> },
          ]}
          pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: ciclos.datos?.total ?? 0, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
        />

        {editar && (
          <EditarTrabajo
            key={editar.tipo}
            trabajo={editar}
            onCerrar={() => setEditar(null)}
            onGuardado={() => {
              setEditar(null);
              trabajos.recargar();
              toast.show("Programación guardada.", "success");
            }}
          />
        )}
        <EjecutarAhora abierto={ejecutar} onCerrar={() => { setEjecutar(false); ciclos.recargar(); }} />
        <DetalleDeCiclo id={ciclo} onCerrar={() => setCiclo(null)} />
      </section>
    </Requiere>
  );
}

"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { DataTable, InlineAlert, Modal, StatusBadge, activeTone, usePaginacion } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { HistorialAuditoria } from "@/components/HistorialAuditoria";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SearchInput } from "@/components/SearchInput";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { formatoFecha } from "@/lib/format/date";
import { tienePermiso } from "@/lib/sesion";
import type { Nit, PlacaMonitoreada } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const TIPOS_DOCUMENTO = ["NIT", "CC", "CE", "TI", "PA", "CD", "RC", "CV"];

/** Variantes con que se consulta un NIT: con y sin dígito de verificación (ADO #12956). */
function variantes(n: Nit): string {
  return n.tipoDocumento === "NIT" && n.digitoVerificacion !== null ? `${n.numeroBase} y ${n.numeroBase}${n.digitoVerificacion}` : n.numeroBase;
}

function Confirmar({ abierto, titulo, texto, onSi, onNo, ocupado }: { abierto: boolean; titulo: string; texto: string; onSi: () => void; onNo: () => void; ocupado: boolean }) {
  return (
    <Modal
      open={abierto}
      onClose={onNo}
      busy={ocupado}
      title={titulo}
      size="sm"
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onNo} disabled={ocupado}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} onClick={onSi} disabled={ocupado}>
            Confirmar
          </button>
        </div>
      }
    >
      <p className="text-sm">{texto}</p>
    </Modal>
  );
}

function Nits({ empresa, puedeGestionar }: { empresa: string | null; puedeGestionar: boolean }) {
  const toast = useToast();
  const [buscar, setBuscar] = useState("");
  const [nuevo, setNuevo] = useState(false);
  const [tipo, setTipo] = useState("NIT");
  const [numero, setNumero] = useState("");
  const [titular, setTitular] = useState("");
  const [errorNuevo, setErrorNuevo] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [cambiar, setCambiar] = useState<Nit | null>(null);
  const [historial, setHistorial] = useState<Nit | null>(null);
  const pg = usePaginacion(25);
  const lista = useCarga(`nits:${empresa}:${buscar}`, (signal) => apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa, buscar } }));
  const filas = lista.datos ?? [];

  const crear = async () => {
    setOcupado(true);
    setErrorNuevo(null);
    try {
      await apiFetch("/monitoreo/nits", { method: "POST", body: { tipoDocumento: tipo, numero, nombreTitular: titular || null } });
      toast.show("Documento agregado al monitoreo.", "success");
      setNuevo(false);
      setNumero("");
      setTitular("");
      lista.recargar();
    } catch (e) {
      setErrorNuevo(e instanceof Error ? e.message : "No se pudo agregar.");
    } finally {
      setOcupado(false);
    }
  };

  const alternar = async () => {
    if (!cambiar) return;
    setOcupado(true);
    try {
      await apiFetch(`/monitoreo/nits/${cambiar.id}`, { method: "PATCH", body: { activo: !cambiar.activo } });
      toast.show(cambiar.activo ? "Documento desactivado: ya no se consulta." : "Documento activado.", "success");
      setCambiar(null);
      lista.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cambiar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <SearchInput value={buscar} onChange={setBuscar} label="Buscar NIT o titular" placeholder="Buscar por número o titular" />
        {puedeGestionar && (
          <button type="button" className={BOTON_PRIMARIO_CLS} onClick={() => setNuevo(true)}>
            <Plus className="h-3.5 w-3.5" aria-hidden />
            Agregar NIT o documento
          </button>
        )}
      </div>
      <DataTable
        ariaLabel="NITs monitoreados"
        rows={pg.paginar(filas)}
        getRowKey={(n) => n.id}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="Todavía no vigilas ningún NIT."
        columns={[
          { key: "tipo", header: "Tipo", render: (n) => n.tipoDocumento },
          { key: "numero", header: "Número", render: (n) => <span className="font-semibold">{n.numero}</span> },
          { key: "variantes", header: "Se consulta como", render: (n) => variantes(n) },
          { key: "titular", header: "Titular", render: (n) => n.nombreTitular ?? "—" },
          { key: "estado", header: "Estado", render: (n) => <StatusBadge label={n.activo ? "Activo" : "Inactivo"} tone={activeTone(n.activo)} /> },
          { key: "creado", header: "Registrado", render: (n) => formatoFecha(n.creadoEn) },
          { key: "activos", header: "Comparendos activos", align: "right", render: (n) => n.comparendosActivos.toLocaleString("es-CO") },
          {
            key: "historial",
            header: "",
            render: (n) => (
              <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => setHistorial(n)}>
                Historial
              </button>
            ),
          },
          {
            key: "accion",
            header: "",
            align: "right",
            render: (n) =>
              puedeGestionar ? (
                <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => setCambiar(n)}>
                  {n.activo ? "Desactivar" : "Activar"}
                </button>
              ) : null,
          },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />

      <Modal
        open={nuevo}
        onClose={() => setNuevo(false)}
        busy={ocupado}
        title="Agregar documento al monitoreo"
        description="Para un NIT puedes escribir el dígito de verificación o no; el sistema lo calcula y consulta las dos formas."
        footer={
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={() => setNuevo(false)}>
              Cancelar
            </button>
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !numero.trim()} onClick={crear}>
              {ocupado ? "Agregando…" : "Agregar"}
            </button>
          </div>
        }
      >
        <div className="flex flex-col gap-3">
          <label className={ETIQUETA_CLS}>
            Tipo de documento
            <select className={INPUT_CLS} value={tipo} onChange={(e) => setTipo(e.target.value)}>
              {TIPOS_DOCUMENTO.map((t) => (
                <option key={t}>{t}</option>
              ))}
            </select>
          </label>
          <label className={ETIQUETA_CLS}>
            Número
            <input className={INPUT_CLS} value={numero} onChange={(e) => setNumero(e.target.value)} placeholder={tipo === "NIT" ? "900.123.456-7" : ""} />
          </label>
          <label className={ETIQUETA_CLS}>
            Titular o razón social (opcional)
            <input className={INPUT_CLS} value={titular} onChange={(e) => setTitular(e.target.value)} />
          </label>
          {errorNuevo && <InlineAlert tone="error" compact>{errorNuevo}</InlineAlert>}
        </div>
      </Modal>

      <Confirmar
        abierto={cambiar !== null}
        ocupado={ocupado}
        titulo={cambiar?.activo ? "Desactivar documento" : "Activar documento"}
        texto={
          cambiar?.activo
            ? `${cambiar.numero} dejará de consultarse en las fuentes. Sus comparendos no se inactivan por esto.`
            : `${cambiar?.numero ?? ""} volverá a consultarse en el próximo ciclo.`
        }
        onSi={alternar}
        onNo={() => setCambiar(null)}
      />
      {historial && (
        <HistorialAuditoria
          entidad="identificacion_monitoreada"
          titulo={`Historial de ${historial.numero}`}
          nombre={() => historial.numero}
          ruta={`/monitoreo/nits/${historial.id}/historial`}
          query={{ empresa }}
          onCerrar={() => setHistorial(null)}
        />
      )}
    </div>
  );
}

function Placas({ empresa, puedeGestionar }: { empresa: string | null; puedeGestionar: boolean }) {
  const toast = useToast();
  const [buscar, setBuscar] = useState("");
  const [nuevo, setNuevo] = useState(false);
  const [texto, setTexto] = useState("");
  const [resultado, setResultado] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [cambiar, setCambiar] = useState<PlacaMonitoreada | null>(null);
  const [historial, setHistorial] = useState<PlacaMonitoreada | null>(null);
  const pg = usePaginacion(25);
  const lista = useCarga(`placas:${empresa}:${buscar}`, (signal) => apiFetch<PlacaMonitoreada[]>("/monitoreo/placas", { signal, query: { empresa, buscar } }));
  const filas = lista.datos ?? [];

  const crear = async () => {
    setOcupado(true);
    try {
      const placas = texto.split(/[\s,;]+/).filter(Boolean);
      const r = await apiFetch<{ agregadas: PlacaMonitoreada[]; yaExistian: string[]; invalidas: string[] }>("/monitoreo/placas", { method: "POST", body: { placas } });
      const partes = [`${r.agregadas.length} agregadas`];
      if (r.yaExistian.length) partes.push(`${r.yaExistian.length} ya estaban (${r.yaExistian.slice(0, 5).join(", ")}${r.yaExistian.length > 5 ? "…" : ""})`);
      if (r.invalidas.length) partes.push(`${r.invalidas.length} no son placas válidas (${r.invalidas.slice(0, 5).join(", ")}${r.invalidas.length > 5 ? "…" : ""})`);
      setResultado(partes.join(" · "));
      if (r.agregadas.length > 0) lista.recargar();
      setTexto("");
    } catch (e) {
      setResultado(e instanceof Error ? e.message : "No se pudieron agregar.");
    } finally {
      setOcupado(false);
    }
  };

  const alternar = async () => {
    if (!cambiar) return;
    setOcupado(true);
    try {
      await apiFetch(`/monitoreo/placas/${cambiar.id}`, { method: "PATCH", body: { activo: !cambiar.activo } });
      toast.show(cambiar.activo ? "Placa desactivada." : "Placa activada.", "success");
      setCambiar(null);
      lista.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cambiar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <SearchInput value={buscar} onChange={setBuscar} label="Buscar placa" placeholder="Buscar placa" />
        {puedeGestionar && (
          <button type="button" className={BOTON_PRIMARIO_CLS} onClick={() => { setNuevo(true); setResultado(null); }}>
            <Plus className="h-3.5 w-3.5" aria-hidden />
            Agregar placas
          </button>
        )}
      </div>
      <DataTable
        ariaLabel="Placas monitoreadas"
        rows={pg.paginar(filas)}
        getRowKey={(p) => p.id}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="Todavía no vigilas ninguna placa."
        columns={[
          { key: "placa", header: "Placa", render: (p) => <span className="font-semibold">{p.placa}</span> },
          { key: "estado", header: "Estado", render: (p) => <StatusBadge label={p.activo ? "Activa" : "Inactiva"} tone={activeTone(p.activo)} /> },
          { key: "origen", header: "Origen", render: (p) => (p.origenRegistro === "OCR" ? "Desde un documento OCR" : "Administrador") },
          { key: "creado", header: "Registrada", render: (p) => formatoFecha(p.creadoEn) },
          { key: "activos", header: "Comparendos activos", align: "right", render: (p) => p.comparendosActivos.toLocaleString("es-CO") },
          {
            key: "historial",
            header: "",
            render: (p) => (
              <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => setHistorial(p)}>
                Historial
              </button>
            ),
          },
          {
            key: "accion",
            header: "",
            align: "right",
            render: (p) =>
              puedeGestionar ? (
                <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => setCambiar(p)}>
                  {p.activo ? "Desactivar" : "Activar"}
                </button>
              ) : null,
          },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />

      <Modal
        open={nuevo}
        onClose={() => setNuevo(false)}
        busy={ocupado}
        title="Agregar placas"
        description="Escribe o pega las placas, una por línea o separadas por comas (hasta 5.000)."
        footer={
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={() => setNuevo(false)}>
              Cerrar
            </button>
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !texto.trim()} onClick={crear}>
              {ocupado ? "Agregando…" : "Agregar"}
            </button>
          </div>
        }
      >
        <div className="flex flex-col gap-3">
          <textarea className={TEXTAREA_CLS} rows={8} value={texto} onChange={(e) => setTexto(e.target.value)} placeholder={"ABC123\nXYZ987"} aria-label="Placas" />
          {resultado && <InlineAlert tone="info" compact>{resultado}</InlineAlert>}
        </div>
      </Modal>

      <Confirmar
        abierto={cambiar !== null}
        ocupado={ocupado}
        titulo={cambiar?.activo ? "Desactivar placa" : "Activar placa"}
        texto={cambiar?.activo ? `${cambiar.placa} dejará de consultarse. Sus comparendos no se inactivan por esto.` : `${cambiar?.placa ?? ""} volverá a consultarse.`}
        onSi={alternar}
        onNo={() => setCambiar(null)}
      />
      {historial && (
        <HistorialAuditoria
          entidad="placa_monitoreada"
          titulo={`Historial de ${historial.placa}`}
          nombre={() => historial.placa}
          ruta={`/monitoreo/placas/${historial.id}/historial`}
          query={{ empresa }}
          onCerrar={() => setHistorial(null)}
        />
      )}
    </div>
  );
}

/** NITs y placas monitoreadas de la empresa (HU #13463, §7.12). El SuperAdmin las ve eligiendo empresa, sin cambiarlas. */
export default function Monitoreo() {
  const { sesion } = useSesion();
  const [pestana, setPestana] = useState<"nits" | "placas">("nits");
  const [empresa, setEmpresa] = useState<string | null>(null);
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  const puedeGestionar = !esSuperAdmin && tienePermiso(sesion, "comparendos.monitoreo.gestionar");

  return (
    <Requiere permiso="comparendos.monitoreo.ver">
      <section className="flex flex-col gap-4">
        <Encabezado titulo="NITs y placas monitoreadas" descripcion="Lo que se consulta en SIMIT y en la fuente municipal en cada ciclo. Desactivar no borra nada." />
        {esSuperAdmin && <SelectorEmpresa valor={empresa} onCambio={setEmpresa} />}
        {(!esSuperAdmin || empresa) && (
          <>
            <div className="flex gap-2" role="tablist">
              <button type="button" role="tab" aria-selected={pestana === "nits"} className={controlCls(pestana === "nits")} onClick={() => setPestana("nits")}>
                NITs y documentos
              </button>
              <button type="button" role="tab" aria-selected={pestana === "placas"} className={controlCls(pestana === "placas")} onClick={() => setPestana("placas")}>
                Placas
              </button>
            </div>
            {pestana === "nits" ? <Nits empresa={esSuperAdmin ? empresa : null} puedeGestionar={puedeGestionar} /> : <Placas empresa={esSuperAdmin ? empresa : null} puedeGestionar={puedeGestionar} />}
          </>
        )}
      </section>
    </Requiere>
  );
}

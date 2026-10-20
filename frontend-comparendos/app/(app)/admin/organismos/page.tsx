"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { DataTable, InlineAlert, Modal, StatusBadge, usePaginacion } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SearchInput } from "@/components/SearchInput";
import { SelectorOrganismo } from "@/components/SelectorOrganismo";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import type { AliasOt, Organismo, Pagina } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

function EditarOrganismo({ ot, onCerrar, onGuardado }: { ot: Organismo; onCerrar: () => void; onGuardado: () => void }) {
  const [correos, setCorreos] = useState(ot.correosRadicacion.join("\n"));
  const [permite, setPermite] = useState(ot.permiteCambioContraventor);
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/admin/organismos/${ot.id}`, {
        method: "PATCH",
        body: { correosRadicacion: correos.split(/[\s,;]+/).filter(Boolean), permiteCambioContraventor: permite },
      });
      onGuardado();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={ocupado}
      title={ot.nombre}
      description={`Código ${ot.codigo}${ot.municipio ? ` · ${ot.municipio}, ${ot.departamento ?? ""}` : ""}`}
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
          Correos de notificaciones judiciales (radicación de DP y solicitudes)
          <textarea className={TEXTAREA_CLS} rows={4} value={correos} onChange={(e) => setCorreos(e.target.value)} placeholder="uno por línea" />
          <span className="font-normal opacity-70">Sin correo, no se pueden enviar derechos de petición ni solicitudes de cambio a este organismo.</span>
        </label>
        <label className="flex items-center gap-2 text-xs font-semibold">
          <input type="checkbox" checked={permite} onChange={(e) => setPermite(e.target.checked)} />
          Admite el cambio de contraventor
        </label>
        {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
      </div>
    </Modal>
  );
}

function Alias() {
  const toast = useToast();
  const lista = useCarga("alias", (signal) => apiFetch<AliasOt[]>("/admin/organismos/alias", { signal }));
  const [texto, setTexto] = useState("");
  const [ot, setOt] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  const crear = async () => {
    setOcupado(true);
    try {
      await apiFetch("/admin/organismos/alias", { method: "POST", body: { texto, organismoTransitoId: ot } });
      setTexto("");
      lista.recargar();
      toast.show("Alias creado. Los comparendos sin organismo con ese texto quedaron asociados.", "success");
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo crear el alias.", "error");
    } finally {
      setOcupado(false);
    }
  };

  const borrar = async (a: AliasOt) => {
    try {
      await apiFetch(`/admin/organismos/alias/${a.id}`, { method: "DELETE" });
      lista.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo borrar.", "error");
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-[#5B6B82] dark:text-slate-300">
        Un alias traduce el texto de secretaría que trae una fuente al organismo del catálogo. Sin organismo no se pueden enviar DP ni solicitudes de cambio.
      </p>
      <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-3">
        <label className={ETIQUETA_CLS}>
          Texto tal como lo trae la fuente
          <input className={INPUT_CLS} value={texto} onChange={(e) => setTexto(e.target.value)} />
        </label>
        <div className={ETIQUETA_CLS}>
          Organismo
          <SelectorOrganismo valor={ot} onCambio={(id) => setOt(id)} />
        </div>
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !texto.trim() || !ot} onClick={crear}>
          <Plus className="h-3.5 w-3.5" aria-hidden />
          Crear alias
        </button>
      </div>
      <DataTable
        ariaLabel="Alias de secretarías"
        rows={lista.datos ?? []}
        getRowKey={(a) => a.id}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="No hay alias todavía."
        columns={[
          { key: "texto", header: "Texto de la fuente", render: (a) => a.texto },
          { key: "ot", header: "Organismo", render: (a) => a.organismo },
          { key: "origen", header: "Origen", render: (a) => <StatusBadge label={a.origen === "AUTOMATICO" ? "Automático" : "Manual"} tone={a.origen === "AUTOMATICO" ? "info" : "neutral"} /> },
          {
            key: "borrar",
            header: "",
            align: "right",
            render: (a) => (
              <button type="button" aria-label={`Borrar alias ${a.texto}`} className="text-[#B42318] hover:opacity-70" onClick={() => borrar(a)}>
                <Trash2 className="h-4 w-4" />
              </button>
            ),
          },
        ]}
      />
    </div>
  );
}

/** Catálogo de organismos de tránsito (HU #13463): correos de radicación [Excel J6], cambio de contraventor y alias. */
export default function Organismos() {
  const toast = useToast();
  const [pestana, setPestana] = useState<"catalogo" | "alias">("catalogo");
  const [buscar, setBuscar] = useState("");
  const [sinCorreo, setSinCorreo] = useState(false);
  const [editar, setEditar] = useState<Organismo | null>(null);
  const pg = usePaginacion(25);
  const lista = useCarga(`ots:${buscar}:${sinCorreo}:${pg.page}:${pg.pageSize}`, (signal) =>
    apiFetch<Pagina<Organismo>>("/organismos", { signal, query: { buscar, sinCorreo: sinCorreo || undefined, pagina: pg.page, tamano: pg.pageSize } }),
  );

  return (
    <Requiere superAdmin>
      <section className="flex flex-col gap-4">
        <Encabezado titulo="Organismos de tránsito" descripcion="Catálogo del RUNT con los correos donde se radican los derechos de petición." />
        <div className="flex gap-2" role="tablist">
          <button type="button" role="tab" aria-selected={pestana === "catalogo"} className={controlCls(pestana === "catalogo")} onClick={() => setPestana("catalogo")}>
            Catálogo
          </button>
          <button type="button" role="tab" aria-selected={pestana === "alias"} className={controlCls(pestana === "alias")} onClick={() => setPestana("alias")}>
            Alias de secretarías
          </button>
        </div>
        {pestana === "alias" ? (
          <Alias />
        ) : (
          <>
            <div className="flex flex-wrap items-center gap-3">
              <SearchInput value={buscar} onChange={(v) => { setBuscar(v); pg.setPage(1); }} label="Buscar organismo" placeholder="Nombre, código o municipio" />
              <label className="flex items-center gap-2 text-xs font-semibold">
                <input type="checkbox" checked={sinCorreo} onChange={(e) => { setSinCorreo(e.target.checked); pg.setPage(1); }} />
                Solo los que no tienen correo
              </label>
            </div>
            <DataTable
              ariaLabel="Organismos de tránsito"
              rows={lista.datos?.items ?? []}
              getRowKey={(o) => o.id}
              status={lista.estado}
              errorMessage={lista.error ?? undefined}
              onRetry={lista.recargar}
              onRowClick={setEditar}
              emptyMessage="No hay organismos con ese criterio."
              columns={[
                { key: "nombre", header: "Organismo", render: (o) => <span className="font-semibold">{o.nombre}</span> },
                { key: "codigo", header: "Código", render: (o) => o.codigo },
                { key: "municipio", header: "Municipio", render: (o) => [o.municipio, o.departamento].filter(Boolean).join(", ") || "—" },
                { key: "correos", header: "Correos de radicación", render: (o) => (o.correosRadicacion.length ? o.correosRadicacion.join(", ") : <StatusBadge label="Sin correo" tone="warning" />) },
                { key: "cambio", header: "Cambio de contraventor", render: (o) => (o.permiteCambioContraventor ? "Permitido" : <StatusBadge label="No admite" tone="danger" />) },
              ]}
              pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: lista.datos?.total ?? 0, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
            />
          </>
        )}
        {editar && (
          <EditarOrganismo
            key={editar.id}
            ot={editar}
            onCerrar={() => setEditar(null)}
            onGuardado={() => {
              setEditar(null);
              lista.recargar();
              toast.show("Organismo actualizado.", "success");
            }}
          />
        )}
      </section>
    </Requiere>
  );
}

"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { DataTable, StatusBadge, activeTone } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorOrganismo } from "@/components/SelectorOrganismo";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import type { Municipio } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/** Fuentes municipales (HU #13463): catálogo global de municipios que se ingieren (RN-039). Solo SuperAdmin. */
export default function Municipios() {
  const toast = useToast();
  const lista = useCarga("municipios", (signal) => apiFetch<Municipio[]>("/admin/municipios", { signal }));
  const [codigo, setCodigo] = useState("");
  const [nombre, setNombre] = useState("");
  const [ot, setOt] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  const crear = async () => {
    setOcupado(true);
    try {
      await apiFetch("/admin/municipios", { method: "POST", body: { codigoFuente: codigo, nombre, organismoTransitoId: ot } });
      setCodigo("");
      setNombre("");
      setOt(null);
      lista.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo agregar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  const alternar = async (m: Municipio) => {
    try {
      await apiFetch(`/admin/municipios/${m.id}`, { method: "PATCH", body: { activo: !m.activo } });
      lista.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cambiar.", "error");
    }
  };

  return (
    <Requiere superAdmin>
      <section className="flex flex-col gap-4">
        <Encabezado titulo="Fuentes municipales" descripcion="Municipios de la API municipal que se consultan en cada ciclo. Es un catálogo general, no por empresa." />
        <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-4">
          <label className={ETIQUETA_CLS}>
            Código de la fuente
            <input className={INPUT_CLS} value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="Como lo devuelve la API (fuenteConsulta)" />
          </label>
          <label className={ETIQUETA_CLS}>
            Nombre
            <input className={INPUT_CLS} value={nombre} onChange={(e) => setNombre(e.target.value)} />
          </label>
          <div className={ETIQUETA_CLS}>
            Organismo asociado (opcional)
            <SelectorOrganismo valor={ot} onCambio={(id) => setOt(id)} />
          </div>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !codigo.trim() || !nombre.trim()} onClick={crear}>
            <Plus className="h-3.5 w-3.5" aria-hidden />
            Agregar
          </button>
        </div>
        <DataTable
          ariaLabel="Fuentes municipales"
          rows={lista.datos ?? []}
          getRowKey={(m) => m.id}
          status={lista.estado}
          errorMessage={lista.error ?? undefined}
          onRetry={lista.recargar}
          emptyMessage="Todavía no hay municipios configurados."
          columns={[
            { key: "codigo", header: "Código", render: (m) => <span className="font-mono">{m.codigoFuente}</span> },
            { key: "nombre", header: "Nombre", render: (m) => m.nombre },
            { key: "ot", header: "Organismo", render: (m) => (m.organismoTransitoId ? "Asociado" : "—") },
            { key: "estado", header: "Estado", render: (m) => <StatusBadge label={m.activo ? "Activo" : "Inactivo"} tone={activeTone(m.activo)} /> },
            {
              key: "accion",
              header: "",
              align: "right",
              render: (m) => (
                <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => alternar(m)}>
                  {m.activo ? "Desactivar" : "Activar"}
                </button>
              ),
            },
          ]}
        />
      </section>
    </Requiere>
  );
}

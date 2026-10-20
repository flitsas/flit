"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { DataTable, InlineAlert, StatusBadge } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { SelectorOrganismo } from "@/components/SelectorOrganismo";
import { useToast } from "@/components/Toast";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PRIMARIO_CLS, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { P } from "@/lib/nav";
import type { PermisoOt, Representante } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/**
 * «Admin de OTs bloqueados» (HU #13476, §7.14): por organismo de tránsito y representante, si se permite consultar el
 * contraventor por API y solicitar el cambio. Sin configuración, todo se permite: un OT solo se bloquea a propósito.
 */
export default function PermisosPorOrganismo() {
  const toast = useToast();
  const { sesion } = useSesion();
  const esSuper = !!sesion?.usuario.esSuperAdmin;
  const [empresa, setEmpresa] = useState<string | null>(null);
  const clave = esSuper && !empresa ? null : `permisos-ot:${empresa ?? "propia"}`;
  const lista = useCarga(clave, (signal) => apiFetch<PermisoOt[]>("/organismos/permisos", { signal, query: { empresa: empresa ?? undefined } }));
  const representantes = useCarga(clave ? `rl-ot:${empresa ?? "propia"}` : null, (signal) =>
    apiFetch<Representante[]>("/representantes", { signal, query: { empresa: empresa ?? undefined } }).catch(() => [] as Representante[]),
  );
  const [nuevoOt, setNuevoOt] = useState<string | null>(null);
  const [nuevoRl, setNuevoRl] = useState("");

  const guardar = async (organismoTransitoId: string, representanteLegalId: string | null, consulta: boolean, cambio: boolean) => {
    try {
      await apiFetch("/organismos/permisos", {
        method: "PUT",
        body: { organismoTransitoId, representanteLegalId, permiteConsultaContraventor: consulta, permiteCambioContraventor: cambio },
      });
      lista.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo guardar el permiso.", "error");
    }
  };

  return (
    <Requiere permiso={P.otsGestionar}>
      <section className="flex flex-col gap-4">
        <Encabezado
          titulo="Organismos de tránsito"
          descripcion="Bloquea la consulta o el cambio de contraventor ante un organismo, para cualquier representante o para uno en particular."
        />
        {esSuper && <SelectorEmpresa valor={empresa} onCambio={setEmpresa} />}
        {esSuper && empresa && <InlineAlert tone="info" compact>Como administrador de FLIT ves los permisos; los cambia la empresa.</InlineAlert>}
        {!esSuper && (
          <div className="grid grid-cols-1 items-end gap-3 md:grid-cols-[2fr_1fr_auto]">
            <div className={ETIQUETA_CLS}>
              Agregar un organismo
              <SelectorOrganismo valor={nuevoOt} onCambio={(id) => setNuevoOt(id)} />
            </div>
            <label className={ETIQUETA_CLS}>
              Representante
              <select className={INPUT_CLS} value={nuevoRl} onChange={(e) => setNuevoRl(e.target.value)}>
                <option value="">Cualquiera</option>
                {(representantes.datos ?? []).map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.nombres}
                  </option>
                ))}
              </select>
            </label>
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!nuevoOt} onClick={() => nuevoOt && guardar(nuevoOt, nuevoRl || null, true, false).then(() => setNuevoOt(null))}>
              <Plus className="h-3.5 w-3.5" aria-hidden />
              Bloquear cambio
            </button>
          </div>
        )}
        {clave === null ? (
          <p className="text-sm opacity-70">Elige una empresa para ver sus permisos.</p>
        ) : (
          <DataTable
            ariaLabel="Permisos por organismo"
            rows={lista.datos ?? []}
            getRowKey={(p) => `${p.organismoTransitoId}:${p.representanteLegalId ?? "todos"}`}
            status={lista.estado}
            errorMessage={lista.error ?? undefined}
            onRetry={lista.recargar}
            emptyMessage="La empresa todavía no tiene comparendos con organismo asignado ni permisos configurados."
            columns={[
              { key: "ot", header: "Organismo", render: (p) => <span className="font-semibold">{p.organismo}</span> },
              { key: "municipio", header: "Municipio", render: (p) => p.municipio ?? "—" },
              { key: "rl", header: "Representante", render: (p) => p.representante ?? "Cualquiera" },
              { key: "activos", header: "Comparendos activos", align: "right", render: (p) => p.comparendosActivos },
              {
                key: "consulta",
                header: "Consultar contraventor",
                render: (p) => (
                  <Interruptor activo={p.permiteConsultaContraventor} deshabilitado={esSuper} etiqueta={p.permiteConsultaContraventor ? "Permitido" : "Bloqueado"}
                    onCambio={(v) => guardar(p.organismoTransitoId, p.representanteLegalId, v, p.permiteCambioContraventor)} />
                ),
              },
              {
                key: "cambio",
                header: "Cambio de contraventor",
                render: (p) =>
                  p.cambioBloqueadoGlobal ? (
                    <StatusBadge label="El organismo no lo admite" tone="danger" title="Bloqueo global del catálogo de organismos" />
                  ) : (
                    <Interruptor activo={p.permiteCambioContraventor} deshabilitado={esSuper} etiqueta={p.permiteCambioContraventor ? "Permitido" : "Bloqueado"}
                      onCambio={(v) => guardar(p.organismoTransitoId, p.representanteLegalId, p.permiteConsultaContraventor, v)} />
                  ),
              },
            ]}
          />
        )}
      </section>
    </Requiere>
  );
}

"use client";

import { useRef, useState } from "react";
import { Plus, Upload } from "lucide-react";
import { DataTable, InlineAlert, Modal, StatusBadge, activeTone } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { formatoFecha } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";
import type { Nit, Representante } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const TIPOS_DOCUMENTO = ["CC", "CE", "PA", "NIT", "TI"];

const DOCUMENTOS: { tipo: "firma" | "cedula" | "certificado" | "escritura"; texto: string; acepta: string; campo: keyof Representante }[] = [
  { tipo: "firma", texto: "Firma (PNG o JPG)", acepta: "image/png,image/jpeg", campo: "firmaArchivoId" },
  { tipo: "cedula", texto: "Cédula", acepta: "application/pdf,image/png,image/jpeg", campo: "cedulaArchivoId" },
  { tipo: "certificado", texto: "Certificado de representación", acepta: "application/pdf,image/png,image/jpeg", campo: "certificadoArchivoId" },
  { tipo: "escritura", texto: "Escritura o poder", acepta: "application/pdf,image/png,image/jpeg", campo: "escrituraArchivoId" },
];

interface FormRl {
  alcance: string;
  identificacionMonitoreadaId: string | null;
  tipoDocumento: string;
  numeroDocumento: string;
  nombres: string;
  cargo: string;
  correo: string;
  telefono: string;
  direccionNotificacion: string;
  fechaExpedicionCertificado: string;
  permisoEnvioDp: boolean;
  permisoCambioContraventor: boolean;
  activo: boolean;
}

function aForm(r: Representante | null): FormRl {
  return {
    alcance: r?.alcance ?? "Empresa",
    identificacionMonitoreadaId: r?.identificacionMonitoreadaId ?? null,
    tipoDocumento: r?.tipoDocumento ?? "CC",
    numeroDocumento: r?.numeroDocumento ?? "",
    nombres: r?.nombres ?? "",
    cargo: r?.cargo ?? "Representante legal",
    correo: r?.correo ?? "",
    telefono: r?.telefono ?? "",
    direccionNotificacion: r?.direccionNotificacion ?? "",
    fechaExpedicionCertificado: r?.fechaExpedicionCertificado ?? "",
    permisoEnvioDp: r?.permisoEnvioDp ?? true,
    permisoCambioContraventor: r?.permisoCambioContraventor ?? true,
    activo: r?.activo ?? true,
  };
}

function SubirDocumento({ rl, doc, onSubido }: { rl: Representante; doc: (typeof DOCUMENTOS)[number]; onSubido: () => void }) {
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [ocupado, setOcupado] = useState(false);
  const archivoId = rl[doc.campo] as string | null;

  const subir = async (archivo: File) => {
    if (archivo.size > 10 * 1024 * 1024) {
      toast.show("El archivo supera 10 MB.", "error");
      return;
    }
    setOcupado(true);
    try {
      const form = new FormData();
      form.append("archivo", archivo);
      await apiFetch(`/representantes/${rl.id}/documentos/${doc.tipo}`, { method: "POST", body: form });
      onSubido();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cargar el archivo.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <div className="flex items-center justify-between gap-2 rounded-xl border border-[#DFE5ED] px-3 py-2 dark:border-white/15">
      <span className="text-xs font-semibold">{doc.texto}</span>
      <div className="flex items-center gap-2">
        {archivoId ? (
          <button type="button" className="text-xs text-[#3B4FD6] hover:underline" onClick={() => apiDownload(`/archivos/${archivoId}`, doc.tipo).catch(() => toast.show("No se pudo descargar.", "error"))}>
            Ver
          </button>
        ) : (
          <span className="text-[11px] opacity-60">Sin cargar</span>
        )}
        <input ref={input} type="file" accept={doc.acepta} className="hidden" onChange={(e) => e.target.files?.[0] && subir(e.target.files[0])} />
        <button type="button" className={controlCls(false)} disabled={ocupado} onClick={() => input.current?.click()}>
          <Upload className="h-3.5 w-3.5" aria-hidden />
          {ocupado ? "Cargando…" : archivoId ? "Reemplazar" : "Cargar"}
        </button>
      </div>
    </div>
  );
}

function Formulario({ rl, nits, onCerrar, onGuardado }: { rl: Representante | null; nits: Nit[]; onCerrar: () => void; onGuardado: () => void }) {
  const [f, setF] = useState<FormRl>(aForm(rl));
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const valido = f.numeroDocumento.trim() && f.nombres.trim() && (f.alcance === "Empresa" || f.identificacionMonitoreadaId);

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      const body = {
        ...f,
        identificacionMonitoreadaId: f.alcance === "Identificacion" ? f.identificacionMonitoreadaId : null,
        fechaExpedicionCertificado: f.fechaExpedicionCertificado || null,
        correo: f.correo.trim() || null,
      };
      await apiFetch(rl ? `/representantes/${rl.id}` : "/representantes", { method: rl ? "PUT" : "POST", body });
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
      size="lg"
      title={rl ? rl.nombres : "Nuevo representante legal"}
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!valido || ocupado} onClick={guardar}>
            {ocupado ? "Guardando…" : "Guardar"}
          </button>
        </div>
      }
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <label className={ETIQUETA_CLS}>
          Representa a
          <select className={INPUT_CLS} value={f.alcance} onChange={(e) => setF({ ...f, alcance: e.target.value })}>
            <option value="Empresa">La empresa</option>
            <option value="Identificacion">Un NIT monitoreado</option>
          </select>
        </label>
        {f.alcance === "Identificacion" && (
          <label className={`${ETIQUETA_CLS} md:col-span-2`}>
            NIT
            <select className={INPUT_CLS} value={f.identificacionMonitoreadaId ?? ""} onChange={(e) => setF({ ...f, identificacionMonitoreadaId: e.target.value || null })}>
              <option value="">Elige el NIT…</option>
              {nits.map((n) => (
                <option key={n.id} value={n.id}>
                  {n.numero} {n.nombreTitular ? `· ${n.nombreTitular}` : ""}
                </option>
              ))}
            </select>
          </label>
        )}
        <label className={ETIQUETA_CLS}>
          Tipo de documento
          <select className={INPUT_CLS} value={f.tipoDocumento} onChange={(e) => setF({ ...f, tipoDocumento: e.target.value })}>
            {TIPOS_DOCUMENTO.map((t) => <option key={t}>{t}</option>)}
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Número de documento
          <input className={INPUT_CLS} value={f.numeroDocumento} maxLength={20} onChange={(e) => setF({ ...f, numeroDocumento: e.target.value })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Nombre completo
          <input className={INPUT_CLS} value={f.nombres} onChange={(e) => setF({ ...f, nombres: e.target.value })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Cargo
          <input className={INPUT_CLS} value={f.cargo} onChange={(e) => setF({ ...f, cargo: e.target.value })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Correo
          <input className={INPUT_CLS} value={f.correo} onChange={(e) => setF({ ...f, correo: e.target.value })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Teléfono
          <input className={INPUT_CLS} value={f.telefono} onChange={(e) => setF({ ...f, telefono: e.target.value })} />
        </label>
        <label className={`${ETIQUETA_CLS} md:col-span-2`}>
          Dirección de notificación (va en el DP)
          <input className={INPUT_CLS} value={f.direccionNotificacion} onChange={(e) => setF({ ...f, direccionNotificacion: e.target.value })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Expedición del certificado
          <input className={INPUT_CLS} type="date" value={f.fechaExpedicionCertificado} onChange={(e) => setF({ ...f, fechaExpedicionCertificado: e.target.value })} />
        </label>
        <Interruptor activo={f.permisoEnvioDp} onCambio={(v) => setF({ ...f, permisoEnvioDp: v })} etiqueta="Firma derechos de petición" />
        <Interruptor activo={f.permisoCambioContraventor} onCambio={(v) => setF({ ...f, permisoCambioContraventor: v })} etiqueta="Firma cambios de contraventor" />
        <Interruptor activo={f.activo} onCambio={(v) => setF({ ...f, activo: v })} etiqueta="Activo" ayuda="Un solo activo por empresa y uno por NIT." />
      </div>
      {error && <InlineAlert tone="error" compact className="mt-3">{error}</InlineAlert>}
    </Modal>
  );
}

/** Representantes legales (HU #13476, §7.13): quién firma los DP y las solicitudes de cambio [Excel J7, P6, P10]. */
export default function Representantes() {
  const toast = useToast();
  const { sesion } = useSesion();
  const esSuper = !!sesion?.usuario.esSuperAdmin;
  const puedeGestionar = !esSuper && tienePermiso(sesion, P.representantesGestionar);
  const [empresa, setEmpresa] = useState<string | null>(null);
  const clave = esSuper && !empresa ? null : `rl:${empresa ?? "propia"}`;
  const lista = useCarga(clave, (signal) => apiFetch<Representante[]>("/representantes", { signal, query: { empresa: empresa ?? undefined } }));
  const nits = useCarga(clave ? `nits:${empresa ?? "propia"}` : null, (signal) => apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa: empresa ?? undefined } }).catch(() => [] as Nit[]));
  const [editar, setEditar] = useState<Representante | "nuevo" | null>(null);
  const [documentos, setDocumentos] = useState<string | null>(null);
  const rlDocumentos = lista.datos?.find((r) => r.id === documentos) ?? null;

  return (
    <Requiere permiso={P.representantesVer}>
      <section className="flex flex-col gap-4">
        <Encabezado
          titulo="Representantes legales"
          descripcion="Firman los derechos de petición y las solicitudes de cambio de contraventor. El de un NIT gana sobre el de la empresa."
          acciones={
            puedeGestionar && (
              <button type="button" className={BOTON_PRIMARIO_CLS} onClick={() => setEditar("nuevo")}>
                <Plus className="h-3.5 w-3.5" aria-hidden />
                Nuevo representante
              </button>
            )
          }
        />
        {esSuper && <SelectorEmpresa valor={empresa} onCambio={setEmpresa} />}
        {clave === null ? (
          <p className="text-sm opacity-70">Elige una empresa para ver sus representantes.</p>
        ) : (
          <DataTable
            ariaLabel="Representantes legales"
            rows={lista.datos ?? []}
            getRowKey={(r) => r.id}
            status={lista.estado}
            errorMessage={lista.error ?? undefined}
            onRetry={lista.recargar}
            onRowClick={puedeGestionar ? setEditar : undefined}
            emptyMessage="No hay representantes. Sin uno activo, los DP de una empresa que lo exige no se envían."
            columns={[
              { key: "nombre", header: "Nombre", render: (r) => <span className="font-semibold">{r.nombres}</span> },
              { key: "alcance", header: "Representa", render: (r) => (r.alcance === "Empresa" ? "La empresa" : `NIT ${r.nit ?? ""}`) },
              { key: "documento", header: "Documento", render: (r) => `${r.tipoDocumento} ${r.numeroDocumento}` },
              { key: "cargo", header: "Cargo", render: (r) => r.cargo ?? "—" },
              {
                key: "permisos",
                header: "Firma",
                render: (r) => [r.permisoEnvioDp && "DP", r.permisoCambioContraventor && "Cambio de contraventor"].filter(Boolean).join(" · ") || "—",
              },
              {
                key: "certificado",
                header: "Certificado",
                render: (r) =>
                  r.fechaExpedicionCertificado ? (
                    <span className="flex items-center gap-1">
                      {formatoFecha(r.fechaExpedicionCertificado)}
                      {r.certificadoVencido && <StatusBadge label="Vencido" tone="warning" title="Aviso informativo: la vigencia no bloquea el envío" />}
                    </span>
                  ) : (
                    "—"
                  ),
              },
              { key: "firma", header: "Firma cargada", render: (r) => (r.firmaArchivoId ? "Sí" : <StatusBadge label="Falta" tone="warning" />) },
              { key: "estado", header: "Estado", render: (r) => <StatusBadge label={r.activo ? "Activo" : "Inactivo"} tone={activeTone(r.activo)} /> },
              {
                key: "docs",
                header: "",
                align: "right",
                render: (r) => (
                  <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={(e) => { e.stopPropagation(); setDocumentos(r.id); }}>
                    Documentos
                  </button>
                ),
              },
            ]}
          />
        )}
        {editar && (
          <Formulario
            key={editar === "nuevo" ? "nuevo" : editar.id}
            rl={editar === "nuevo" ? null : editar}
            nits={nits.datos ?? []}
            onCerrar={() => setEditar(null)}
            onGuardado={() => {
              setEditar(null);
              lista.recargar();
              toast.show("Representante guardado.", "success");
            }}
          />
        )}
        {rlDocumentos && (
          <Modal open onClose={() => setDocumentos(null)} title={`Documentos de ${rlDocumentos.nombres}`} size="md">
            <div className="flex flex-col gap-2">
              {DOCUMENTOS.map((d) =>
                puedeGestionar ? (
                  <SubirDocumento key={d.tipo} rl={rlDocumentos} doc={d} onSubido={lista.recargar} />
                ) : (
                  <div key={d.tipo} className="flex justify-between text-xs">
                    <span>{d.texto}</span>
                    <span>{rlDocumentos[d.campo] ? "Cargado" : "Sin cargar"}</span>
                  </div>
                ),
              )}
              <p className="text-[11px] opacity-70">La firma se recomienda en PNG con fondo transparente. PDF, JPG o PNG de hasta 10 MB para los demás.</p>
            </div>
          </Modal>
        )}
      </section>
    </Requiere>
  );
}

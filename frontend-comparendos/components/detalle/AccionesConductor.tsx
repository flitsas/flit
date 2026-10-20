"use client";

import { useRef, useState } from "react";
import { RefreshCw, Upload, UserRoundPen } from "lucide-react";
import { InlineAlert, Modal } from "@flit/ui";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { armarAsignacion, DECLARACION_LEY_1581, erroresAsignacion, type FormConductor } from "@/lib/conductor";
import { P } from "@/lib/nav";
import { capacidades, tienePermiso } from "@/lib/sesion";
import type { Contraventor } from "@/lib/tipos";

const TIPOS_DOCUMENTO = ["CC", "CE", "PA", "TI", "NIT"];

function vacio(c: Contraventor | null): FormConductor {
  return {
    tipoDocumento: c?.tipoDocumento ?? "CC",
    numeroDocumento: c?.numeroDocumento ?? "",
    nombres: c?.nombres ?? "",
    correo: c?.correo ?? "",
    telefono: c?.telefono ?? "",
    direccion: c?.direccion ?? "",
    ciudad: c?.ciudad ?? "",
    sucursal: c?.sucursal ?? "",
    tipoContrato: c?.tipoContrato ?? "",
    motivo: "",
    declaracion: false,
  };
}

function ModalConductor({ ctx, onCerrar }: { ctx: ContextoDetalle; onCerrar: () => void }) {
  const toast = useToast();
  const actual = ctx.detalle.contraventor;
  const [f, setF] = useState<FormConductor>(vacio(actual));
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const errores = erroresAsignacion(f, actual !== null);
  const campo = (k: keyof FormConductor, etiqueta: string, extra?: { tipo?: string; ancho?: string }) => (
    <label className={`${ETIQUETA_CLS} ${extra?.ancho ?? ""}`}>
      {etiqueta}
      <input className={INPUT_CLS} type={extra?.tipo ?? "text"} value={f[k] as string} onChange={(e) => setF({ ...f, [k]: e.target.value })} />
    </label>
  );

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/comparendos/${ctx.detalle.id}/contraventor`, { method: "PUT", body: armarAsignacion(f) });
      toast.show(actual ? "Conductor reemplazado." : "Conductor asignado.", "success");
      ctx.recargar();
      onCerrar();
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
      title={actual ? "Cambiar conductor" : "Asignar conductor"}
      description={actual ? `Conductor actual: ${actual.nombres} (${actual.origen === "Api" ? "API" : "manual"}).` : "Quién tenía el vehículo en la fecha de la infracción."}
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || errores.length > 0} onClick={guardar}>
            {ocupado ? "Guardando…" : "Guardar"}
          </button>
        </div>
      }
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <label className={ETIQUETA_CLS}>
          Tipo de documento
          <select className={INPUT_CLS} value={f.tipoDocumento} onChange={(e) => setF({ ...f, tipoDocumento: e.target.value })}>
            {TIPOS_DOCUMENTO.map((t) => <option key={t}>{t}</option>)}
          </select>
        </label>
        {campo("numeroDocumento", "Número de documento")}
        {campo("nombres", "Nombre completo")}
        {campo("correo", "Correo (sin correo no salen avisos)", { tipo: "email" })}
        {campo("telefono", "Teléfono")}
        {campo("direccion", "Dirección")}
        {campo("ciudad", "Ciudad")}
        {campo("sucursal", "Sucursal")}
        {campo("tipoContrato", "Tipo de contrato")}
        {actual && (
          <label className={`${ETIQUETA_CLS} md:col-span-3`}>
            Motivo del cambio (obligatorio)
            <textarea className={TEXTAREA_CLS} rows={2} value={f.motivo} onChange={(e) => setF({ ...f, motivo: e.target.value })} />
          </label>
        )}
        <label className="flex items-start gap-2 text-xs md:col-span-3">
          <input type="checkbox" className="mt-0.5" checked={f.declaracion} onChange={(e) => setF({ ...f, declaracion: e.target.checked })} />
          <span>{DECLARACION_LEY_1581}</span>
        </label>
      </div>
      {errores.length > 0 && <InlineAlert tone="warning" compact className="mt-3">{errores.join(" ")}</InlineAlert>}
      {error && <InlineAlert tone="error" compact className="mt-3">{error}</InlineAlert>}
    </Modal>
  );
}

function CargarDocumento({ ctx }: { ctx: ContextoDetalle }) {
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [tipo, setTipo] = useState("Cedula");
  const [ocupado, setOcupado] = useState(false);

  const subir = async (archivo: File) => {
    if (archivo.size > 10 * 1024 * 1024) {
      toast.show("El archivo supera 10 MB.", "error");
      return;
    }
    setOcupado(true);
    try {
      const form = new FormData();
      form.append("archivo", archivo);
      await apiFetch(`/comparendos/${ctx.detalle.id}/documentos`, { method: "POST", body: form, query: { tipo } });
      toast.show("Documento cargado.", "success");
      ctx.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cargar el documento.", "error");
    } finally {
      setOcupado(false);
      if (input.current) input.current.value = "";
    }
  };

  return (
    <div className="flex items-center gap-2">
      <select className={`${INPUT_CLS} w-32`} value={tipo} onChange={(e) => setTipo(e.target.value)} aria-label="Tipo de documento a cargar">
        <option value="Cedula">Cédula</option>
        <option value="Licencia">Licencia</option>
        <option value="Otro">Otro</option>
      </select>
      <input ref={input} type="file" accept="application/pdf,image/jpeg,image/png" className="hidden" onChange={(e) => e.target.files?.[0] && subir(e.target.files[0])} />
      <button type="button" className={controlCls(false)} disabled={ocupado} onClick={() => input.current?.click()}>
        <Upload className="h-3.5 w-3.5" aria-hidden />
        {ocupado ? "Cargando…" : "Cargar documento"}
      </button>
    </div>
  );
}

/**
 * El conductor que trajo la API de contratos llega sin documento (HU #13712): se completa aquí sin reemplazarlo, así no se
 * revocan sus enlaces del portal ni se reinician sus avisos. Sin documento no se solicita el cambio de contraventor.
 */
function CompletarDocumento({ ctx }: { ctx: ContextoDetalle }) {
  const toast = useToast();
  const [tipo, setTipo] = useState("CC");
  const [numero, setNumero] = useState("");
  const [ocupado, setOcupado] = useState(false);
  const valido = numero.trim().length > 0 && numero.trim().length <= 20;

  const guardar = async () => {
    setOcupado(true);
    try {
      await apiFetch(`/comparendos/${ctx.detalle.id}/contraventor/documento`, {
        method: "PUT",
        body: { tipoDocumento: tipo, numeroDocumento: numero.trim() },
      });
      toast.show("Documento del conductor completado.", "success");
      ctx.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo guardar el documento.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <InlineAlert tone="warning" title="Falta el documento del conductor">
      <p className="mb-2 text-xs">
        La API de contratos trae el nombre y el correo, pero no el documento. Complétalo para poder solicitar el cambio de
        contraventor.
      </p>
      <div className="flex flex-wrap items-end gap-2">
        <label className={ETIQUETA_CLS}>
          Tipo
          <select className={`${INPUT_CLS} w-24`} value={tipo} onChange={(e) => setTipo(e.target.value)}>
            {TIPOS_DOCUMENTO.map((t) => <option key={t}>{t}</option>)}
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Número
          <input className={INPUT_CLS} value={numero} onChange={(e) => setNumero(e.target.value)} />
        </label>
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!valido || ocupado} onClick={guardar}>
          {ocupado ? "Guardando…" : "Guardar documento"}
        </button>
      </div>
    </InlineAlert>
  );
}

/**
 * Acciones del conductor dentro del acordeón Contraventor (HU #13481, §7.4): asignar o cambiar, consultar la API de
 * contratos (si la empresa la tiene), cargar documentos y marcar que ya fue notificado en persona.
 */
export function AccionesConductor({ ctx }: { ctx: ContextoDetalle }) {
  const toast = useToast();
  const [modal, setModal] = useState(false);
  const [consultando, setConsultando] = useState(false);
  const [confirmar, setConfirmar] = useState<{ nombres: string; numeroDocumento: string | null } | null>(null);
  const d = ctx.detalle;
  const puedeAsignar = tienePermiso(ctx.sesion, P.contraventorAsignar);
  const puedeConsultar = tienePermiso(ctx.sesion, P.contraventorConsultar) && capacidades(ctx.sesion).consultaContraventor;
  const puedeDocumentos = tienePermiso(ctx.sesion, P.contraventorDocumentos);
  if (!d.activo) return null;

  const consultar = async (confirmarReemplazo = false) => {
    setConsultando(true);
    try {
      const r = await apiFetch<{ resultado: string; mensaje: string | null; conductor: { nombres: string; numeroDocumento: string | null } | null; requiereConfirmarReemplazo: boolean }>(
        `/comparendos/${d.id}/contraventor/consultar`,
        { method: "POST", query: { confirmarReemplazo: confirmarReemplazo || undefined } },
      );
      if (r.requiereConfirmarReemplazo && r.conductor) {
        setConfirmar(r.conductor);
        return;
      }
      setConfirmar(null);
      toast.show(
        r.resultado === "Encontrado" ? "La API de contratos encontró el conductor." : r.resultado === "NoEncontrado" ? "La API no encontró conductor: asígnalo a mano." : r.mensaje ?? "La consulta falló.",
        r.resultado === "Error" ? "error" : r.resultado === "NoEncontrado" ? "warning" : "success",
      );
      ctx.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo consultar.", "error");
    } finally {
      setConsultando(false);
    }
  };

  const notificado = async (valor: boolean) => {
    try {
      await apiFetch(`/comparendos/${d.id}/notificado-fisicamente`, { method: "PUT", body: { valor } });
      ctx.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo cambiar.", "error");
    }
  };

  return (
    <div className="flex flex-col gap-3 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
      <div className="flex flex-wrap items-center gap-2">
        {puedeAsignar && (
          <button type="button" className={controlCls(false)} onClick={() => setModal(true)}>
            <UserRoundPen className="h-3.5 w-3.5" aria-hidden />
            {d.contraventor ? "Cambiar conductor" : "Asignar conductor"}
          </button>
        )}
        {puedeConsultar && (
          <button type="button" className={controlCls(false)} disabled={consultando} onClick={() => consultar()}>
            <RefreshCw className="h-3.5 w-3.5" aria-hidden />
            {consultando ? "Consultando…" : "Consultar API"}
          </button>
        )}
        {puedeDocumentos && <CargarDocumento ctx={ctx} />}
      </div>
      {puedeAsignar && d.contraventor?.documentoPendiente && <CompletarDocumento ctx={ctx} />}
      {puedeAsignar && (
        <Interruptor activo={d.notificadoFisicamente} onCambio={notificado} etiqueta="Notificado en persona" ayuda="El conductor ya conoce el comparendo: no recibe avisos automáticos." />
      )}
      {confirmar && (
        <InlineAlert
          tone="warning"
          title="La API encontró otro conductor"
          action={
            <div className="flex gap-2">
              <button type="button" className={controlCls(false)} onClick={() => setConfirmar(null)}>
                Mantener el actual
              </button>
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={consultando} onClick={() => consultar(true)}>
                Reemplazar
              </button>
            </div>
          }
        >
          {confirmar.nombres}
          {confirmar.numeroDocumento ? ` (${confirmar.numeroDocumento})` : ""}. ¿Reemplazar al conductor actual?
        </InlineAlert>
      )}
      {modal && <ModalConductor ctx={ctx} onCerrar={() => setModal(false)} />}
    </div>
  );
}

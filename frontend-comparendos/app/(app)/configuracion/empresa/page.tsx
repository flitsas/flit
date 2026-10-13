"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { InlineAlert, StatusBadge } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { ConsultasEmpresa } from "@/components/consultas/ConsultasAdmin";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { MultiSeleccion } from "@/components/ui/MultiSeleccion";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { partirCorreos } from "@/lib/correos";
import { opciones, TIPO_COMUNICACION } from "@/lib/etiquetas";
import { P } from "@/lib/nav";
import { limpiarCopias } from "@/lib/parametrizador";
import { tienePermiso } from "@/lib/sesion";
import type { ConfiguracionEmpresa, Copia } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const TIPOS_COPIA = opciones(TIPO_COMUNICACION).filter((o) => !o.valor.startsWith("Alerta") && o.valor !== "ExportacionLista");

function Copias({ titulo, ayuda, valor, onCambio, soloLectura }: { titulo: string; ayuda: string; valor: Copia[]; onCambio: (v: Copia[]) => void; soloLectura: boolean }) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="text-sm font-semibold">{titulo}</legend>
      <p className="text-[11px] text-[#5B6B82] dark:text-slate-300">{ayuda}</p>
      {valor.map((c, i) => (
        <div key={i} className="grid grid-cols-1 items-center gap-2 md:grid-cols-[1fr_1fr_auto]">
          <input className={INPUT_CLS} value={c.correo} disabled={soloLectura} aria-label={`${titulo}: correo ${i + 1}`} placeholder="correo@empresa.com"
            onChange={(e) => onCambio(valor.map((x, j) => (j === i ? { ...x, correo: e.target.value } : x)))} />
          {soloLectura ? (
            <span className="text-xs">{c.tipos.length ? c.tipos.map((t) => TIPO_COMUNICACION[t] ?? t).join(", ") : "Todos los envíos"}</span>
          ) : (
            <MultiSeleccion etiqueta={c.tipos.length ? "Envíos" : "Todos los envíos"} opciones={TIPOS_COPIA} valores={c.tipos}
              onCambio={(tipos) => onCambio(valor.map((x, j) => (j === i ? { ...x, tipos } : x)))} />
          )}
          {!soloLectura && (
            <button type="button" aria-label="Quitar" className="text-[#B42318] hover:opacity-70" onClick={() => onCambio(valor.filter((_, j) => j !== i))}>
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>
      ))}
      {!soloLectura && (
        <button type="button" className={`${controlCls(false)} w-fit`} onClick={() => onCambio([...valor, { correo: "", tipos: [] }])}>
          <Plus className="h-3.5 w-3.5" aria-hidden />
          Agregar correo
        </button>
      )}
    </fieldset>
  );
}

interface FormCorreo {
  canalCorreo: string;
  remitenteNombre: string;
  correoRespuesta: string;
  cc: Copia[];
  bcc: Copia[];
  destinatariosReportes: string;
  correosAlertaOperativa: string;
}

function aForm(c: ConfiguracionEmpresa): FormCorreo {
  return {
    canalCorreo: c.canalCorreo,
    remitenteNombre: c.remitenteNombre ?? "",
    correoRespuesta: c.correoRespuesta ?? "",
    cc: c.cc,
    bcc: c.bcc,
    destinatariosReportes: c.destinatariosReportes.join("\n"),
    correosAlertaOperativa: c.correosAlertaOperativa.join("\n"),
  };
}

function Configuracion({ empresa }: { empresa: string | null }) {
  const toast = useToast();
  const { sesion } = useSesion();
  const esSuper = !!sesion?.usuario.esSuperAdmin;
  const puedeEditar = tienePermiso(sesion, P.configuracionEditar);
  const ruta = esSuper ? (empresa ? `/admin/empresas/${empresa}/configuracion` : null) : "/configuracion";
  const config = useCarga(ruta, (signal) => apiFetch<ConfiguracionEmpresa>(ruta!, { signal }));
  const [form, setForm] = useState<{ base: string; valores: FormCorreo } | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!ruta) return <p className="text-sm opacity-70">Elige una empresa para ver su configuración.</p>;
  if (config.estado === "loading") return <p className="text-sm opacity-60">Cargando…</p>;
  if (config.estado === "error" || !config.datos) return <InlineAlert tone="error">{config.error ?? "No se pudo cargar."}</InlineAlert>;

  const c = config.datos;
  const base = aForm(c);
  const clave = JSON.stringify(base);
  const f = form && form.base === clave ? form.valores : base;
  const cambiar = (parcial: Partial<FormCorreo>) => setForm({ base: clave, valores: { ...f, ...parcial } });
  const reportes = partirCorreos(f.destinatariosReportes);
  const alertas = partirCorreos(f.correosAlertaOperativa);
  const invalidos = [...reportes.invalidos, ...alertas.invalidos];

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      const body = {
        canalCorreo: f.canalCorreo,
        remitenteNombre: f.remitenteNombre.trim() || null,
        correoRespuesta: f.correoRespuesta.trim() || null,
        cc: limpiarCopias(f.cc),
        bcc: limpiarCopias(f.bcc),
        destinatariosReportes: reportes.validos,
        correosAlertaOperativa: alertas.validos,
      };
      await apiFetch(esSuper ? `/admin/empresas/${empresa}/correo` : "/configuracion/correo", { method: "PUT", body });
      setForm(null);
      config.recargar();
      toast.show("Configuración de correo guardada.", "success");
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar.");
    } finally {
      setOcupado(false);
    }
  };

  const soloLectura = !esSuper && !puedeEditar;
  const sucio = JSON.stringify(f) !== clave;

  return (
    <div className="flex flex-col gap-6">
      <section className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold">Lo que define el administrador de FLIT</h2>
        <div className="flex flex-wrap gap-2">
          {[
            ["Derechos de petición", c.hasRightPetitionModule],
            ["Carga por OCR", c.hasOcrAccess],
            ["Consulta de contraventor", c.allowsContravenerConsultation],
            ["Cambio de contraventor", c.allowsContravenerChange],
            ["Reportes", c.hasReportesModule],
          ].map(([t, v]) => (
            <StatusBadge key={t as string} label={`${t as string}: ${v ? "sí" : "no"}`} tone={v ? "success" : "neutral"} />
          ))}
          <StatusBadge label={`Conductor: ${c.tipoIntegracionConductor === "ApiRenting" ? "API de Renting" : "manual"}`} tone="info" />
          <StatusBadge label={`Plantillas: ${c.conjuntoPlantillas === "Renting" ? "Renting" : "FLIT"}`} tone="info" />
          <StatusBadge label={c.requiereRepresentanteLegal ? "Exige representante legal" : "No exige representante legal"} tone="info" />
        </div>
      </section>

      <section className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <label className={ETIQUETA_CLS}>
          Canal de correo
          <select className={INPUT_CLS} value={f.canalCorreo} disabled={soloLectura} onChange={(e) => cambiar({ canalCorreo: e.target.value })}>
            <option value="FlitSmtp">Cuenta de FLIT</option>
            <option value="EmpresaApi">Cuenta propia de la empresa (API)</option>
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Nombre del remitente
          <input className={INPUT_CLS} value={f.remitenteNombre} maxLength={120} disabled={soloLectura} onChange={(e) => cambiar({ remitenteNombre: e.target.value })} placeholder="Si se deja vacío, la razón social" />
        </label>
        <label className={ETIQUETA_CLS}>
          Correo para respuestas
          <input className={INPUT_CLS} value={f.correoRespuesta} disabled={soloLectura} onChange={(e) => cambiar({ correoRespuesta: e.target.value })} placeholder="juridico@empresa.com" />
        </label>
      </section>

      <Copias titulo="Copias (CC)" ayuda="Reciben una copia visible de los envíos que elijas. Sin tipos, de todos." valor={f.cc} onCambio={(cc) => cambiar({ cc })} soloLectura={soloLectura} />
      <Copias titulo="Copias ocultas (BCC)" ayuda="Igual que las copias, pero ocultas para el destinatario." valor={f.bcc} onCambio={(bcc) => cambiar({ bcc })} soloLectura={soloLectura} />

      <section className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <label className={ETIQUETA_CLS}>
          Destinatarios de los reportes semanal y mensual
          <textarea className={TEXTAREA_CLS} rows={3} value={f.destinatariosReportes} disabled={soloLectura} onChange={(e) => cambiar({ destinatariosReportes: e.target.value })} placeholder="uno por línea" />
        </label>
        <label className={ETIQUETA_CLS}>
          Correos de alerta operativa (falta de representante legal)
          <textarea className={TEXTAREA_CLS} rows={3} value={f.correosAlertaOperativa} disabled={soloLectura} onChange={(e) => cambiar({ correosAlertaOperativa: e.target.value })} placeholder="uno por línea" />
        </label>
      </section>

      {invalidos.length > 0 && <InlineAlert tone="warning" compact>No son correos válidos: {invalidos.join(", ")}</InlineAlert>}
      {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
      {!soloLectura && (
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} disabled={!sucio} onClick={() => setForm(null)}>
            Descartar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!sucio || ocupado || invalidos.length > 0} onClick={guardar}>
            {ocupado ? "Guardando…" : "Guardar"}
          </button>
        </div>
      )}
    </div>
  );
}

/** Configuración de la empresa (HU #13476, §7.11): remitente, copias y destinatarios. Lo demás, en solo lectura. */
export default function EmpresaYCorreo() {
  const { sesion } = useSesion();
  const [empresa, setEmpresa] = useState<string | null>(null);
  return (
    <Requiere permiso={P.configuracionVer}>
      <section className="flex flex-col gap-4">
        <Encabezado titulo="Empresa y correo" descripcion="Cómo salen los correos de la empresa: remitente, respuestas y copias." />
        {sesion?.usuario.esSuperAdmin && <SelectorEmpresa valor={empresa} onCambio={setEmpresa} />}
        <Configuracion key={empresa ?? "propia"} empresa={empresa} />
        {(!sesion?.usuario.esSuperAdmin || empresa) && <ConsultasEmpresa key={`consultas:${empresa ?? "propia"}`} empresa={empresa} />}
      </section>
    </Requiere>
  );
}

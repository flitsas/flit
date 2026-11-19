"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { Icono } from "@flit/ui/Icono";
import { EstadoVacio, InlineAlert, ListaDefinicion, StatusBadge, VehicleLoader } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { ConsultasEmpresa } from "@/components/consultas/ConsultasAdmin";
import { TipologiasEmpresa } from "@/components/consultas/TipologiasEmpresa";
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
      <p className="text-xs text-muted-foreground">{ayuda}</p>
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
            <button type="button" aria-label="Quitar" className="text-destructive hover:text-destructive/80" onClick={() => onCambio(valor.filter((_, j) => j !== i))}>
              <Icono nombre="papelera" className="h-4 w-4" />
            </button>
          )}
        </div>
      ))}
      {!soloLectura && (
        <button type="button" className={`${controlCls(false)} w-fit`} onClick={() => onCambio([...valor, { correo: "", tipos: [] }])}>
          <Icono nombre="mas" className="h-3.5 w-3.5" />
          Agregar correo
        </button>
      )}
    </fieldset>
  );
}

interface FormCorreo {
  remitenteNombre: string;
  correoRespuesta: string;
  cc: Copia[];
  bcc: Copia[];
  destinatariosReportes: string;
  correosAlertaOperativa: string;
}

function aForm(c: ConfiguracionEmpresa): FormCorreo {
  return {
    remitenteNombre: c.remitenteNombre ?? "",
    correoRespuesta: c.correoRespuesta ?? "",
    cc: c.cc,
    bcc: c.bcc,
    destinatariosReportes: c.destinatariosReportes.join("\n"),
    correosAlertaOperativa: c.correosAlertaOperativa.join("\n"),
  };
}

/** Tarjeta de un bloque: título y propósito fuera, contenido dentro. */
function Bloque({ titulo, proposito, children, sinTarjeta }: { titulo: string; proposito: string; children: ReactNode; sinTarjeta?: boolean }) {
  return (
    <section className="flex flex-col gap-3">
      <div className="flex flex-col gap-0.5">
        <h2 className="text-base font-semibold">{titulo}</h2>
        <p className="text-sm text-muted-foreground">{proposito}</p>
      </div>
      {sinTarjeta ? children : <div className="flex flex-col gap-5 rounded-xl border border-border bg-card p-4 md:p-5">{children}</div>}
    </section>
  );
}

const AYUDA_CLS = "font-normal text-muted-foreground";

function Incluido({ si }: { si: boolean }) {
  return <StatusBadge label={si ? "Incluido" : "No incluido"} tone={si ? "success" : "neutral"} />;
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

  if (!ruta) return <EstadoVacio titulo="Elige una empresa" mensaje="Selecciona una empresa para ver cómo salen sus correos y lo que tiene contratado." />;
  if (config.estado === "loading") return <VehicleLoader label="Cargando la configuración…" />;
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
  const cuentaPropia = c.canalCorreo === "EmpresaApi";
  // El SuperAdmin no ve el enlace: representantes no recibe la empresa elegida y caería en el selector vacío.
  const verRepresentantes = !esSuper && tienePermiso(sesion, P.representantesVer);

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-8">
        <Bloque titulo="Cómo salen tus correos" proposito="Así ve el organismo de tránsito los correos que enviamos en nombre de tu empresa.">
          {/* HU #13882: el canal sale del «Enrutamiento de notificaciones» de la empresa en la plataforma; aquí solo se ve. */}
          <ListaDefinicion
            items={[
              {
                termino: "Se envían desde",
                valor: (
                  <span className="flex flex-col gap-0.5">
                    <span className="font-semibold">{cuentaPropia ? "Cuenta de correo propia de la empresa" : "Cuenta de FLIT"}</span>
                    <span className="text-xs text-muted-foreground">
                      {esSuper ? "Sigue el «Enrutamiento de notificaciones» de la empresa en Trámites (Administración → Compañías)." : "Lo define FLIT para tu empresa."}
                    </span>
                  </span>
                ),
              },
            ]}
          />
          <div className="grid max-w-6xl grid-cols-1 gap-4 md:grid-cols-2">
            <label className={ETIQUETA_CLS}>
              Nombre del remitente
              <input className={INPUT_CLS} value={f.remitenteNombre} maxLength={120} disabled={soloLectura} onChange={(e) => cambiar({ remitenteNombre: e.target.value })} placeholder="Si se deja vacío, la razón social" />
              <span className={AYUDA_CLS}>
                {cuentaPropia
                  ? "Con la cuenta propia de la empresa, los correos salen con el remitente de esa cuenta."
                  : "El nombre que ve el organismo como remitente. Si lo dejas vacío, usamos la razón social."}
              </span>
            </label>
            <label className={ETIQUETA_CLS}>
              Correo para respuestas del organismo
              <input className={INPUT_CLS} value={f.correoRespuesta} disabled={soloLectura} onChange={(e) => cambiar({ correoRespuesta: e.target.value })} placeholder="Ej.: juridico@tuempresa.com" />
              <span className={AYUDA_CLS}>
                Va escrito dentro del derecho de petición y de la solicitud de cambio de contraventor, para que el organismo responda a ese correo. No cambia el remitente de los correos.
              </span>
            </label>
          </div>
          {!f.correoRespuesta.trim() && (
            <InlineAlert tone="warning" compact className="max-w-6xl">
              Sin este correo usamos el del representante legal. Si tampoco hay, no se podrán radicar derechos de petición ni pedir cambios de contraventor para vehículos de la empresa.
              {verRepresentantes && (
                <>
                  {" "}
                  <Link href="/configuracion/representantes" className="font-semibold underline underline-offset-2">
                    Revisar representantes legales
                  </Link>
                </>
              )}
            </InlineAlert>
          )}
          <div className="grid max-w-6xl grid-cols-1 gap-5 xl:grid-cols-2">
            <Copias titulo="Copias visibles (CC)" ayuda="Reciben una copia visible de los envíos que elijas; si no eliges ninguno, de todos." valor={f.cc} onCambio={(cc) => cambiar({ cc })} soloLectura={soloLectura} />
            <Copias titulo="Copias ocultas (BCC)" ayuda="Igual que las copias visibles, pero el organismo no ve estos correos." valor={f.bcc} onCambio={(bcc) => cambiar({ bcc })} soloLectura={soloLectura} />
          </div>
        </Bloque>

        <Bloque titulo="Quién recibe reportes y alertas" proposito="Correos internos de tu empresa: aquí no se escribe al organismo.">
          <div className="grid max-w-6xl grid-cols-1 gap-4 md:grid-cols-2">
            <label className={ETIQUETA_CLS}>
              Reporte semanal y mensual
              <textarea className={TEXTAREA_CLS} rows={3} value={f.destinatariosReportes} disabled={soloLectura} onChange={(e) => cambiar({ destinatariosReportes: e.target.value })} placeholder="Un correo por línea" />
              <span className={AYUDA_CLS}>Reciben el resumen de la gestión de comparendos.</span>
            </label>
            <label className={ETIQUETA_CLS}>
              Alertas operativas
              <textarea className={TEXTAREA_CLS} rows={3} value={f.correosAlertaOperativa} disabled={soloLectura} onChange={(e) => cambiar({ correosAlertaOperativa: e.target.value })} placeholder="Un correo por línea" />
              <span className={AYUDA_CLS}>Avisan cuando falta un representante legal y eso frena un derecho de petición. Si lo dejas vacío, avisamos a quienes gestionan los representantes.</span>
            </label>
          </div>
          {!c.hasReportesModule && (
            <InlineAlert tone="info" compact>
              {esSuper ? "La empresa no tiene" : "Tu empresa no tiene"} el módulo de Reportes: los correos del reporte semanal y mensual no recibirán nada hasta que se contrate.
            </InlineAlert>
          )}
          {invalidos.length > 0 && <InlineAlert tone="warning" compact>No son correos válidos: {invalidos.join(", ")}</InlineAlert>}
        </Bloque>

        {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}

        {!soloLectura && (
          <div
            className={`flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-end ${
              sucio ? "sticky bottom-3 z-10 rounded-xl border border-border bg-card p-3 shadow-[var(--kit-sombra)]" : ""
            }`}
          >
            {sucio && <p className="text-xs font-semibold text-muted-foreground sm:mr-auto" role="status">Tienes cambios sin guardar.</p>}
            <div className="grid grid-cols-2 gap-2 sm:flex">
              <button type="button" className={controlCls(false)} disabled={!sucio} onClick={() => setForm(null)}>
                Descartar
              </button>
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!sucio || ocupado || invalidos.length > 0} onClick={guardar}>
                {ocupado ? "Guardando…" : "Guardar cambios"}
              </button>
            </div>
          </div>
        )}
      </div>

      <Bloque
        titulo={esSuper ? "Lo que tiene contratado la empresa" : "Lo que tienes contratado con FLIT"}
        proposito="Solo lectura. Si necesitas cambiar algo, solicítalo a operaciones de FLIT."
        sinTarjeta
      >
        <div className="rounded-xl border border-border bg-card p-4 md:p-5">
          <ListaDefinicion
            columnas={2}
            items={[
              { termino: "Derechos de petición", valor: <Incluido si={c.hasRightPetitionModule} /> },
              { termino: "Lectura automática de documentos", valor: <Incluido si={c.hasOcrAccess} /> },
              { termino: "Consulta del contraventor", valor: <Incluido si={c.allowsContravenerConsultation} /> },
              { termino: "Cambio de contraventor", valor: <Incluido si={c.allowsContravenerChange} /> },
              { termino: "Reportes", valor: <Incluido si={c.hasReportesModule} /> },
              { termino: "Datos del conductor", valor: c.tipoIntegracionConductor === "ApiRenting" ? "Se consultan automáticamente" : "Se registran a mano" },
              { termino: "Plantillas de documentos", valor: c.conjuntoPlantillas === "Renting" ? "Renting" : "FLIT" },
              { termino: "Representante legal", valor: c.requiereRepresentanteLegal ? "Obligatorio para radicar" : "No obligatorio" },
            ]}
          />
        </div>
        <ConsultasEmpresa key={`consultas:${empresa ?? "propia"}`} empresa={empresa} />
        <TipologiasEmpresa key={`tipologias:${empresa ?? "propia"}`} empresa={empresa} />
      </Bloque>
    </div>
  );
}

/** Configuración de la empresa (HU #13476, §7.11): remitente, copias y destinatarios. Lo demás, en solo lectura. */
export default function EmpresaYCorreo() {
  const { sesion } = useSesion();
  const [empresa, setEmpresa] = useState<string | null>(null);
  return (
    <Requiere permiso={P.configuracionVer}>
      <section className="flex flex-col gap-6">
        <Encabezado titulo="Empresa y correo" descripcion="Cómo salen los correos de tu empresa, quién recibe los avisos y qué tienes contratado con FLIT." />
        {sesion?.usuario.esSuperAdmin && <SelectorEmpresa valor={empresa} onCambio={setEmpresa} />}
        <Configuracion key={empresa ?? "propia"} empresa={empresa} />
      </section>
    </Requiere>
  );
}

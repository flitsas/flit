"use client";

import { useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { DataTable, InlineAlert, Modal, StatusBadge } from "@flit/ui";
import { VistaPrevia } from "@/components/plantillas/VistaPrevia";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { Interruptor } from "@/components/ui/Interruptor";
import { apiFetch } from "@/lib/api";
import { bloqueoActivar, errorTope, esAutomatizable, estadoTexto, mensajeError, TOPE_MAXIMO, TOPE_MINIMO, TOPE_POR_DEFECTO } from "@/lib/envioAutomaticoDp";
import { formatoFecha } from "@/lib/format/date";
import type { ConfiguracionEmpresa, PlantillaResumen, Tipologia, VersionPlantilla } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

interface FilaTipologia {
  tipologia: Tipologia;
  /** Versión publicada del texto del DP (en el conjunto de plantillas de la empresa); null si no hay. */
  version: VersionPlantilla | null;
}

interface Datos {
  config: ConfiguracionEmpresa;
  filas: FilaTipologia[];
  /** Versión publicada del correo que acompaña al DP (CorreoDpOt) en el conjunto de la empresa; null si no hay. */
  correo: VersionPlantilla | null;
}

async function cargarDatos(empresaId: string, signal: AbortSignal): Promise<Datos> {
  const [config, tipologias] = await Promise.all([
    apiFetch<ConfiguracionEmpresa>(`/admin/empresas/${empresaId}/configuracion`, { signal }),
    apiFetch<Tipologia[]>("/admin/tipologias", { query: { empresa: empresaId }, signal }),
  ]);
  const plantillas = await apiFetch<PlantillaResumen[]>("/plantillas", { query: { tipo: "DpDocumento", conjunto: config.conjuntoPlantillas }, signal });
  const plantillasCorreo = await apiFetch<PlantillaResumen[]>("/plantillas", { query: { tipo: "CorreoDpOt", conjunto: config.conjuntoPlantillas }, signal });
  const correoPublicado = plantillasCorreo[0]?.publicada;
  const correo = correoPublicado ? await apiFetch<VersionPlantilla>(`/plantillas/versiones/${correoPublicado.id}`, { signal }) : null;
  const filas = await Promise.all(
    tipologias
      .filter((t) => esAutomatizable(t.codigo))
      .map(async (tipologia) => {
        const publicada = plantillas.find((p) => p.tipologiaId === tipologia.id)?.publicada;
        const version = publicada ? await apiFetch<VersionPlantilla>(`/plantillas/versiones/${publicada.id}`, { signal }) : null;
        return { tipologia, version };
      }),
  );
  return { config, filas, correo };
}

/** Enciende o apaga el envío automático de la empresa y fija el tope diario (1 a 500). Apagado es el estado por defecto. */
function Interruptores({ datos, empresaId, onGuardado }: { datos: Datos; empresaId: string; onGuardado: () => void }) {
  const toast = useToast();
  const { config } = datos;
  const [activo, setActivo] = useState(config.dpAutomaticoActivo ?? false);
  const [tope, setTope] = useState(String(config.dpAutomaticoTopeDiario ?? TOPE_POR_DEFECTO));
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const sinModulo = !config.hasRightPetitionModule;
  const numero = Number(tope);
  const invalido = tope.trim() === "" ? errorTope(Number.NaN) : errorTope(numero);
  const sinCambios = activo === (config.dpAutomaticoActivo ?? false) && numero === (config.dpAutomaticoTopeDiario ?? TOPE_POR_DEFECTO);

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/admin/empresas/${empresaId}/envio-automatico-dp`, { method: "PUT", body: { activo, topeDiario: numero } });
      toast.show(activo ? "Envío automático encendido." : "Envío automático apagado.", "success");
      onGuardado();
    } catch (e) {
      setError(mensajeError(e, "No se pudo guardar el envío automático."));
    } finally {
      setOcupado(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <h3 className="text-sm font-semibold">Envío automático de la empresa</h3>
      {sinModulo && (
        <InlineAlert tone="warning" compact>
          Esta empresa no tiene el módulo de derechos de petición, así que el envío automático no puede encenderse.
        </InlineAlert>
      )}
      <Interruptor
        activo={activo && !sinModulo}
        onCambio={setActivo}
        deshabilitado={sinModulo}
        etiqueta={activo && !sinModulo ? "Encendido" : "Apagado"}
        ayuda="Cuando está encendido, cada día hábil se envían solos los derechos de petición que aplican, hasta el tope diario. Al principio siempre está apagado."
      />
      <label className={`${ETIQUETA_CLS} max-w-xs`}>
        Tope diario de envíos
        <input
          className={INPUT_CLS}
          type="number"
          inputMode="numeric"
          min={TOPE_MINIMO}
          max={TOPE_MAXIMO}
          step={1}
          value={tope}
          disabled={sinModulo}
          aria-invalid={invalido !== null}
          onChange={(e) => setTope(e.target.value)}
        />
        <span className="text-[11px] font-normal opacity-70">De {TOPE_MINIMO} a {TOPE_MAXIMO} por día. Lo que pase del tope sigue el siguiente día hábil.</span>
      </label>
      {invalido && !sinModulo && <InlineAlert tone="warning" compact>{invalido}</InlineAlert>}
      {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
      <div>
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || sinModulo || invalido !== null || sinCambios} onClick={guardar}>
          {ocupado ? "Guardando…" : "Guardar"}
        </button>
      </div>
    </div>
  );
}

type EstadoVista = "loading" | "ready" | "error";

/** Razón visible de por qué «Aprobar texto» sigue bloqueado; null si ya se puede. */
function razonBloqueo(dp: EstadoVista, correo: EstadoVista | "sin-correo", nota: string): string | null {
  if (dp === "loading" || correo === "loading") return "Espera a que se arme la vista previa: no se aprueba un texto que no se ha visto.";
  if (dp === "error" || correo === "error") return "No se pudo armar la vista previa, así que no se puede aprobar un texto que no se ve. Cierra y vuelve a intentar.";
  if (!nota.trim()) return "Escribe la nota de la revisión para poder aprobar.";
  return null;
}

function AprobarTexto({ fila, correo, conjunto, onCerrar, onAprobada }: { fila: FilaTipologia; correo: VersionPlantilla | null; conjunto: string; onCerrar: () => void; onAprobada: () => void }) {
  const toast = useToast();
  const [nota, setNota] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [vistaDp, setVistaDp] = useState<EstadoVista>("loading");
  const [vistaCorreo, setVistaCorreo] = useState<EstadoVista>("loading");
  const v = fila.version!;
  const razon = razonBloqueo(vistaDp, correo ? vistaCorreo : "sin-correo", nota);

  const aprobar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/admin/plantillas/versiones/${v.id}/revision-juridica`, { method: "POST", body: { nota: nota.trim() } });
      toast.show("Texto aprobado.", "success");
      onAprobada();
    } catch (e) {
      setError(mensajeError(e, "No se pudo aprobar el texto."));
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal
      icono="escudo"
      open
      onClose={onCerrar}
      busy={ocupado}
      size="2xl"
      title={`Aprobar el texto de ${fila.tipologia.codigo}`}
      description={`${fila.tipologia.nombre} · versión ${v.numeroVersion} publicada${v.publicadaEn ? ` el ${formatoFecha(v.publicadaEn)}` : ""}`}
      footer={
        <div className="flex flex-col gap-2">
          {razon && <p role="status" className="text-xs text-muted-foreground">{razon}</p>}
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={onCerrar} disabled={ocupado}>
              Cancelar
            </button>
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || razon !== null} onClick={aprobar}>
              {ocupado ? "Aprobando…" : "Aprobar texto"}
            </button>
          </div>
        </div>
      }
    >
      <div className="flex min-w-0 flex-col gap-4">
        <InlineAlert tone="info" compact>
          VISTA PREVIA con datos de ejemplo: no se envía nada a nadie. Lee el documento y el correo; al aprobar dices que el abogado los revisó y que se pueden enviar sin que una persona los revise cada vez.
        </InlineAlert>
        <div className="grid min-w-0 grid-cols-1 gap-4 xl:grid-cols-2">
          <div className="flex min-w-0 flex-col gap-2">
            <h4 className="text-xs font-semibold">Derecho de petición (documento PDF)</h4>
            <VistaPrevia contenido={{ tipo: "DpDocumento", asunto: null, cuerpoHtml: v.cuerpoHtml, conjunto }} alto={440} onEstado={setVistaDp} />
          </div>
          <div className="flex min-w-0 flex-col gap-2">
            <h4 className="text-xs font-semibold">Correo que lo acompaña</h4>
            {correo ? (
              <VistaPrevia contenido={{ tipo: "CorreoDpOt", asunto: correo.asunto, cuerpoHtml: correo.cuerpoHtml, conjunto }} alto={440} onEstado={setVistaCorreo} />
            ) : (
              <InlineAlert tone="warning" compact>
                No hay un correo publicado para el derecho de petición en el conjunto de esta empresa. Publícalo en el editor de plantillas.
              </InlineAlert>
            )}
          </div>
        </div>
        <label className={ETIQUETA_CLS}>
          Nota de la revisión (obligatoria)
          <textarea className={TEXTAREA_CLS} rows={3} maxLength={1000} value={nota} onChange={(e) => setNota(e.target.value)} />
        </label>
        {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
      </div>
    </Modal>
  );
}

/** Qué tipologías se envían solas en una empresa y si su texto está aprobado (Epic #14606, HU #14614). */
function Tipologias({ datos, empresaId, onCambio }: { datos: Datos; empresaId: string; onCambio: () => void }) {
  const toast = useToast();
  const [aprobar, setAprobar] = useState<FilaTipologia | null>(null);
  const [ocupada, setOcupada] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const habilitar = async (fila: FilaTipologia) => {
    setOcupada(fila.tipologia.id);
    setError(null);
    try {
      await apiFetch(`/admin/empresas/${empresaId}/tipologias/${fila.tipologia.id}`, { method: "PUT", body: { habilitada: true } });
      toast.show(`${fila.tipologia.codigo} habilitada para esta empresa.`, "success");
      onCambio();
    } catch (e) {
      setError(mensajeError(e, "No se pudo habilitar la tipología."));
    } finally {
      setOcupada(null);
    }
  };

  const marcar = async (fila: FilaTipologia, automatizable: boolean) => {
    setOcupada(fila.tipologia.id);
    setError(null);
    try {
      await apiFetch(`/admin/empresas/${empresaId}/tipologias/${fila.tipologia.id}/automatizable`, { method: "PUT", body: { automatizable } });
      toast.show(automatizable ? `${fila.tipologia.codigo} se enviará sola.` : `${fila.tipologia.codigo} vuelve a enviarse a mano.`, "success");
      onCambio();
    } catch (e) {
      setError(mensajeError(e, "No se pudo cambiar la tipología."));
    } finally {
      setOcupada(null);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <h3 className="text-sm font-semibold">Tipologías que se envían solas</h3>
      <InlineAlert tone="info" compact>
        Una tipología solo se envía sola si su texto publicado está aprobado. Publicar un texto nuevo apaga la marca de esa tipología en todas las empresas: hay que aprobar el texto nuevo y volver a marcarla.
      </InlineAlert>
      {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
      <DataTable
        ariaLabel="Tipologías automatizables"
        rows={datos.filas}
        getRowKey={(f) => f.tipologia.id}
        status="ready"
        emptyMessage="No hay tipologías con regla automática."
        columns={[
          { key: "tipologia", header: "Tipología", render: (f) => <span><span className="font-mono font-semibold">{f.tipologia.codigo}</span> · {f.tipologia.nombre}</span> },
          {
            key: "habilitada",
            header: "En la empresa",
            render: (f) => <StatusBadge label={f.tipologia.habilitada ? "Habilitada" : "No habilitada"} tone={f.tipologia.habilitada ? "success" : "neutral"} />,
          },
          {
            key: "texto",
            header: "Texto del documento",
            render: (f) => {
              const e = estadoTexto(f.version !== null, f.version?.revisadaJuridicamenteEn);
              const v = f.version;
              return (
                <div className="flex min-w-0 flex-col gap-1">
                  <div>
                    <StatusBadge label={e.texto} tone={e.tono} />
                  </div>
                  {v?.revisadaJuridicamenteEn && (
                    <p className="break-words text-[11px] text-muted-foreground">
                      Aprobado por {v.revisadaPor ?? "un abogado"} el {formatoFecha(v.revisadaJuridicamenteEn)}
                      {v.notaRevisionJuridica ? ` · «${v.notaRevisionJuridica}»` : ""}
                    </p>
                  )}
                </div>
              );
            },
          },
          {
            key: "modo",
            header: "Envío",
            render: (f) => <StatusBadge label={f.tipologia.automatizable ? "Automático" : "A mano"} tone={f.tipologia.automatizable ? "success" : "neutral"} />,
          },
          {
            key: "acciones",
            header: "Acciones",
            render: (f) => {
              const e = estadoTexto(f.version !== null, f.version?.revisadaJuridicamenteEn);
              const bloqueo = f.tipologia.automatizable ? null : bloqueoActivar(f.tipologia.habilitada, f.version !== null, f.version?.revisadaJuridicamenteEn);
              const ocupadaAhora = ocupada === f.tipologia.id;
              return (
                <div className="flex min-w-0 flex-col gap-2">
                  <div className="flex flex-wrap gap-2">
                    {e.aprobable && (
                      <button type="button" className={controlCls(false)} onClick={() => setAprobar(f)}>
                        <Icono nombre="escudo" className="h-3.5 w-3.5" />
                        Aprobar texto
                      </button>
                    )}
                    {bloqueo?.accion === "habilitar" && (
                      <button type="button" className={controlCls(false)} disabled={ocupadaAhora} onClick={() => habilitar(f)}>
                        {ocupadaAhora ? "Habilitando…" : "Habilitar para esta empresa"}
                      </button>
                    )}
                    <button
                      type="button"
                      className={controlCls(false)}
                      disabled={ocupadaAhora || bloqueo !== null}
                      onClick={() => marcar(f, !f.tipologia.automatizable)}
                    >
                      {f.tipologia.automatizable ? "Volver a enviar a mano" : "Activar envío automático"}
                    </button>
                  </div>
                  {bloqueo && <p className="break-words text-[11px] text-muted-foreground">{bloqueo.texto}</p>}
                </div>
              );
            },
          },
        ]}
      />
      {aprobar && (
        <AprobarTexto
          fila={aprobar}
          correo={datos.correo}
          conjunto={datos.config.conjuntoPlantillas}
          onCerrar={() => setAprobar(null)}
          onAprobada={() => {
            setAprobar(null);
            onCambio();
          }}
        />
      )}
    </div>
  );
}

/** Pantalla del SuperAdmin para el envío automático de derechos de petición (Epic #14606, HU #14614). */
export default function EnvioAutomaticoDp() {
  const [empresaId, setEmpresaId] = useState<string | null>(null);
  const carga = useCarga(empresaId ? `envio-automatico-dp:${empresaId}` : null, (signal) => cargarDatos(empresaId!, signal));
  const datos = carga.datos;

  return (
    <Requiere superAdmin>
      <section className="flex min-w-0 flex-col gap-6">
        <Encabezado
          titulo="Envío automático de derechos de petición"
          descripcion="Elige una empresa para encenderle o apagarle el envío automático, fijar el tope diario y decidir qué tipologías se envían solas."
        />
        <SelectorEmpresa valor={empresaId} onCambio={setEmpresaId} />
        {!empresaId && <p className="text-sm opacity-70">Elige una empresa para empezar.</p>}
        {empresaId && carga.estado === "loading" && <p className="text-sm opacity-60">Cargando…</p>}
        {empresaId && carga.estado === "error" && (
          <InlineAlert tone="error">
            {carga.error}{" "}
            <button type="button" className="underline" onClick={carga.recargar}>
              Reintentar
            </button>
          </InlineAlert>
        )}
        {empresaId && datos && (
          <>
            <Interruptores key={`${empresaId}:${datos.config.dpAutomaticoActivo}:${datos.config.dpAutomaticoTopeDiario}`} datos={datos} empresaId={empresaId} onGuardado={carga.recargar} />
            <Tipologias datos={datos} empresaId={empresaId} onCambio={carga.recargar} />
          </>
        )}
      </section>
    </Requiere>
  );
}

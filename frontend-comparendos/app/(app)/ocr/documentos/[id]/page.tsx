"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Download, RotateCcw, Save, Trash2, ZoomIn, ZoomOut } from "lucide-react";
import { DataTable, InlineAlert, Modal } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { DetalleComparendo } from "@/components/detalle/DetalleComparendo";
import { EstadoDocumento, useSondeo } from "@/components/ocr/comunes";
import { OfertaPlacas } from "@/components/ocr/OfertaPlacas";
import { Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import {
  BOTON_PELIGRO_CLS,
  BOTON_PRIMARIO_CLS,
  controlCls,
  ETIQUETA_CLS,
  INPUT_CLS,
  TITULO_CLS,
} from "@/components/ui/estilos";
import { API_PREFIX, apiDownload, apiFetch } from "@/lib/api";
import { formatoFechaHora } from "@/lib/format/date";
import { P } from "@/lib/nav";
import {
  accionesDocumento,
  cambiosCampos,
  confianza,
  ETIQUETA_CAMPO,
  FUENTE_DUPLICADO,
  hayEnCurso,
  TIPO_FALLO,
  TIPOS_DOCUMENTO,
  tipoEntrada,
  valorInicial,
  vistaOriginal,
} from "@/lib/ocr";
import { tienePermiso } from "@/lib/sesion";
import { useCarga } from "@/lib/useCarga";

interface Campo {
  nombreCampo: string;
  obligatorio: boolean;
  valorExtraido: string | null;
  confianza: number | null;
  esNull: boolean;
  valorCorregido: string | null;
  valorFinal: string | null;
}

interface Duplicado {
  tipo: "comparendo" | "documento";
  id: string;
  numero: string | null;
  fuente: string | null;
  registradoEn: string;
  activo?: boolean;
  pagado?: boolean;
  paqueteId?: string;
  paquete?: string;
  estado?: string;
  comparendoId?: string | null;
}

interface Documento {
  id: string;
  paqueteId: string;
  nombreArchivo: string;
  tipoArchivo: string;
  archivoId: string;
  estado: string;
  intentos: number;
  proveedorModelo: string | null;
  numeroComparendoExtraido: string | null;
  camposFaltantes: string[];
  tipoFallo: string | null;
  mensajeError: string | null;
  reintentable: boolean;
  comparendoId: string | null;
  procesadoEn: string | null;
  revisadoEn: string | null;
  motivoDescarte: string | null;
  duplicado: Duplicado | null;
  campos: Campo[];
  ediciones: {
    campo: string;
    valorAnterior: string | null;
    valorNuevo: string | null;
    usuarioId: string;
    usuarioNombre: string | null;
    editadoEn: string;
    motivo: string | null;
  }[];
  historialIntentos: {
    numero: number;
    resultado: string;
    tipoError: string | null;
    mensajeError: string | null;
    intentadoEn: string;
    proximoReintentoEn: string | null;
  }[];
}

interface Registro {
  comparendoId: string;
  numero: string;
  placa: string | null;
  placaYaMonitoreada: boolean;
}

const TONO_CONFIANZA = {
  alta: "text-[#1E7B3C]",
  media: "text-[#B45309]",
  baja: "text-[#B42318]",
} as const;

function Original({ d }: { d: Documento }) {
  const [zoom, setZoom] = useState(1);
  const url = `${API_PREFIX}/archivos/${d.archivoId}?inline=true`;
  const vista = vistaOriginal(d.tipoArchivo, d.nombreArchivo);
  const descargar = () => apiDownload(`/archivos/${d.archivoId}`, d.nombreArchivo);

  return (
    <div className="flex min-h-[70vh] flex-col gap-2">
      <div className="flex items-center justify-between gap-2 text-xs">
        <span className="truncate font-semibold">{d.nombreArchivo}</span>
        <div className="flex items-center gap-1">
          {vista === "imagen" && (
            <>
              <button
                type="button"
                className={controlCls(false)}
                aria-label="Alejar"
                disabled={zoom <= 0.5}
                onClick={() => setZoom((z) => Math.max(0.5, z - 0.25))}
              >
                <ZoomOut className="h-3.5 w-3.5" aria-hidden />
              </button>
              <span className="w-12 text-center tabular-nums">{Math.round(zoom * 100)} %</span>
              <button
                type="button"
                className={controlCls(false)}
                aria-label="Acercar"
                disabled={zoom >= 4}
                onClick={() => setZoom((z) => Math.min(4, z + 0.25))}
              >
                <ZoomIn className="h-3.5 w-3.5" aria-hidden />
              </button>
            </>
          )}
          <button type="button" className={controlCls(false)} onClick={descargar}>
            <Download className="h-3.5 w-3.5" aria-hidden />
            Descargar
          </button>
        </div>
      </div>
      <div className="flex-1 overflow-auto rounded-2xl border border-[#DFE5ED] bg-[#F8FAFC] dark:border-white/15 dark:bg-[#0B0F14]">
        {vista === "imagen" && (
          // eslint-disable-next-line @next/next/no-img-element -- archivo privado servido por el proxy de la API, no un asset optimizable
          <img
            src={url}
            alt={`Original de ${d.nombreArchivo}`}
            style={{ width: `${zoom * 100}%`, maxWidth: "none" }}
            className="block"
          />
        )}
        {vista === "pdf" && (
          <iframe
            title={`Original de ${d.nombreArchivo}`}
            src={url}
            className="h-full min-h-[70vh] w-full"
          />
        )}
        {vista === "descarga" && (
          <p className="p-6 text-xs opacity-70">
            El navegador no muestra archivos TIFF. Descárgalo para verlo junto al formulario.
          </p>
        )}
      </div>
    </div>
  );
}

function CampoEntrada({
  campo,
  valor,
  onCambio,
  deshabilitado,
}: {
  campo: string;
  valor: string;
  onCambio: (v: string) => void;
  deshabilitado: boolean;
}) {
  const tipo = tipoEntrada(campo);
  if (tipo === "documento")
    return (
      <select
        className={INPUT_CLS}
        value={valor}
        disabled={deshabilitado}
        onChange={(e) => onCambio(e.target.value)}
      >
        <option value="">—</option>
        {TIPOS_DOCUMENTO.map((t) => (
          <option key={t} value={t}>
            {t}
          </option>
        ))}
        {valor && !TIPOS_DOCUMENTO.includes(valor) && (
          <option value={valor}>{valor} (no reconocido)</option>
        )}
      </select>
    );
  if (tipo === "booleano")
    return (
      <select
        className={INPUT_CLS}
        value={valor}
        disabled={deshabilitado}
        onChange={(e) => onCambio(e.target.value)}
      >
        <option value="">No se sabe</option>
        <option value="true">Sí, fotomulta</option>
        <option value="false">No, comparendo físico</option>
      </select>
    );
  return (
    <input
      className={INPUT_CLS}
      type={tipo === "fecha" ? "date" : tipo === "hora" ? "time" : "text"}
      value={valor}
      disabled={deshabilitado}
      onChange={(e) => onCambio(e.target.value)}
    />
  );
}

function Formulario({
  d,
  opera,
  onCambio,
  onRegistrado,
  onVer,
}: {
  d: Documento;
  opera: boolean;
  onCambio: () => void;
  onRegistrado: (r: Registro) => void;
  onVer: (comparendoId: string) => void;
}) {
  const toast = useToast();
  const acciones = accionesDocumento({
    estado: d.estado,
    reintentable: d.reintentable,
    fuenteDuplicado: d.duplicado?.fuente ?? null,
  });
  const iniciales = Object.fromEntries(
    d.campos.map((c) => [c.nombreCampo, valorInicial(c.nombreCampo, c.valorFinal)]),
  );
  const [editados, setEditados] = useState<Record<string, string>>(iniciales);
  const [motivo, setMotivo] = useState("");
  const [modal, setModal] = useState<"guardar" | "descartar" | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const cambios = cambiosCampos(iniciales, editados);
  const sucio = Object.keys(cambios).length > 0;
  const editable = opera && acciones.editar;

  const ejecutar = async (hacer: () => Promise<void>) => {
    setOcupado(true);
    try {
      await hacer();
      setModal(null);
      setMotivo("");
      onCambio();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo completar la acción.", "error");
    } finally {
      setOcupado(false);
    }
  };

  const guardar = () =>
    ejecutar(async () => {
      const r = await apiFetch<{ estado: string; camposFaltantes: string[] }>(
        `/ocr/documentos/${d.id}/campos`,
        {
          method: "PUT",
          body: { campos: cambios, motivo: motivo.trim() || null },
        },
      );
      toast.show(
        r.estado === "Completo"
          ? "Cambios guardados: el documento quedó completo."
          : r.estado === "Duplicado"
            ? "Cambios guardados: el número ya existe."
            : "Cambios guardados.",
        r.estado === "Duplicado" ? "error" : "success",
      );
    });

  const registrar = () =>
    ejecutar(async () => {
      const r = await apiFetch<Registro>(`/ocr/documentos/${d.id}/registrar`, { method: "POST" });
      onRegistrado(r);
      toast.show(`Comparendo ${r.numero} registrado.`, "success");
    });

  const descartar = () =>
    ejecutar(async () => {
      await apiFetch(`/ocr/documentos/${d.id}/descartar`, {
        method: "POST",
        body: { motivo: motivo.trim() },
      });
      toast.show("Documento descartado.", "success");
    });

  const reintentar = () =>
    ejecutar(async () => {
      await apiFetch(`/ocr/documentos/${d.id}/reintentar`, { method: "POST" });
      toast.show("El documento volvió a la cola.", "success");
    });

  const dup = d.duplicado;
  // Arriba y al final del formulario, sin barra flotante: una barra pegada abajo con fondo tapaba los campos (B12-6).
  const botones = opera ? (
    <div className="flex flex-wrap justify-end gap-2">
      {acciones.reintentar && (
        <button type="button" className={controlCls(false)} disabled={ocupado} onClick={reintentar}>
          <RotateCcw className="h-3.5 w-3.5" aria-hidden />
          Reintentar
        </button>
      )}
      {acciones.descartar && (
        <button
          type="button"
          className={controlCls(false)}
          disabled={ocupado}
          onClick={() => setModal("descartar")}
        >
          <Trash2 className="h-3.5 w-3.5" aria-hidden />
          Descartar
        </button>
      )}
      {editable && (
        <button
          type="button"
          className={controlCls(sucio)}
          disabled={ocupado || !sucio}
          onClick={() => setModal("guardar")}
        >
          <Save className="h-3.5 w-3.5" aria-hidden />
          Guardar cambios
        </button>
      )}
      {acciones.registrar && (
        <button
          type="button"
          className={BOTON_PRIMARIO_CLS}
          disabled={ocupado || sucio}
          title={sucio ? "Guarda los cambios antes de registrar" : undefined}
          onClick={registrar}
        >
          {ocupado ? "Registrando…" : "Registrar comparendo"}
        </button>
      )}
    </div>
  ) : null;
  return (
    <div className="flex flex-col gap-4">
      {botones}
      {d.estado === "Fallido" && (
        <InlineAlert
          tone="error"
          title={(d.tipoFallo && TIPO_FALLO[d.tipoFallo]) ?? "El documento falló"}
        >
          {d.mensajeError}
          {!d.reintentable &&
            " No se reintenta: revisa el archivo y descártalo, o cárgalo de nuevo en otro paquete."}
        </InlineAlert>
      )}

      {dup && (
        <InlineAlert tone="warning" title="Este comparendo ya existe: no se puede registrar">
          <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5">
            <dt className="font-semibold">Fuente</dt>
            <dd>{(dup.fuente && FUENTE_DUPLICADO[dup.fuente]) ?? dup.fuente ?? "—"}</dd>
            <dt className="font-semibold">Número</dt>
            <dd>{dup.numero ?? "—"}</dd>
            <dt className="font-semibold">Registrado</dt>
            <dd>{formatoFechaHora(dup.registradoEn)}</dd>
            {dup.tipo === "comparendo" ? (
              <>
                <dt className="font-semibold">Estado</dt>
                <dd>
                  {dup.activo === false ? "Inactivo" : "Activo"}
                  {dup.pagado ? " · pagado" : ""} ·{" "}
                  <button
                    type="button"
                    className="font-semibold text-[#3B4FD6] hover:underline"
                    onClick={() => onVer(dup.id)}
                  >
                    ver el comparendo
                  </button>
                </dd>
              </>
            ) : (
              <>
                <dt className="font-semibold">Paquete</dt>
                <dd>
                  {dup.paquete} ·{" "}
                  <Link
                    className="font-semibold text-[#3B4FD6] hover:underline"
                    href={`/ocr/documentos/${dup.id}`}
                  >
                    ver el documento original
                  </Link>
                </dd>
              </>
            )}
          </dl>
          {acciones.editar && opera && (
            <p className="mt-1">
              Si el número quedó mal leído, corrígelo abajo y guarda: se vuelve a validar.
            </p>
          )}
        </InlineAlert>
      )}

      {d.campos.length > 0 && (
        <div className="flex flex-col gap-3">
          {d.campos.map((c) => {
            const faltante = d.camposFaltantes.includes(c.nombreCampo);
            const conf = confianza(c.confianza);
            return (
              <label key={c.nombreCampo} className={ETIQUETA_CLS}>
                <span className="flex items-center justify-between gap-2">
                  <span className={faltante ? "text-[#B42318]" : undefined}>
                    {ETIQUETA_CAMPO[c.nombreCampo] ?? c.nombreCampo}
                    {c.obligatorio && <span aria-hidden> *</span>}
                    {faltante && <span className="ml-1 font-normal">· falta o no es válido</span>}
                  </span>
                  <span className="flex items-center gap-2 font-normal">
                    {c.valorCorregido !== null && <span className="opacity-60">corregido</span>}
                    {conf ? (
                      <span
                        className={TONO_CONFIANZA[conf.tono]}
                        title="Confianza del modelo en la lectura"
                      >
                        {conf.texto}
                      </span>
                    ) : c.esNull ? (
                      <span className="opacity-50">no leído</span>
                    ) : null}
                  </span>
                </span>
                <div className={faltante ? "rounded-xl ring-1 ring-[#B42318]" : undefined}>
                  <CampoEntrada
                    campo={c.nombreCampo}
                    valor={editados[c.nombreCampo] ?? ""}
                    deshabilitado={!editable || ocupado}
                    onCambio={(v) => setEditados((e) => ({ ...e, [c.nombreCampo]: v }))}
                  />
                </div>
                {c.nombreCampo === "fecha_notificacion" && (
                  <span className="text-[11px] font-normal opacity-70">
                    En las fotomultas casi nunca viene impresa: se toma del sello de recibido o
                    radicado de la empresa. Confírmala, porque de ella cuentan los plazos.
                  </span>
                )}
              </label>
            );
          })}
        </div>
      )}

      {botones && (
        <div className="border-t border-[#DFE5ED] pt-3 dark:border-white/15">{botones}</div>
      )}

      <Modal
        open={modal !== null}
        onClose={() => setModal(null)}
        busy={ocupado}
        title={modal === "descartar" ? "Descartar el documento" : "Guardar la corrección"}
        description={
          modal === "descartar"
            ? "El documento queda descartado con su motivo; no se registra ni se borra."
            : `Cambian ${Object.keys(cambios).length} campo(s). Cada cambio queda en el historial con tu usuario.`
        }
        footer={
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={() => setModal(null)}>
              Cancelar
            </button>
            {modal === "descartar" ? (
              <button
                type="button"
                className={BOTON_PELIGRO_CLS}
                disabled={ocupado || !motivo.trim()}
                onClick={descartar}
              >
                Descartar
              </button>
            ) : (
              <button
                type="button"
                className={BOTON_PRIMARIO_CLS}
                disabled={ocupado}
                onClick={guardar}
              >
                Guardar
              </button>
            )}
          </div>
        }
      >
        <label className={ETIQUETA_CLS}>
          {modal === "descartar" ? "Motivo (obligatorio)" : "Motivo (opcional)"}
          <input
            className={INPUT_CLS}
            value={motivo}
            maxLength={500}
            onChange={(e) => setMotivo(e.target.value)}
          />
        </label>
      </Modal>
    </div>
  );
}

function Historial({ d }: { d: Documento }) {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold">Historial de ediciones</h2>
        <DataTable
          ariaLabel="Ediciones del documento"
          rows={d.ediciones}
          getRowKey={(e) => `${e.campo}-${e.editadoEn}`}
          emptyMessage="Nadie ha corregido este documento."
          columns={[
            { key: "fecha", header: "Fecha", render: (e) => formatoFechaHora(e.editadoEn) },
            { key: "campo", header: "Campo", render: (e) => ETIQUETA_CAMPO[e.campo] ?? e.campo },
            { key: "antes", header: "Anterior", render: (e) => e.valorAnterior ?? "—" },
            { key: "nuevo", header: "Nuevo", render: (e) => e.valorNuevo ?? "—" },
            {
              key: "usuario",
              header: "Usuario",
              render: (e) =>
                e.usuarioNombre ? (
                  <span title={e.usuarioId}>{e.usuarioNombre}</span>
                ) : (
                  <span title={e.usuarioId} className="font-mono text-[11px] opacity-70">
                    {e.usuarioId.slice(0, 8)}
                  </span>
                ),
            },
            { key: "motivo", header: "Motivo", render: (e) => e.motivo ?? "—" },
          ]}
        />
      </div>
      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold">Intentos de extracción</h2>
        <DataTable
          ariaLabel="Intentos de extracción"
          rows={d.historialIntentos}
          getRowKey={(i) => String(i.numero) + i.intentadoEn}
          emptyMessage="Todavía no se ha procesado."
          columns={[
            { key: "n", header: "#", align: "right", render: (i) => i.numero },
            { key: "fecha", header: "Fecha", render: (i) => formatoFechaHora(i.intentadoEn) },
            {
              key: "resultado",
              header: "Resultado",
              render: (i) => (i.resultado === "EXITO" ? "Éxito" : "Error"),
            },
            {
              key: "error",
              header: "Error",
              render: (i) =>
                i.tipoError
                  ? `${TIPO_FALLO[i.tipoError] ?? i.tipoError}${i.mensajeError ? `: ${i.mensajeError}` : ""}`
                  : "—",
            },
            {
              key: "proximo",
              header: "Siguiente intento",
              render: (i) => formatoFechaHora(i.proximoReintentoEn),
            },
          ]}
        />
      </div>
    </div>
  );
}

function Revision({ id }: { id: string }) {
  const { sesion } = useSesion();
  const opera = !sesion?.usuario.esSuperAdmin && tienePermiso(sesion, P.ocrGestionar);
  const carga = useCarga(`ocr-documento:${id}`, (signal) =>
    apiFetch<Documento>(`/ocr/documentos/${id}`, { signal }),
  );
  const d = carga.datos;
  const [registro, setRegistro] = useState<Registro | null>(null);
  const [verComparendo, setVerComparendo] = useState<string | null>(null);
  useSondeo(!!d && hayEnCurso([d.estado]), carga.recargar);

  if (carga.estado === "error" && !d) return <InlineAlert tone="error">{carga.error}</InlineAlert>;
  if (!d) return <p className="text-sm opacity-60">Cargando documento…</p>;

  return (
    <section className="flex flex-col gap-4">
      <Link
        href={`/ocr/paquetes/${d.paqueteId}`}
        className="inline-flex items-center gap-1 text-xs font-semibold text-[#3B4FD6] hover:underline"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden />
        Volver al paquete
      </Link>
      <header className="flex flex-wrap items-center gap-3">
        <h1 className={TITULO_CLS}>{d.numeroComparendoExtraido ?? "Documento sin número"}</h1>
        <EstadoDocumento estado={d.estado} />
        <span className="text-xs opacity-70">
          {d.procesadoEn ? `Procesado ${formatoFechaHora(d.procesadoEn)}` : "En cola"}
          {d.proveedorModelo && ` · ${d.proveedorModelo}`}
          {d.motivoDescarte && ` · Descartado: ${d.motivoDescarte}`}
        </span>
      </header>
      {registro && (
        <div className="flex flex-col gap-2">
          <InlineAlert tone="success" title={`Comparendo ${registro.numero} registrado`}>
            <button
              type="button"
              className="font-semibold text-[#3B4FD6] hover:underline"
              onClick={() => setVerComparendo(registro.comparendoId)}
            >
              Ver el comparendo
            </button>
          </InlineAlert>
          {registro.placa && !registro.placaYaMonitoreada && (
            <OfertaPlacas placas={[registro.placa]} />
          )}
          {registro.placa && registro.placaYaMonitoreada && (
            <InlineAlert tone="info">La placa {registro.placa} ya está monitoreada.</InlineAlert>
          )}
        </div>
      )}

      {hayEnCurso([d.estado]) && (
        <InlineAlert tone="info">
          El documento está en cola; esta vista se actualiza sola.
        </InlineAlert>
      )}
      <div className="grid gap-6 lg:grid-cols-2">
        <Original d={d} />
        {/* La clave fuerza un formulario nuevo cada vez que cambian los valores guardados. */}
        <Formulario
          key={`${d.estado}:${d.revisadoEn ?? ""}:${d.campos.map((c) => c.valorFinal).join("|")}`}
          d={d}
          opera={opera}
          onCambio={carga.recargar}
          onRegistrado={setRegistro}
          onVer={setVerComparendo}
        />
      </div>
      <Historial d={d} />
      <DetalleComparendo id={verComparendo} onCerrar={() => setVerComparendo(null)} />
    </section>
  );
}

/** Revisión de un documento OCR en vista dividida (HU #13471 sobre la API de la HU #13470; §7.6). */
export default function DocumentoOcr() {
  const { id } = useParams<{ id: string }>();
  return (
    <Requiere permiso={P.ocrVer}>
      <Revision id={id} />
    </Requiere>
  );
}

"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ZoomOut } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import { DataTable, InlineAlert, Miga, Modal, VehicleLoader } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { useAbrirDetalle } from "@/components/detalle/useAbrirDetalle";
import { EstadoDocumento, useSondeo } from "@/components/ocr/comunes";
import { ModalAsociar, ModalComparar } from "@/components/ocr/AsociarDocumento";
import { OfertaPlacas } from "@/components/ocr/OfertaPlacas";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { BOTON_PELIGRO_CLS, BOTON_PRIMARIO_CLS, ETIQUETA_CLS, FECHA_CLS, INPUT_CLS, controlCls } from "@/components/ui/estilos";
import { API_PREFIX, ApiError, apiDownload, apiFetch } from "@/lib/api";
import { formatoFechaHora } from "@/lib/format/date";
import { P } from "@/lib/nav";
import {
  accionesDocumento,
  cambiosCampos,
  ETIQUETA_CAMPO,
  FUENTE_DUPLICADO,
  hayEnCurso,
  marcasRevision,
  TIPO_FALLO,
  TIPOS_DOCUMENTO,
  ETIQUETA_TIPO_DOCUMENTO,
  tipoEntrada,
  vistaDuplicado,
  type EvidenciaActual,
  valorInicial,
  vistaOriginal,
} from "@/lib/ocr";
import { errorNit, mascaraNit } from "@/lib/nit";
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
  evidenciaActual?: EvidenciaActual | null;
  asociable?: boolean;
  motivoNoAsociable?: string | null;
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
  avisoNumero: string | null;
  numeroPorConfirmar: boolean;
  reprocesos: number;
  maximoReprocesos: number;
  motivoNoReprocesable: string | null;
  versionPrompt: number | null;
  comparendoId: string | null;
  procesadoEn: string | null;
  revisadoEn: string | null;
  motivoDescarte: string | null;
  duplicado: Duplicado | null;
  campos: Campo[];
  ediciones: { campo: string; valorAnterior: string | null; valorNuevo: string | null; usuarioId: string; usuarioNombre: string | null; editadoEn: string; motivo: string | null }[];
  historialIntentos: { numero: number; resultado: string; tipoError: string | null; mensajeError: string | null; intentadoEn: string; proximoReintentoEn: string | null }[];
}

interface Registro {
  comparendoId: string;
  numero: string;
  placa: string | null;
  placaYaMonitoreada: boolean;
}

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
              <button type="button" className={controlCls(false)} aria-label="Alejar" disabled={zoom <= 0.5} onClick={() => setZoom((z) => Math.max(0.5, z - 0.25))}>
                <ZoomOut className="h-3.5 w-3.5" aria-hidden />
              </button>
              <span className="w-12 text-center tabular-nums">{Math.round(zoom * 100)} %</span>
              <button type="button" className={controlCls(false)} aria-label="Acercar" disabled={zoom >= 4} onClick={() => setZoom((z) => Math.min(4, z + 0.25))}>
                <Icono nombre="buscar" className="h-3.5 w-3.5" />
              </button>
            </>
          )}
          <button type="button" className={controlCls(false)} onClick={descargar}>
            <Icono nombre="nube-bajar" className="h-3.5 w-3.5" />
            Descargar
          </button>
        </div>
      </div>
      <div className="flex-1 overflow-auto rounded-2xl border border-border bg-secondary">
        {vista === "imagen" && (
          // eslint-disable-next-line @next/next/no-img-element -- archivo privado servido por el proxy de la API, no un asset optimizable
          <img src={url} alt={`Original de ${d.nombreArchivo}`} style={{ width: `${zoom * 100}%`, maxWidth: "none" }} className="block" />
        )}
        {vista === "pdf" && <iframe title={`Original de ${d.nombreArchivo}`} src={url} className="h-full min-h-[70vh] w-full" />}
        {vista === "descarga" && (
          <p className="p-6 text-xs text-muted-foreground">El navegador no muestra archivos TIFF. Descárgalo para verlo junto al formulario.</p>
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
  tipoDocumento,
}: {
  campo: string;
  valor: string;
  onCambio: (v: string) => void;
  deshabilitado: boolean;
  tipoDocumento?: string;
}) {
  const tipo = tipoEntrada(campo);
  if (tipo === "documento")
    return (
      <select className={INPUT_CLS} value={valor} disabled={deshabilitado} onChange={(e) => onCambio(e.target.value)}>
        <option value="">—</option>
        {TIPOS_DOCUMENTO.map((t) => (
          <option key={t} value={t}>
            {ETIQUETA_TIPO_DOCUMENTO[t]}
          </option>
        ))}
        {valor && !(TIPOS_DOCUMENTO as readonly string[]).includes(valor) && <option value={valor}>{valor} (no reconocido)</option>}
      </select>
    );
  if (tipo === "booleano")
    return (
      <select className={INPUT_CLS} value={valor} disabled={deshabilitado} onChange={(e) => onCambio(e.target.value)}>
        <option value="">No se sabe</option>
        <option value="true">Sí, fotomulta</option>
        <option value="false">No, comparendo físico</option>
      </select>
    );
  const esNit = campo === "numero_documento_infractor" && tipoDocumento === "NIT";
  const error = esNit ? errorNit(valor) : null;
  return (
    <div className="flex flex-col gap-1">
      <input
        className={tipo === "fecha" || tipo === "hora" ? FECHA_CLS : INPUT_CLS}
        type={tipo === "fecha" ? "date" : tipo === "hora" ? "time" : "text"}
        value={valor}
        disabled={deshabilitado}
        inputMode={esNit ? "numeric" : undefined}
        placeholder={esNit ? "900.123.456-7" : undefined}
        aria-invalid={error ? true : undefined}
        aria-describedby={esNit ? `${campo}-nit-ayuda` : undefined}
        onChange={(e) => onCambio(esNit ? mascaraNit(e.target.value) : e.target.value)}
      />
      {esNit && (
        <span id={`${campo}-nit-ayuda`} className={`text-xs font-normal ${error ? "text-destructive" : "text-muted-foreground"}`}>
          {error ?? "Puedes escribir el dígito de verificación; el sistema lo valida (formato 900.123.456-7)."}
        </span>
      )}
    </div>
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
  const acciones = accionesDocumento({ estado: d.estado, reintentable: d.reintentable, fuenteDuplicado: d.duplicado?.fuente ?? null, tipoFallo: d.tipoFallo });
  // Asociado es un estado final y correcto: no quedan campos por revisar.
  const marcas = d.estado === "Asociado" ? {} : marcasRevision(d);
  const porRevisar = Object.keys(marcas).length;
  // GDC punto 6: el NIT guardado (base sin puntos) se muestra con la misma máscara con que se escribe.
  const tipoGuardado = d.campos.find((c) => c.nombreCampo === "tipo_documento_infractor")?.valorFinal;
  const iniciales = Object.fromEntries(
    d.campos.map((c) => {
      const v = valorInicial(c.nombreCampo, c.valorFinal);
      return [c.nombreCampo, c.nombreCampo === "numero_documento_infractor" && tipoGuardado?.trim().toUpperCase() === "NIT" && /^[\d.\s,-]+$/.test(v) ? mascaraNit(v) : v];
    }),
  );
  const [editados, setEditados] = useState<Record<string, string>>(iniciales);
  const [motivo, setMotivo] = useState("");
  const [modal, setModal] = useState<"guardar" | "descartar" | "reprocesar" | "asociar" | "comparar" | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const cambios = cambiosCampos(iniciales, editados);
  const sucio = Object.keys(cambios).length > 0;
  const editable = opera && acciones.editar;
  const nitInvalido = editados.tipo_documento_infractor === "NIT" && !!errorNit(editados.numero_documento_infractor ?? "");
  // Por qué no se puede registrar todavía (la API lo exige igual; esto lo explica en el botón).
  const bloqueoRegistro = sucio
    ? "Guarda los cambios antes de registrar"
    : d.numeroPorConfirmar
      ? "Corrige el número o confirma que está bien antes de registrar"
      : nitInvalido
        ? "Corrige el dígito de verificación del NIT antes de registrar"
        : undefined;

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
      const r = await apiFetch<{ estado: string; camposFaltantes: string[] }>(`/ocr/documentos/${d.id}/campos`, {
        method: "PUT",
        body: { campos: cambios, motivo: motivo.trim() || null },
      });
      toast.show(
        r.estado === "Completo" ? "Cambios guardados: el documento quedó completo." : r.estado === "Duplicado" ? "Cambios guardados: el número ya existe." : "Cambios guardados.",
        r.estado === "Duplicado" ? "error" : "success",
      );
    });

  // GDC punto 14: confirmar es guardar el número tal cual; queda en el historial con este motivo.
  const confirmarNumero = () =>
    ejecutar(async () => {
      const numero = d.campos.find((c) => c.nombreCampo === "numero_comparendo")?.valorFinal ?? null;
      await apiFetch(`/ocr/documentos/${d.id}/campos`, {
        method: "PUT",
        body: { campos: { numero_comparendo: numero }, motivo: "Número confirmado por el operador" },
      });
      toast.show("Número confirmado: ya puedes registrar el comparendo.", "success");
    });

  const registrar = () =>
    ejecutar(async () => {
      const r = await apiFetch<Registro>(`/ocr/documentos/${d.id}/registrar`, { method: "POST" });
      onRegistrado(r);
      toast.show(`Comparendo ${r.numero} registrado.`, "success");
    });

  const descartar = () =>
    ejecutar(async () => {
      await apiFetch(`/ocr/documentos/${d.id}/descartar`, { method: "POST", body: { motivo: motivo.trim() } });
      toast.show("Documento descartado.", "success");
    });

  // Contrato de asociar: 409 NO_ASOCIABLE / EVIDENCIA_EXISTENTE / EVIDENCIA_CAMBIO = la pantalla estaba desactualizada.
  const asociar = (reemplazar: boolean) =>
    ejecutar(async () => {
      try {
        const r = await apiFetch<{ numero: string }>(`/ocr/documentos/${d.id}/asociar`, {
          method: "POST",
          body: { reemplazar, evidenciaActualId: d.duplicado?.evidenciaActual?.archivoId ?? null },
        });
        toast.show(reemplazar ? `Documento reemplazado en el comparendo ${r.numero}.` : `Documento asociado al comparendo ${r.numero}.`, "success");
      } catch (e) {
        if (!(e instanceof ApiError) || e.status !== 409) throw e;
        toast.show(
          e.code === "EVIDENCIA_CAMBIO"
            ? "Otra persona cambió el documento del comparendo mientras lo revisabas. Actualizamos la pantalla: compara de nuevo antes de decidir."
            : e.code === "EVIDENCIA_EXISTENTE"
              ? "El comparendo ya tiene un documento. Actualizamos la pantalla: compara los dos antes de decidir."
              : e.message || "Este documento ya no se puede asociar. Actualizamos la pantalla.",
          "error",
        );
      }
    });

  const reintentar = () =>
    ejecutar(async () => {
      await apiFetch(`/ocr/documentos/${d.id}/reintentar`, { method: "POST" });
      toast.show("El documento volvió a la cola.", "success");
    });

  const reprocesar = () =>
    ejecutar(async () => {
      await apiFetch(`/ocr/documentos/${d.id}/reprocesar`, { method: "POST" });
      toast.show("El documento volvió a la cola para leerse de nuevo.", "success");
    });

  const dup = d.duplicado;
  const vistaDup = vistaDuplicado(d);
  const puedeAsociar = opera && acciones.asociar;
  const comparendoExistente = dup && dup.tipo === "comparendo" ? { id: dup.id, numero: dup.numero, fuente: dup.fuente && FUENTE_DUPLICADO[dup.fuente] ? FUENTE_DUPLICADO[dup.fuente] : dup.fuente, activo: dup.activo, pagado: dup.pagado } : null;
  // Arriba y al final del formulario, sin barra flotante: una barra pegada abajo con fondo tapaba los campos (B12-6).
  const botones = opera ? (
    <div className="flex flex-wrap justify-end gap-2">
      {acciones.reintentar && (
        <button type="button" className={controlCls(false)} disabled={ocupado} onClick={reintentar}>
          <Icono nombre="reintento" className="h-3.5 w-3.5" />
          Reintentar
        </button>
      )}
      {acciones.reprocesar && (
        <button
          type="button"
          className={controlCls(false)}
          disabled={ocupado || sucio || !!d.motivoNoReprocesable}
          title={d.motivoNoReprocesable ?? (sucio ? "Guarda o deshaz los cambios antes de reprocesar" : undefined)}
          onClick={() => setModal("reprocesar")}
        >
          <Icono nombre="reintento" className="h-3.5 w-3.5" />
          Reprocesar
        </button>
      )}
      {acciones.descartar && (
        <button type="button" className={controlCls(false)} disabled={ocupado} onClick={() => setModal("descartar")}>
          <Icono nombre="papelera" className="h-3.5 w-3.5" />
          Descartar
        </button>
      )}
      {editable && (
        <button type="button" className={controlCls(sucio)} disabled={ocupado || !sucio} onClick={() => setModal("guardar")}>
          Guardar cambios
        </button>
      )}
      {acciones.registrar && (
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !!bloqueoRegistro} title={bloqueoRegistro} onClick={registrar}>
          {ocupado ? "Registrando…" : "Registrar comparendo"}
        </button>
      )}
    </div>
  ) : null;
  return (
    <div className="flex flex-col gap-4">
      {botones}
      {d.estado === "Fallido" && (
        <InlineAlert tone="error" title={(d.tipoFallo && TIPO_FALLO[d.tipoFallo]) ?? "El documento falló"}>
          {d.mensajeError}
          {!d.reintentable &&
            (acciones.reprocesar && !d.motivoNoReprocesable
              ? " Puedes reprocesarlo para leerlo de nuevo, o descartarlo."
              : " No se reintenta: revisa el archivo y descártalo, o cárgalo de nuevo en otro paquete.")}
        </InlineAlert>
      )}

      {d.numeroPorConfirmar && d.avisoNumero && ["Completo", "Incompleto"].includes(d.estado) && (
        <InlineAlert tone="warning" title="Verifica el número antes de registrar">
          <p>{d.avisoNumero}</p>
          {editable && (
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <button
                type="button"
                className={controlCls(false)}
                disabled={ocupado || sucio}
                title={sucio ? "Guarda o deshaz los cambios antes de confirmar" : undefined}
                onClick={confirmarNumero}
              >
                <Icono nombre="check" className="h-3.5 w-3.5" />
                El número está bien
              </button>
              <span className="text-xs text-muted-foreground">Si está mal, corrígelo en el campo y guarda.</span>
            </div>
          )}
        </InlineAlert>
      )}

      {porRevisar > 0 && ["Completo", "Incompleto", "Duplicado"].includes(d.estado) && (
        <InlineAlert tone="warning" title={porRevisar === 1 ? "Revisa 1 campo marcado" : `Revisa ${porRevisar} campos marcados`}>
          Compáralos con el original antes de registrar.
        </InlineAlert>
      )}

      {dup && d.estado !== "Asociado" && (
        <InlineAlert tone="warning" title={vistaDup === "asociar" || vistaDup === "comparar" ? "Este comparendo ya existe" : "Este comparendo ya existe: no se puede registrar"}>
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
                  <button type="button" className="font-semibold text-flit-brand-ink hover:underline" onClick={() => onVer(dup.id)}>
                    ver el comparendo
                  </button>
                </dd>
              </>
            ) : (
              <>
                <dt className="font-semibold">Paquete</dt>
                <dd>
                  {dup.paquete} ·{" "}
                  <Link className="font-semibold text-flit-brand-ink hover:underline" href={`/ocr/documentos/${dup.id}`}>
                    ver el documento original
                  </Link>
                </dd>
              </>
            )}
          </dl>
          {dup.motivoNoAsociable && vistaDup !== "asociar" && vistaDup !== "comparar" && <p className="mt-1">{dup.motivoNoAsociable}</p>}
          {puedeAsociar && vistaDup === "asociar" && (
            <div className="mt-2 flex flex-col gap-1">
              <p>No tiene documento: puedes agregarle este archivo para que salga en sus próximos avisos.</p>
              <div>
                <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={() => setModal("asociar")}>
                  <Icono nombre="documento" className="h-3.5 w-3.5" />
                  Asociar al comparendo
                </button>
              </div>
            </div>
          )}
          {puedeAsociar && vistaDup === "comparar" && (
            <div className="mt-2 flex flex-col gap-1">
              <p>Ya tiene un documento: compáralos para decidir si lo reemplazas.</p>
              <div>
                <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={() => setModal("comparar")}>
                  <Icono nombre="documento" className="h-3.5 w-3.5" />
                  Comparar documentos
                </button>
              </div>
            </div>
          )}
          {acciones.editar && opera && <p className="mt-1">Si el número quedó mal leído, corrígelo abajo y guarda: se vuelve a validar.</p>}
        </InlineAlert>
      )}

      {d.campos.length > 0 && (
        <div className="flex flex-col gap-3">
          {d.campos.map((c) => {
            const faltante = d.camposFaltantes.includes(c.nombreCampo);
            const marca = marcas[c.nombreCampo];
            return (
              <label key={c.nombreCampo} className={ETIQUETA_CLS}>
                <span className="flex items-center justify-between gap-2">
                  <span className={faltante ? "text-destructive" : undefined}>
                    {ETIQUETA_CAMPO[c.nombreCampo] ?? c.nombreCampo}
                    {c.obligatorio && <span aria-hidden> <Icono nombre="asterisco" className="inline-block h-[0.6em] w-[0.6em] align-[0.3em]" /></span>}
                    {faltante && <span className="ml-1 font-normal">· falta o no es válido</span>}
                  </span>
                  <span className="flex items-center gap-2 font-normal">
                    {c.valorCorregido !== null && <span className="text-muted-foreground">corregido</span>}
                    {marca && !faltante ? (
                      <span className="inline-flex items-center gap-1 font-semibold text-[var(--badge-warning-fg)]">
                        <Icono nombre="alerta" className="h-3 w-3" />
                        Revisar
                      </span>
                    ) : c.esNull && !faltante ? (
                      <span className="text-muted-foreground">no leído</span>
                    ) : null}
                  </span>
                </span>
                <div className={faltante ? "rounded-xl ring-1 ring-destructive" : marca ? "rounded-xl ring-1 ring-[var(--badge-warning-fg)]" : undefined}>
                  <CampoEntrada
                    campo={c.nombreCampo}
                    valor={editados[c.nombreCampo] ?? ""}
                    deshabilitado={!editable || ocupado}
                    tipoDocumento={editados.tipo_documento_infractor}
                    onCambio={(v) => setEditados((e) => ({ ...e, [c.nombreCampo]: v }))}
                  />
                </div>
                {marca && !faltante && c.nombreCampo !== "fecha_notificacion" && <span className="text-xs font-normal text-[var(--badge-warning-fg)]">{marca}</span>}
                {c.nombreCampo === "fecha_notificacion" && (
                  <span className="text-xs font-normal text-muted-foreground">
                    En las fotomultas casi nunca viene impresa: se toma del sello de recibido o radicado de la empresa. Confírmala, porque de ella cuentan los plazos.
                  </span>
                )}
              </label>
            );
          })}
        </div>
      )}

      {botones && <div className="border-t border-border pt-3">{botones}</div>}

      {modal === "asociar" && comparendoExistente && (
        <ModalAsociar comparendo={comparendoExistente} documento={d} ocupado={ocupado} onClose={() => setModal(null)} onConfirmar={() => asociar(false)} />
      )}
      {modal === "comparar" && comparendoExistente && dup?.evidenciaActual && (
        <ModalComparar
          comparendo={comparendoExistente}
          entrante={{ archivoId: d.archivoId, nombreArchivo: d.nombreArchivo, tipoArchivo: d.tipoArchivo }}
          actual={dup.evidenciaActual}
          ocupado={ocupado}
          onClose={() => setModal(null)}
          onReemplazar={() => asociar(true)}
          onDescartar={() => setModal("descartar")}
        />
      )}

      <Modal icono="documento"
        open={modal === "guardar" || modal === "descartar" || modal === "reprocesar"}
        onClose={() => setModal(null)}
        busy={ocupado}
        title={modal === "descartar" ? "Descartar el documento" : modal === "reprocesar" ? "Leer de nuevo el documento" : "Guardar la corrección"}
        description={
          modal === "descartar"
            ? "El documento queda descartado con su motivo; no se registra ni se borra."
            : modal === "reprocesar"
              ? `Se vuelve a leer el documento (reproceso ${d.reprocesos + 1} de ${d.maximoReprocesos}). Lo que corregiste a mano se conserva; lo demás toma la nueva lectura.`
              : `Cambian ${Object.keys(cambios).length} campo(s). Cada cambio queda en el historial con tu usuario.`
        }
        footer={
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={() => setModal(null)}>
              Cancelar
            </button>
            {modal === "descartar" ? (
              <button type="button" className={BOTON_PELIGRO_CLS} disabled={ocupado || !motivo.trim()} onClick={descartar}>
                Descartar
              </button>
            ) : modal === "reprocesar" ? (
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={reprocesar}>
                Reprocesar
              </button>
            ) : (
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={guardar}>
                Guardar
              </button>
            )}
          </div>
        }
      >
        {modal !== "reprocesar" && (
          <label className={ETIQUETA_CLS}>
            {modal === "descartar" ? "Motivo (obligatorio)" : "Motivo (opcional)"}
            <input className={INPUT_CLS} value={motivo} maxLength={500} onChange={(e) => setMotivo(e.target.value)} />
          </label>
        )}
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
            { key: "usuario", header: "Usuario", render: (e) => (e.usuarioNombre ? <span title={e.usuarioId}>{e.usuarioNombre}</span> : <span title={e.usuarioId} className="font-mono text-xs text-muted-foreground">{e.usuarioId.slice(0, 8)}</span>) },
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
            { key: "n", header: "#", align: "right", render: (i) => (i.resultado === "REPROCESO" ? "—" : i.numero) },
            { key: "fecha", header: "Fecha", render: (i) => formatoFechaHora(i.intentadoEn) },
            { key: "resultado", header: "Resultado", render: (i) => (i.resultado === "EXITO" ? "Éxito" : i.resultado === "REPROCESO" ? "Reproceso pedido" : "Error") },
            {
              key: "error",
              header: "Detalle",
              render: (i) => (i.tipoError ? `${TIPO_FALLO[i.tipoError] ?? i.tipoError}${i.mensajeError ? `: ${i.mensajeError}` : ""}` : (i.mensajeError ?? "—")),
            },
            { key: "proximo", header: "Siguiente intento", render: (i) => formatoFechaHora(i.proximoReintentoEn) },
          ]}
        />
      </div>
    </div>
  );
}

function Revision({ id }: { id: string }) {
  const { sesion } = useSesion();
  const opera = !sesion?.usuario.esSuperAdmin && tienePermiso(sesion, P.ocrGestionar);
  const carga = useCarga(`ocr-documento:${id}`, (signal) => apiFetch<Documento>(`/ocr/documentos/${id}`, { signal }));
  const d = carga.datos;
  const [registro, setRegistro] = useState<Registro | null>(null);
  const setVerComparendo = useAbrirDetalle();
  useSondeo(!!d && hayEnCurso([d.estado]), carga.recargar);

  if (carga.estado === "error" && !d) return <InlineAlert tone="error">{carga.error}</InlineAlert>;
  if (!d) return <VehicleLoader label="Cargando documento…" />;

  return (
    <section className="flex flex-col gap-4">
      <Miga
        items={[
          { etiqueta: "Paquetes OCR", href: "/ocr" },
          { etiqueta: "Paquete", href: `/ocr/paquetes/${d.paqueteId}` },
          { etiqueta: d.numeroComparendoExtraido ?? d.nombreArchivo },
        ]}
        enlace={({ href, className, children }) => (
          <Link href={href} className={className}>
            {children}
          </Link>
        )}
      />
      <Encabezado
        titulo={d.numeroComparendoExtraido ?? "Documento sin número"}
        ilustracion="documentos"
        descripcion={
          <span className="flex flex-wrap items-center gap-2">
            <EstadoDocumento estado={d.estado} />
            {/* Versión del prompt, proveedor y modelo: solo los recibe el SuperAdmin, y aun así van en el title, no en la línea. */}
            <span
              title={[d.versionPrompt !== null ? `Prompt v${d.versionPrompt}` : null, d.proveedorModelo].filter(Boolean).join(" · ") || undefined}
            >
              {d.procesadoEn ? `Procesado ${formatoFechaHora(d.procesadoEn)}` : "En cola"}
              {d.reprocesos > 0 && ` · Reprocesado ${d.reprocesos} de ${d.maximoReprocesos}`}
              {d.motivoDescarte && ` · Descartado: ${d.motivoDescarte}`}
            </span>
          </span>
        }
      />
      {registro && (
        <div className="flex flex-col gap-2">
          <InlineAlert tone="success" title={`Comparendo ${registro.numero} registrado`}>
            <button type="button" className="font-semibold text-flit-brand-ink hover:underline" onClick={() => setVerComparendo(registro.comparendoId)}>
              Ver el comparendo
            </button>
          </InlineAlert>
          {registro.placa && !registro.placaYaMonitoreada && <OfertaPlacas placas={[registro.placa]} />}
          {registro.placa && registro.placaYaMonitoreada && <InlineAlert tone="info">La placa {registro.placa} ya está monitoreada.</InlineAlert>}
        </div>
      )}

      {d.estado === "Asociado" && (
        <InlineAlert tone="success" title="Documento asociado al comparendo">
          {d.comparendoId ? (
            <button type="button" className="font-semibold text-flit-brand-ink hover:underline" onClick={() => setVerComparendo(d.comparendoId!)}>
              Ver el comparendo
            </button>
          ) : (
            "El archivo quedó agregado al comparendo existente."
          )}
        </InlineAlert>
      )}

      {hayEnCurso([d.estado]) && <InlineAlert tone="info">El documento está en cola; esta vista se actualiza sola.</InlineAlert>}
      <div className="grid gap-6 lg:grid-cols-2">
        <Original d={d} />
        {/* La clave fuerza un formulario nuevo cada vez que cambian los valores guardados. */}
        <Formulario key={`${d.estado}:${d.revisadoEn ?? ""}:${d.campos.map((c) => c.valorFinal).join("|")}`} d={d}
          opera={opera}
          onCambio={carga.recargar}
          onRegistrado={setRegistro}
          onVer={setVerComparendo}
        />
      </div>
      <Historial d={d} />
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

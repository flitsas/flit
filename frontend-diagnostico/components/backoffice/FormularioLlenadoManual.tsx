"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { Plus, Trash2 } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Modal } from "@flit/ui/Modal";
import {
  AYUDA_CLS,
  BOTON_PRIMARIO_CLS,
  BOTON_REINTENTAR_CLS,
  BOTON_SECUNDARIO_CLS,
  CAMPO_CLS,
  ETIQUETA_CLS,
  FOCO_CLS,
  MARCA_CLS,
  OPCION_CLS,
} from "@/components/configuracion/estilos";
import { useFocoAtrapado } from "@/components/tablero/DetalleCelda";
import { TARJETA_DETALLE_CLS } from "@/components/tablero/vistasDetalle/comun";
import {
  llenarConsultaManual,
  obtenerFormularioManual,
  traducirErrorLlenado,
  type FormularioManual,
  type MotivoNoManual,
  type PersonaEnError,
  type RespuestaLlenado,
} from "@/lib/backoffice/api";
import {
  CLAVE_VEHICULO,
  MAX_COMPARENDOS,
  MAX_FUENTES_AVALUO,
  MAX_LICENCIAS,
  borradorVacio,
  comparendoVacio,
  construirCuerpo,
  esquemaDe,
  fuenteAvaluoVacia,
  idDeCampo,
  licenciaVacia,
  type AvaluoBorrador,
  type Borrador,
  type ErroresCampo,
  type RuesBorrador,
  type RuntPersonaBorrador,
  type SimitBorrador,
} from "@/lib/backoffice/esquemasManual";
import { textoTipoError } from "@/lib/tablero/detalle";

export interface FormularioLlenadoManualProps {
  consultaId: string;
  empresa: string;
  nombreFuente: string;
  identificador: string;
  /** Cierre pedido por Cancelar, la X, Escape o el clic fuera. */
  onClose: () => void;
  /** La API aceptó el llenado: la celda quedó COMPLETADO con origen MANUAL. */
  onLlenada: (respuesta: RespuestaLlenado) => void;
  /** Un 409 dijo que la celda cambió: la grilla debe volver a leerse. */
  onCeldaCambiada?: () => void;
  /** Elemento que recibe el foco al cerrar (el semáforo de la celda). */
  devolverFocoA?: HTMLElement | null;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; formulario: FormularioManual };

const MOTIVOS: Record<MotivoNoManual, string> = {
  FUENTE_NO_ADMITE_MANUAL: "Esta fuente no se llena a mano (el RUNT del vehículo y el impuesto departamental quedan fuera).",
  CELDA_NO_EN_ERROR: "Esta celda ya no está en error: no hay nada que llenar.",
  CELDA_BLOQUEADA_POR_RUNT: "Esta celda depende del RUNT del vehículo, que también falló. Se resuelve al reprocesar el RUNT.",
  SIN_PERSONAS_EN_ERROR: "Ninguna persona de esta celda está en error: no hay nada que llenar.",
};

const SEMAFOROS = [
  { valor: "", nombre: "Sin dato" },
  { valor: "VERDE", nombre: "Verde" },
  { valor: "AMARILLO", nombre: "Amarillo" },
  { valor: "ROJO", nombre: "Rojo" },
] as const;

const FUENTES_AVALUO = [
  { valor: "fasecolda", nombre: "Fasecolda" },
  { valor: "mercado_libre", nombre: "Mercado Libre" },
  { valor: "base_gravable", nombre: "Base gravable" },
] as const;

const ESTADOS_FUENTE_AVALUO = [
  { valor: "OK", nombre: "Con valor" },
  { valor: "SIN_DATOS", nombre: "Sin datos" },
  { valor: "ERROR", nombre: "Error" },
] as const;

const BOTON_FILA_CLS = `inline-flex items-center gap-1 self-start rounded-full px-2 py-1 text-xs font-semibold text-[var(--flit-brand-ink)] disabled:cursor-not-allowed disabled:opacity-50 dark:text-white ${FOCO_CLS}`;

/** Cambio de un borrador: el nuevo valor y la ruta del campo editado (su error se borra). */
type AlCambiar<B> = (borrador: B, campo: string) => void;

interface PropsCampo {
  campo: string;
  etiqueta: string;
  errores: ErroresCampo;
  ayuda?: string;
  children: (atributos: { id: string; "aria-invalid"?: true; "aria-describedby"?: string }) => ReactNode;
}

/** Rótulo, control y, si lo hay, el error junto al campo (enlazado por aria-describedby), con la misma ruta que el 422. */
function Campo({ campo, etiqueta, errores, ayuda, children }: PropsCampo) {
  const id = idDeCampo(campo);
  const error = errores[campo];
  const describe = [ayuda ? `${id}-ayuda` : null, error ? `${id}-error` : null].filter(Boolean).join(" ");
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <label htmlFor={id} className={ETIQUETA_CLS}>
        {etiqueta}
      </label>
      {children({ id, ...(error ? { "aria-invalid": true as const } : {}), ...(describe ? { "aria-describedby": describe } : {}) })}
      {ayuda && (
        <p id={`${id}-ayuda`} className={AYUDA_CLS}>
          {ayuda}
        </p>
      )}
      {error && (
        <InlineAlert tone="error" compact id={`${id}-error`}>
          {error}
        </InlineAlert>
      )}
    </div>
  );
}

function Texto(props: { campo: string; etiqueta: string; valor: string; errores: ErroresCampo; onChange: (v: string) => void; ayuda?: string; tipo?: "text" | "date"; numerico?: boolean }) {
  const { campo, etiqueta, valor, errores, onChange, ayuda, tipo = "text", numerico } = props;
  return (
    <Campo campo={campo} etiqueta={etiqueta} errores={errores} ayuda={ayuda}>
      {(a) => <input {...a} type={tipo} inputMode={numerico ? "numeric" : undefined} value={valor} onChange={(e) => onChange(e.target.value)} className={CAMPO_CLS} />}
    </Campo>
  );
}

function Seleccion(props: { campo: string; etiqueta: string; valor: string; errores: ErroresCampo; onChange: (v: string) => void; opciones: ReadonlyArray<{ valor: string; nombre: string }>; vacia?: string }) {
  const { campo, etiqueta, valor, errores, onChange, opciones, vacia } = props;
  return (
    <Campo campo={campo} etiqueta={etiqueta} errores={errores}>
      {(a) => (
        <select {...a} value={valor} onChange={(e) => onChange(e.target.value)} className={CAMPO_CLS}>
          {vacia !== undefined && <option value="">{vacia}</option>}
          {opciones.map((o) => (
            <option key={o.valor} value={o.valor}>
              {o.nombre}
            </option>
          ))}
        </select>
      )}
    </Campo>
  );
}

function Marca({ campo, etiqueta, valor, errores, onChange }: { campo: string; etiqueta: string; valor: boolean; errores: ErroresCampo; onChange: (v: boolean) => void }) {
  const id = idDeCampo(campo);
  const error = errores[campo];
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className={OPCION_CLS}>
        <input
          id={id}
          type="checkbox"
          checked={valor}
          onChange={(e) => onChange(e.target.checked)}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? `${id}-error` : undefined}
          className={MARCA_CLS}
        />
        {etiqueta}
      </label>
      {error && (
        <InlineAlert tone="error" compact id={`${id}-error`}>
          {error}
        </InlineAlert>
      )}
    </div>
  );
}

/** Fila de una lista (comparendo, fuente, licencia) con su título y «Quitar». */
function Fila({ titulo, onQuitar, children }: { titulo: string; onQuitar: () => void; children: ReactNode }) {
  return (
    <div role="group" aria-label={titulo} className={`${TARJETA_DETALLE_CLS} flex flex-col gap-3`}>
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm font-semibold text-flit-primary dark:text-white">{titulo}</span>
        <button type="button" onClick={onQuitar} className={BOTON_FILA_CLS}>
          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
          {`Quitar ${titulo.toLowerCase()}`}
        </button>
      </div>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">{children}</div>
    </div>
  );
}

function Agregar({ texto, onClick, deshabilitado }: { texto: string; onClick: () => void; deshabilitado?: boolean }) {
  return (
    <button type="button" onClick={onClick} disabled={deshabilitado} className={BOTON_FILA_CLS}>
      <Plus className="h-3.5 w-3.5" aria-hidden="true" />
      {texto}
    </button>
  );
}

function FormSimit({ b, raiz, errores, onChange }: { b: SimitBorrador; raiz: string; errores: ErroresCampo; onChange: AlCambiar<SimitBorrador> }) {
  const comparendo = (i: number, clave: keyof SimitBorrador["comparendos"][number]) => (v: string) =>
    onChange({ ...b, comparendos: b.comparendos.map((c, j) => (j === i ? { ...c, [clave]: v } : c)) }, `${raiz}.comparendos[${i}].${clave}`);
  return (
    <div className="flex flex-col gap-3">
      <Seleccion
        campo={`${raiz}.semaforo`}
        etiqueta="Semáforo"
        valor={b.semaforo}
        errores={errores}
        opciones={SEMAFOROS}
        onChange={(v) => onChange({ ...b, semaforo: v as SimitBorrador["semaforo"] }, `${raiz}.semaforo`)}
      />
      <p className={AYUDA_CLS}>{`Comparendos: ${b.comparendos.length}. La cantidad se calcula con la lista.`}</p>
      {b.comparendos.map((c, i) => (
        <Fila key={i} titulo={`Comparendo ${i + 1}`} onQuitar={() => onChange({ ...b, comparendos: b.comparendos.filter((_, j) => j !== i) }, `${raiz}.comparendos`)}>
          <Texto campo={`${raiz}.comparendos[${i}].numero`} etiqueta={`Número del comparendo ${i + 1}`} valor={c.numero} errores={errores} onChange={comparendo(i, "numero")} />
          <Texto campo={`${raiz}.comparendos[${i}].fecha`} etiqueta={`Fecha del comparendo ${i + 1}`} tipo="date" valor={c.fecha} errores={errores} onChange={comparendo(i, "fecha")} />
          <Texto campo={`${raiz}.comparendos[${i}].valor`} etiqueta={`Valor del comparendo ${i + 1} (pesos)`} numerico valor={c.valor} errores={errores} onChange={comparendo(i, "valor")} />
          <Texto campo={`${raiz}.comparendos[${i}].estado`} etiqueta={`Estado del comparendo ${i + 1}`} valor={c.estado} errores={errores} onChange={comparendo(i, "estado")} />
          <Texto campo={`${raiz}.comparendos[${i}].organismo`} etiqueta={`Organismo del comparendo ${i + 1}`} valor={c.organismo} errores={errores} onChange={comparendo(i, "organismo")} />
        </Fila>
      ))}
      <Agregar
        texto="Agregar comparendo"
        deshabilitado={b.comparendos.length >= MAX_COMPARENDOS}
        onClick={() => onChange({ ...b, comparendos: [...b.comparendos, comparendoVacio()] }, `${raiz}.comparendos`)}
      />
    </div>
  );
}

function FormAvaluo({ b, raiz, errores, onChange }: { b: AvaluoBorrador; raiz: string; errores: ErroresCampo; onChange: AlCambiar<AvaluoBorrador> }) {
  const fuente = (i: number, clave: keyof AvaluoBorrador["fuentes"][number]) => (v: string) =>
    onChange({ ...b, fuentes: b.fuentes.map((f, j) => (j === i ? { ...f, [clave]: v } : f)) }, `${raiz}.fuentes[${i}].${clave}`);
  return (
    <div className="flex flex-col gap-3">
      <Marca
        campo={`${raiz}.sin_valor_comercial`}
        etiqueta="Sin valor comercial"
        valor={b.sin_valor_comercial}
        errores={errores}
        onChange={(v) => onChange({ ...b, sin_valor_comercial: v }, `${raiz}.sin_valor_comercial`)}
      />
      {!b.sin_valor_comercial && (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Texto
            campo={`${raiz}.valor_sugerido`}
            etiqueta="Valor sugerido (pesos)"
            numerico
            valor={b.valor_sugerido}
            errores={errores}
            onChange={(v) => onChange({ ...b, valor_sugerido: v }, `${raiz}.valor_sugerido`)}
          />
          <Seleccion
            campo={`${raiz}.fuente_principal`}
            etiqueta="Fuente principal"
            vacia="Elige la fuente"
            valor={b.fuente_principal}
            errores={errores}
            opciones={FUENTES_AVALUO}
            onChange={(v) => onChange({ ...b, fuente_principal: v as AvaluoBorrador["fuente_principal"] }, `${raiz}.fuente_principal`)}
          />
        </div>
      )}
      <p className={AYUDA_CLS}>La fecha del dato la pone el servidor.</p>
      {b.fuentes.map((f, i) => (
        <Fila key={i} titulo={`Fuente de avalúo ${i + 1}`} onQuitar={() => onChange({ ...b, fuentes: b.fuentes.filter((_, j) => j !== i) }, `${raiz}.fuentes`)}>
          <Seleccion campo={`${raiz}.fuentes[${i}].fuente`} etiqueta={`Fuente ${i + 1}`} vacia="Elige la fuente" valor={f.fuente} errores={errores} opciones={FUENTES_AVALUO} onChange={fuente(i, "fuente")} />
          <Seleccion campo={`${raiz}.fuentes[${i}].estado`} etiqueta={`Estado de la fuente ${i + 1}`} vacia="Elige el estado" valor={f.estado} errores={errores} opciones={ESTADOS_FUENTE_AVALUO} onChange={fuente(i, "estado")} />
          <Texto campo={`${raiz}.fuentes[${i}].valor`} etiqueta={`Valor de la fuente ${i + 1} (pesos)`} numerico valor={f.valor} errores={errores} onChange={fuente(i, "valor")} />
          <Texto campo={`${raiz}.fuentes[${i}].muestras`} etiqueta={`Muestras de la fuente ${i + 1}`} numerico valor={f.muestras} errores={errores} onChange={fuente(i, "muestras")} />
        </Fila>
      ))}
      <Agregar
        texto="Agregar fuente"
        deshabilitado={b.fuentes.length >= MAX_FUENTES_AVALUO}
        onClick={() => onChange({ ...b, fuentes: [...b.fuentes, fuenteAvaluoVacia()] }, `${raiz}.fuentes`)}
      />
    </div>
  );
}

function FormRuntPersona({ b, raiz, errores, onChange }: { b: RuntPersonaBorrador; raiz: string; errores: ErroresCampo; onChange: AlCambiar<RuntPersonaBorrador> }) {
  const licencia = (i: number, clave: keyof RuntPersonaBorrador["licencias"][number]) => (v: string) =>
    onChange({ ...b, licencias: b.licencias.map((l, j) => (j === i ? { ...l, [clave]: v } : l)) }, `${raiz}.licencias[${i}].${clave}`);
  return (
    <div className="flex flex-col gap-3">
      <Marca campo={`${raiz}.inscrito`} etiqueta="Inscrito en el RUNT" valor={b.inscrito} errores={errores} onChange={(v) => onChange({ ...b, inscrito: v }, `${raiz}.inscrito`)} />
      <Texto
        campo={`${raiz}.estado_conductor`}
        etiqueta="Estado del conductor"
        valor={b.estado_conductor}
        errores={errores}
        onChange={(v) => onChange({ ...b, estado_conductor: v }, `${raiz}.estado_conductor`)}
      />
      {b.licencias.map((l, i) => (
        <Fila key={i} titulo={`Licencia ${i + 1}`} onQuitar={() => onChange({ ...b, licencias: b.licencias.filter((_, j) => j !== i) }, `${raiz}.licencias`)}>
          <Texto campo={`${raiz}.licencias[${i}].categoria`} etiqueta={`Categoría de la licencia ${i + 1}`} valor={l.categoria} errores={errores} onChange={licencia(i, "categoria")} />
          <Texto campo={`${raiz}.licencias[${i}].estado`} etiqueta={`Estado de la licencia ${i + 1}`} valor={l.estado} errores={errores} onChange={licencia(i, "estado")} />
          <Texto campo={`${raiz}.licencias[${i}].vigente_hasta`} etiqueta={`Vigente hasta (licencia ${i + 1})`} tipo="date" valor={l.vigente_hasta} errores={errores} onChange={licencia(i, "vigente_hasta")} />
        </Fila>
      ))}
      <Agregar
        texto="Agregar licencia"
        deshabilitado={b.licencias.length >= MAX_LICENCIAS}
        onClick={() => onChange({ ...b, licencias: [...b.licencias, licenciaVacia()] }, `${raiz}.licencias`)}
      />
    </div>
  );
}

function FormRues({ b, raiz, errores, onChange }: { b: RuesBorrador; raiz: string; errores: ErroresCampo; onChange: AlCambiar<RuesBorrador> }) {
  return (
    <div className="flex flex-col gap-3">
      <Marca campo={`${raiz}.encontrada`} etiqueta="Matrícula encontrada en el RUES" valor={b.encontrada} errores={errores} onChange={(v) => onChange({ ...b, encontrada: v }, `${raiz}.encontrada`)} />
      <Texto
        campo={`${raiz}.estado_matricula`}
        etiqueta="Estado de la matrícula"
        valor={b.estado_matricula}
        errores={errores}
        onChange={(v) => onChange({ ...b, estado_matricula: v }, `${raiz}.estado_matricula`)}
      />
      <Texto
        campo={`${raiz}.ultima_renovacion`}
        etiqueta="Última renovación"
        ayuda="AAAA-MM-DD o solo el año (AAAA)."
        valor={b.ultima_renovacion}
        errores={errores}
        onChange={(v) => onChange({ ...b, ultima_renovacion: v }, `${raiz}.ultima_renovacion`)}
      />
    </div>
  );
}

/** El formulario del esquema del borrador (SIMIT, avalúo, RUNT persona o RUES). */
function FormEsquema({ b, raiz, errores, onChange }: { b: Borrador; raiz: string; errores: ErroresCampo; onChange: AlCambiar<Borrador> }) {
  switch (b.tipo) {
    case "SIMIT":
      return <FormSimit b={b} raiz={raiz} errores={errores} onChange={onChange} />;
    case "AVALUO":
      return <FormAvaluo b={b} raiz={raiz} errores={errores} onChange={onChange} />;
    case "RUNT_PERSONA":
      return <FormRuntPersona b={b} raiz={raiz} errores={errores} onChange={onChange} />;
    case "RUES":
      return <FormRues b={b} raiz={raiz} errores={errores} onChange={onChange} />;
  }
}

const ROL: Record<PersonaEnError["rol"], string> = { PROPIETARIO: "Propietario", LOCATARIO: "Locatario" };

/** Sección de una persona en error: rol, orden, documento enmascarado tal como llega y el formulario de su consulta. */
function SeccionPersona(props: { persona: PersonaEnError; indice: number; b: Borrador; errores: ErroresCampo; onChange: AlCambiar<Borrador> }) {
  const { persona, indice, b, errores, onChange } = props;
  const campoPersona = `personas[${indice}].persona_id`;
  const error = errores[campoPersona];
  return (
    <fieldset id={idDeCampo(campoPersona)} className={`${TARJETA_DETALLE_CLS} flex flex-col gap-3`}>
      <legend className="px-1 text-sm font-semibold text-flit-primary dark:text-white">
        {`${ROL[persona.rol]} ${persona.orden} · ${persona.tipo_documento} ${persona.documento_enmascarado}`}
      </legend>
      <p className={AYUDA_CLS}>
        {`${textoTipoError(persona.tipo_error)}${persona.declarado_por_cliente ? " Documento declarado por el cliente." : ""}`}
      </p>
      {error && (
        <InlineAlert tone="error" compact>
          {error}
        </InlineAlert>
      )}
      <FormEsquema b={b} raiz={`personas[${indice}].resultado`} errores={errores} onChange={onChange} />
    </fieldset>
  );
}

function borradoresIniciales(f: FormularioManual): Record<string, Borrador> {
  if (!f.personas) return { [CLAVE_VEHICULO]: borradorVacio(esquemaDe(f.fuente)) };
  return Object.fromEntries(f.personas.map((p) => [p.persona_id, borradorVacio(esquemaDe(f.fuente, p.consulta))]));
}

/**
 * Llenado manual de una celda en ERROR desde el backoffice FLIT (HU #13802, F06-H5; F06-C8..C11, C14). Diálogo de @flit/ui
 * con el foco atrapado que pide GET /backoffice/consultas/{cid}/manual?empresa= y pinta un formulario por tipo de resultado:
 * SIMIT (vehículo o persona), avalúo, RUNT persona o RUES; en las fuentes de persona, una sección por persona en error con su
 * documento enmascarado. Valida la forma antes de mandar el PUT; los errores (propios o el 422 de la API) van junto a su campo
 * y los 409 como aviso. Cuatro estados: cargando, error con Reintentar, sin llenado posible (motivo) y formulario.
 */
export function FormularioLlenadoManual({ consultaId, empresa, nombreFuente, identificador, onClose, onLlenada, onCeldaCambiada, devolverFocoA }: FormularioLlenadoManualProps) {
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [borradores, setBorradores] = useState<Record<string, Borrador>>({});
  const [errores, setErrores] = useState<ErroresCampo>({});
  const [aviso, setAviso] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [enfocarError, setEnfocarError] = useState(0);
  const ancla = useRef<HTMLDivElement>(null);
  useFocoAtrapado(ancla, devolverFocoA);

  useEffect(() => {
    const control = new AbortController();
    obtenerFormularioManual(consultaId, empresa, control.signal).then(
      (formulario) => {
        if (control.signal.aborted) return;
        setBorradores(borradoresIniciales(formulario));
        setCarga({ tipo: "listo", formulario });
      },
      (e: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", mensaje: traducirErrorLlenado(e).mensaje });
      },
    );
    return () => control.abort();
  }, [consultaId, empresa, intento]);

  // Tras marcar errores, el foco va al primer campo inválido.
  useEffect(() => {
    if (enfocarError === 0) return;
    ancla.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [enfocarError]);

  function cambiar(clave: string): AlCambiar<Borrador> {
    return (borrador, campo) => {
      setBorradores((actual) => ({ ...actual, [clave]: borrador }));
      setErrores((actual) => {
        if (!Object.keys(actual).some((c) => c === campo || c.startsWith(`${campo}.`) || c.startsWith(`${campo}[`))) return actual;
        return Object.fromEntries(Object.entries(actual).filter(([c]) => !(c === campo || c.startsWith(`${campo}.`) || c.startsWith(`${campo}[`))));
      });
    };
  }

  async function enviar() {
    if (carga.tipo !== "listo" || enviando) return;
    setAviso(null);
    const { cuerpo, errores: propios } = construirCuerpo(carga.formulario, borradores);
    if (!cuerpo) {
      setErrores(propios);
      setEnfocarError((n) => n + 1);
      return;
    }
    setErrores({});
    setEnviando(true);
    try {
      const respuesta = await llenarConsultaManual(consultaId, empresa, cuerpo);
      onLlenada(respuesta);
    } catch (e) {
      const error = traducirErrorLlenado(e);
      if (error.tipo === "CAMPO" && error.campo && document.getElementById(idDeCampo(error.campo))) {
        setErrores({ [error.campo]: error.mensaje });
        setEnfocarError((n) => n + 1);
      } else {
        setAviso(error.mensaje);
      }
      if (error.recargarGrilla) onCeldaCambiada?.();
    } finally {
      setEnviando(false);
    }
  }

  let contenido: ReactNode;
  let llenable = false;
  if (carga.tipo === "cargando") {
    contenido = <VehicleLoader label="Cargando el formulario…" />;
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo abrir el formulario"
        action={
          <button
            type="button"
            onClick={() => {
              setCarga({ tipo: "cargando" });
              setIntento((n) => n + 1);
            }}
            className={BOTON_REINTENTAR_CLS}
          >
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  } else if (!carga.formulario.admite_manual) {
    contenido = (
      <InlineAlert tone="warning" title="No se puede llenar a mano">
        {(carga.formulario.motivo && MOTIVOS[carga.formulario.motivo]) || "Esta celda no admite el llenado manual."}
      </InlineAlert>
    );
  } else {
    llenable = true;
    const f = carga.formulario;
    contenido = (
      <form
        noValidate
        aria-label={`Resultado de ${nombreFuente}`}
        onSubmit={(e) => {
          e.preventDefault();
          void enviar();
        }}
        className="flex flex-col gap-4"
      >
        <p className={AYUDA_CLS}>
          {`La celda está en error (${textoTipoError(f.tipo_error)}) y sigue así hasta que confirmes. El dato queda registrado como llenado a mano con tu usuario.`}
        </p>
        {aviso && (
          <InlineAlert tone="error" title="No se guardó el llenado">
            {aviso}
          </InlineAlert>
        )}
        {f.personas
          ? f.personas.map((p, i) => (
              <SeccionPersona key={p.persona_id} persona={p} indice={i} b={borradores[p.persona_id]} errores={errores} onChange={cambiar(p.persona_id)} />
            ))
          : borradores[CLAVE_VEHICULO] && <FormEsquema b={borradores[CLAVE_VEHICULO]} raiz="resultado" errores={errores} onChange={cambiar(CLAVE_VEHICULO)} />}
      </form>
    );
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={enviando}
      size="lg"
      zClassName="z-[110]"
      title={`Llenar a mano · ${nombreFuente} · ${identificador}`}
      footer={
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" onClick={onClose} disabled={enviando} className={BOTON_SECUNDARIO_CLS}>
            Cancelar
          </button>
          {llenable && (
            <button type="button" onClick={() => void enviar()} disabled={enviando} aria-busy={enviando} className={BOTON_PRIMARIO_CLS}>
              Confirmar llenado
            </button>
          )}
        </div>
      }
    >
      <div ref={ancla}>{contenido}</div>
    </Modal>
  );
}

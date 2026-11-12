"use client";

import { useRef, useState, type FormEvent } from "react";
import { Icono } from "@flit/ui/Icono";
import { INTERRUPTOR_ALERTA_CLS, INTERRUPTOR_CLS, INTERRUPTOR_PERILLA_CLS } from "@flit/ui/Interruptor";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { etiquetaKit } from "@flit/ui/StatusBadge";
import { VehicleLoader } from "@flit/ui/Loader";
import { ApiError } from "@/lib/api";
import {
  AVISO_FUENTE_YA_NO_DISPONIBLE,
  AYUDA_FUENTE_NO_DISPONIBLE,
  FUENTES,
  estaDisponible,
  etiquetaNit,
  guardarContratoEmpresa,
  nombreFuente,
  obtenerContratoEmpresa,
  ordenarFuentes,
  type ContratoBackoffice,
  type EmpresaBackoffice,
  type Fuente,
  type NivelContrato,
} from "@/lib/configuracion";
import {
  AYUDA_CLS,
  BOTON_PRIMARIO_CLS,
  BOTON_REINTENTAR_CLS,
  CAMPO_CLS,
  ETIQUETA_CLS,
  FOCO_CLS,
  MARCA_CLS,
  OPCION_CLS,
  TARJETA_CLS,
} from "./estilos";
import { TOOLTIP_CLS } from "@/components/parametrizacion/estilos";
import { SelectorEmpresaBackoffice } from "@/components/backoffice/SelectorEmpresaBackoffice";

/** Código del 422 al habilitar una fuente que el backend no declara disponible (HU #13863). */
const CODIGO_FUENTE_NO_DISPONIBLE = "FUENTE_NO_DISPONIBLE";

/**
 * Pestaña Contrato. Lo contratado por la compañía del token va siempre en solo lectura. Con la capacidad editarContrato
 * (backoffice FLIT: permiso diagnostico.configuracion.contrato.editar y tenant_type=FLIT) se suma el editor del contrato
 * de la empresa elegida en el selector de empresas (SelectorEmpresaBackoffice, compartido con el listado de diagnósticos
 * del backoffice; GET /backoffice/empresas, HU #13741; escenarios 7 y 8): se busca por nombre o NIT, sin escribir su
 * identificador, y el editor no se abre sin una empresa elegida. Solo ese perfil pide la lista: un admin de compañía nunca
 * llama al endpoint.
 * La disponibilidad de cada fuente sale solo de `fuentes_disponibles` del contrato (HU #13865, gate Q7 = a): una no
 * disponible y no habilitada queda bloqueada con su ayuda; una habilitada que dejó de estar disponible se conserva
 * marcada, con aviso, y se puede quitar.
 */
export function PestanaContrato({ contrato, puedeEditar }: { contrato: NivelContrato | null; puedeEditar: boolean }) {
  return (
    <>
      {contrato && <ContratoSoloLectura contrato={contrato} />}
      {puedeEditar && <EditorContratoBackoffice />}
    </>
  );
}

function ListaFuentes({ fuentes }: { fuentes: Fuente[] }) {
  if (fuentes.length === 0) return <p className={AYUDA_CLS}>El contrato no tiene fuentes habilitadas.</p>;
  return (
    <ul className="flex flex-wrap gap-2" aria-label="Fuentes habilitadas">
      {ordenarFuentes(fuentes).map((f) => (
        <li key={f} {...etiquetaKit("info")}>
          {nombreFuente(f)}
        </li>
      ))}
    </ul>
  );
}

function ContratoSoloLectura({ contrato }: { contrato: NivelContrato }) {
  return (
    <section aria-labelledby="contrato-titulo" className={`${TARJETA_CLS} flex flex-col gap-4`}>
      <div className="flex items-start gap-3">
        <Icono nombre="documento" className="mt-0.5 h-5 w-5 shrink-0 text-flit-brand" />
        <div>
          <h2 id="contrato-titulo" className="text-lg font-semibold text-flit-primary">
            Lo contratado por tu compañía
          </h2>
          <p className={AYUDA_CLS}>Solo lectura: el contrato lo administra el backoffice FLIT.</p>
        </div>
      </div>
      <dl className="grid gap-4 sm:grid-cols-[auto_1fr] sm:gap-x-6">
        <dt className={ETIQUETA_CLS}>Fuentes habilitadas</dt>
        <dd>
          <ListaFuentes fuentes={contrato.fuentes_habilitadas} />
        </dd>
        <dt className={ETIQUETA_CLS}>Límite de vehículos por lote</dt>
        <dd className="text-sm font-semibold text-flit-primary">{contrato.limite_vehiculos_lote}</dd>
      </dl>
    </section>
  );
}

type Carga =
  | { tipo: "vacio" }
  | { tipo: "cargando" }
  | { tipo: "error"; mensaje: string }
  | { tipo: "listo"; contrato: ContratoBackoffice };

type Guardado = { tipo: "inactivo" } | { tipo: "guardando" } | { tipo: "guardado"; recortadas: Fuente[] } | { tipo: "error"; mensaje: string; reintentable: boolean };

interface Borrador {
  fuentes: Fuente[];
  limite: string;
  suspendida: boolean;
}

function borradorDe(c: ContratoBackoffice): Borrador {
  return { fuentes: ordenarFuentes(c.fuentes_habilitadas), limite: String(c.limite_vehiculos_lote), suspendida: c.redaccion_ia_suspendida };
}

function EditorContratoBackoffice() {
  const [elegida, setElegida] = useState<EmpresaBackoffice | null>(null);
  const [carga, setCarga] = useState<Carga>({ tipo: "vacio" });
  const [borrador, setBorrador] = useState<Borrador | null>(null);
  const [guardado, setGuardado] = useState<Guardado>({ tipo: "inactivo" });
  const [limiteInvalido, setLimiteInvalido] = useState(false);
  const [errorFuentes, setErrorFuentes] = useState<string | null>(null);
  const empresaCargada = useRef("");
  const peticion = useRef<AbortController | null>(null);

  async function cargar(id: string) {
    peticion.current?.abort();
    const controller = new AbortController();
    peticion.current = controller;
    empresaCargada.current = id;
    setCarga({ tipo: "cargando" });
    setGuardado({ tipo: "inactivo" });
    setErrorFuentes(null);
    try {
      const contrato = await obtenerContratoEmpresa(id, controller.signal);
      if (controller.signal.aborted) return;
      setBorrador(borradorDe(contrato));
      setCarga({ tipo: "listo", contrato });
    } catch (e) {
      if (controller.signal.aborted) return;
      setCarga({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo cargar el contrato." });
    }
  }

  function alElegir(empresa: EmpresaBackoffice) {
    setElegida(empresa);
    void cargar(empresa.id);
  }

  async function guardar() {
    if (carga.tipo !== "listo" || !borrador) return;
    const limite = Number(borrador.limite);
    if (!Number.isInteger(limite) || limite <= 0) {
      setLimiteInvalido(true);
      return;
    }
    setLimiteInvalido(false);
    setErrorFuentes(null);
    setGuardado({ tipo: "guardando" });
    try {
      const anterior = carga.contrato.fuentes_por_defecto;
      const contrato = await guardarContratoEmpresa(empresaCargada.current, {
        fuentes_habilitadas: borrador.fuentes,
        limite_vehiculos_lote: limite,
        redaccion_ia_suspendida: borrador.suspendida,
      });
      setBorrador(borradorDe(contrato));
      setCarga({ tipo: "listo", contrato });
      setGuardado({ tipo: "guardado", recortadas: anterior.filter((f) => !contrato.fuentes_por_defecto.includes(f)) });
    } catch (e) {
      if (e instanceof ApiError && e.status === 422 && e.code === CODIGO_FUENTE_NO_DISPONIBLE) {
        // El error es del campo Fuentes habilitadas (el 422 del contrato no trae `campo`): se muestra sobre él.
        setErrorFuentes(e.message);
        setGuardado({ tipo: "inactivo" });
        return;
      }
      const reintentable = !(e instanceof ApiError) || e.status >= 500;
      setGuardado({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo guardar el contrato.", reintentable });
    }
  }

  function alGuardar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    void guardar();
  }

  function alternar(fuente: Fuente) {
    setErrorFuentes(null);
    setBorrador((b) => (b ? { ...b, fuentes: b.fuentes.includes(fuente) ? b.fuentes.filter((f) => f !== fuente) : ordenarFuentes([...b.fuentes, fuente]) } : b));
  }

  const ocupado = guardado.tipo === "guardando";

  return (
    <section aria-labelledby="backoffice-titulo" className={`${TARJETA_CLS} flex flex-col gap-5`}>
      <div>
        <h2 id="backoffice-titulo" className="text-lg font-semibold text-flit-primary">
          Contrato de una empresa (backoffice FLIT)
        </h2>
        <p className={AYUDA_CLS}>Busca la empresa por nombre o NIT para ver y editar su contrato.</p>
      </div>

      <SelectorEmpresaBackoffice
        id="contrato-empresa"
        value={elegida?.id ?? ""}
        onChange={alElegir}
        disabled={ocupado}
        avisoSinElegir="Todavía no has elegido una empresa."
      />

      {elegida && (
        <div className="flex items-start gap-3">
          <Icono nombre="empresas" className="mt-0.5 h-5 w-5 shrink-0 text-flit-brand" />
          <div>
            <h3 className="text-base font-semibold text-flit-primary">{elegida.nombre}</h3>
            <p className={AYUDA_CLS}>{etiquetaNit(elegida.nit)}</p>
          </div>
        </div>
      )}
      {carga.tipo === "cargando" && <VehicleLoader label="Cargando contrato…" />}
      {carga.tipo === "error" && (
        <InlineAlert
          tone="error"
          title="No se pudo cargar el contrato"
          action={
            <button type="button" onClick={() => void cargar(empresaCargada.current)} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          }
        >
          {carga.mensaje}
        </InlineAlert>
      )}

      {carga.tipo === "listo" && borrador && (
        <form onSubmit={alGuardar} className="flex flex-col gap-5" noValidate aria-label="Editar contrato">
          <fieldset className="flex flex-col gap-2" aria-describedby={errorFuentes ? "contrato-fuentes-error" : undefined}>
            <legend className={ETIQUETA_CLS}>Fuentes habilitadas</legend>
            {errorFuentes && (
              <InlineAlert tone="error" compact id="contrato-fuentes-error">
                {errorFuentes}
              </InlineAlert>
            )}
            <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {FUENTES.map(({ codigo, nombre }) => {
                const disponible = estaDisponible(codigo, carga.contrato.fuentes_disponibles);
                const enContrato = carga.contrato.fuentes_habilitadas.includes(codigo);
                return (
                  <CasillaFuenteContrato
                    key={codigo}
                    codigo={codigo}
                    nombre={nombre}
                    marcada={borrador.fuentes.includes(codigo)}
                    estado={disponible ? "disponible" : enContrato ? "ya-no-disponible" : "bloqueada"}
                    disabled={ocupado}
                    onAlternar={alternar}
                  />
                );
              })}
            </div>
            <p className={AYUDA_CLS}>Al quitar una fuente habilitada se recortan las fuentes por defecto de la empresa.</p>
          </fieldset>

          <div className="flex max-w-xs flex-col gap-1">
            <label htmlFor="contrato-limite" className={ETIQUETA_CLS}>
              Límite de vehículos por lote
            </label>
            <input
              id="contrato-limite"
              type="number"
              min={1}
              step={1}
              value={borrador.limite}
              onChange={(e) => setBorrador({ ...borrador, limite: e.target.value })}
              disabled={ocupado}
              aria-invalid={limiteInvalido || undefined}
              aria-describedby={limiteInvalido ? "contrato-limite-error" : undefined}
              className={CAMPO_CLS}
            />
            {limiteInvalido && (
              <InlineAlert tone="error" compact id="contrato-limite-error">
                El límite debe ser un número entero mayor que cero.
              </InlineAlert>
            )}
          </div>

          <div className="flex items-center justify-between gap-4 rounded-xl border border-border px-4 py-3">
            <div>
              <p id="contrato-ia" className={ETIQUETA_CLS}>
                Suspender la redacción con IA
              </p>
              <p className={AYUDA_CLS}>Interruptor de emergencia: los dictámenes salen sin redacción automática mientras esté activo.</p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={borrador.suspendida}
              aria-labelledby="contrato-ia"
              disabled={ocupado}
              onClick={() => setBorrador({ ...borrador, suspendida: !borrador.suspendida })}
              className={`${INTERRUPTOR_CLS} ${INTERRUPTOR_ALERTA_CLS} ${FOCO_CLS}`}
            >
              <span
                aria-hidden="true"
                className={INTERRUPTOR_PERILLA_CLS}
              />
            </button>
          </div>

          {guardado.tipo === "error" && (
            <InlineAlert
              tone="error"
              title="No se guardó el contrato"
              action={
                guardado.reintentable ? (
                  <button type="button" onClick={() => void guardar()} className={BOTON_REINTENTAR_CLS}>
                    Reintentar
                  </button>
                ) : undefined
              }
            >
              {guardado.mensaje}
            </InlineAlert>
          )}
          {guardado.tipo === "guardado" && (
            <InlineAlert tone="success">
              <span>Contrato guardado.</span>
              {guardado.recortadas.length > 0 && ` Se quitaron de las fuentes por defecto: ${guardado.recortadas.map(nombreFuente).join(", ")}.`}
            </InlineAlert>
          )}

          <div className="flex justify-end">
            <button type="submit" disabled={ocupado} className={BOTON_PRIMARIO_CLS}>
              <Icono nombre="nube-subir" className="h-4 w-4" />
              {ocupado ? "Guardando…" : "Guardar contrato"}
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

/**
 * Casilla de una fuente en el editor del contrato. Bloqueada: desmarcada, con aria-disabled (sigue enfocable para que la
 * ayuda llegue por teclado) y el tooltip asociado por aria-describedby; no baja la opacidad para conservar el contraste
 * AA. Ya no disponible: marcada según el borrador, con el aviso asociado, y se puede desmarcar.
 */
function CasillaFuenteContrato({
  codigo,
  nombre,
  marcada,
  estado,
  disabled,
  onAlternar,
}: {
  codigo: Fuente;
  nombre: string;
  marcada: boolean;
  estado: "disponible" | "bloqueada" | "ya-no-disponible";
  disabled: boolean;
  onAlternar: (fuente: Fuente) => void;
}) {
  const id = `contrato-fuente-${codigo}`;
  const ayudaId = `${id}-ayuda`;
  const bloqueada = estado === "bloqueada";
  return (
    <div className="group relative flex flex-col gap-1">
      <label className={`${OPCION_CLS} ${bloqueada ? "cursor-not-allowed" : ""}`}>
        <input
          type="checkbox"
          name="fuentes_habilitadas"
          value={codigo}
          checked={marcada}
          onChange={() => {
            if (!bloqueada) onAlternar(codigo);
          }}
          aria-disabled={bloqueada || undefined}
          aria-describedby={estado === "disponible" ? undefined : ayudaId}
          disabled={disabled}
          className={MARCA_CLS}
        />
        <span className="flex-1">{nombre}</span>
        {bloqueada && <Icono nombre="candado" className="h-3.5 w-3.5 text-muted-foreground" />}
        {estado === "ya-no-disponible" && <Icono nombre="alerta" className="h-3.5 w-3.5 text-[var(--badge-warning-fg)]" />}
      </label>
      {bloqueada && (
        <span role="tooltip" id={ayudaId} className={TOOLTIP_CLS}>
          {AYUDA_FUENTE_NO_DISPONIBLE}
        </span>
      )}
      {estado === "ya-no-disponible" && (
        <p id={ayudaId} className="px-1 text-xs font-medium text-[var(--badge-warning-fg)]">
          {AVISO_FUENTE_YA_NO_DISPONIBLE}
        </p>
      )}
    </div>
  );
}

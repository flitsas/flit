"use client";

// HU #14245 / #14244 — Requisitos del canal OT. Monta el MISMO paso Requisitos del portal web (FormularioRequisitos: tarjetas
// «Datos comerciales», «Prenda», «Documentos», Drop, errores y reintento de subida) dándole un contexto del wizard propio del
// punto. El gate a Pago lo decide el servidor al pulsar Continuar; la validación local solo da feedback inmediato.
import { AlertTriangle } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { ProveedorWizard, type ApiWizard, type EstadoSeccion } from "@/components/tramite/ContextoWizard";
import { Btn, Card, Nav } from "@/components/tramite/piezas";
import { FormularioRequisitos } from "@/components/tramite/renderers/FormularioRequisitos";
import type {
  ClientePortal,
  CompletitudRequisitosPunto,
  DefinicionTipo,
  DocumentoRequerido,
  MatrizRequisitosPunto,
  Solicitud,
} from "@/lib/api/cliente";
import { ApiError, mensajeDeFallo } from "@/lib/api/errores";
import { GRUPO_POR_SECCION, type SeccionWizard } from "@/lib/tramite/pasos";
import { MOTIVO_REQUISITOS } from "@/lib/tramite/requisitos";

type Props = {
  cliente: ClientePortal;
  solicitudId: string;
  tipoCodigo: string;
  placa: string;
  onAtras: () => void;
  onContinuarPago: () => void;
  onAviso: (titulo: string, mensaje: string, tono?: "alerta" | "exito" | "info") => void;
};

interface Cargado {
  definicion: DefinicionTipo;
  avaluo: number | null;
}

/**
 * Una sola lista de documentos: la matriz de FLIT 2.0 no asigna documentos a partes, así que si el servidor repite un
 * documento por actor se muestra una vez. Los informativos no se cargan.
 */
export function documentosDeLaMatriz(matriz: MatrizRequisitosPunto): DocumentoRequerido[] {
  const vistos = new Set<string>();
  const documentos: DocumentoRequerido[] = [];
  for (const grupo of matriz.grupos) {
    for (const item of grupo.items) {
      if (item.clase === "informativo" || vistos.has(item.docCode)) continue;
      vistos.add(item.docCode);
      documentos.push({
        docCode: item.docCode,
        nombre: item.nombre,
        obligatorio: item.clase === "obligatorio",
        orden: item.orden,
        instruccionCargue: item.instruccionCargue ?? "",
        condicion: item.condicion ?? "",
      });
    }
  }
  return documentos;
}

/** La definición real del tipo (flags, campos, secciones) con los documentos de la matriz del punto (las transformaciones se cotizan en el envío). */
function definicionDelPunto(definicion: DefinicionTipo, matriz: MatrizRequisitosPunto): DefinicionTipo {
  return {
    ...definicion,
    documentos: documentosDeLaMatriz(matriz),
  };
}

/** La sección que pinta el paso (la primera del grupo requisitos, como en el wizard del portal). */
function seccionDeRequisitos(definicion: DefinicionTipo): SeccionWizard | null {
  for (const paso of definicion.pasos) {
    for (const sectionType of paso.sectionTypes) {
      if (GRUPO_POR_SECCION[sectionType] === "requisitos") return { clave: `${paso.clave}:${sectionType}`, sectionType, paso };
    }
  }
  return null;
}

/** Borrador PN abierto en el punto: lo vigente, la versión para guardar y los guardadores que registra el paso Requisitos. */
function useBorradorPunto(cliente: ClientePortal, solicitudId: string) {
  const [solicitud, setSolicitud] = useState<Solicitud | null>(null);
  const actual = useRef<Solicitud | null>(null);
  const version = useRef(0);
  const guardadores = useRef(new Map<string, () => Promise<void>>());

  const aplicar = useCallback((s: Solicitud) => {
    version.current = s.version;
    actual.current = s;
    setSolicitud(s);
  }, []);
  const recargar = useCallback(async () => {
    aplicar(await cliente.solicitudes.obtener(solicitudId));
  }, [cliente, solicitudId, aplicar]);
  const guardarConVersion = useCallback(
    async (escribir: (version: number) => Promise<Solicitud>) => {
      aplicar(await escribir(version.current));
    },
    [aplicar],
  );
  const registrarGuardado = useCallback((clave: string, guardar: (() => Promise<void>) | null) => {
    if (guardar) guardadores.current.set(clave, guardar);
    else guardadores.current.delete(clave);
  }, []);
  /** Ejecuta lo pendiente de guardar. Un 422 ya queda marcado bajo su campo en el formulario; cualquier otro error se propaga. */
  const guardarPendientes = useCallback(async () => {
    for (const guardar of [...guardadores.current.values()]) {
      try {
        await guardar();
      } catch (e) {
        if (!(e instanceof ApiError && e.status === 422)) throw e;
      }
    }
  }, []);
  const vigente = useCallback(() => actual.current, []);

  return { solicitud, aplicar, recargar, guardarConVersion, registrarGuardado, guardarPendientes, vigente };
}

export function RequisitosPunto({ cliente, solicitudId, tipoCodigo, placa, onAtras, onContinuarPago, onAviso }: Props) {
  const [cargado, setCargado] = useState<Cargado | null>(null);
  const { solicitud, aplicar: aplicarSolicitud, recargar, guardarConVersion, registrarGuardado, guardarPendientes, vigente } = useBorradorPunto(cliente, solicitudId);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [estado, setEstado] = useState<EstadoSeccion>({ completo: false });
  const [intentoAvance, setIntentoAvance] = useState(0);
  const [evaluando, setEvaluando] = useState(false);
  // El resultado del gate se asocia al borrador con el que se calculó: si el borrador cambia (archivo, valor), deja de mostrarse.
  const [gateGuardado, setGateGuardado] = useState<{ resultado: CompletitudRequisitosPunto; base: Solicitud | null } | null>(null);
  const pendientesRef = useRef<HTMLDivElement | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const [matriz, borrador, sugerido] = await Promise.all([
        cliente.punto.requisitos(tipoCodigo),
        cliente.solicitudes.obtener(solicitudId),
        // Fasecolda puede no responder o no traer avalúo: el valor queda vacío para escribirlo.
        cliente.punto.requisitosValorSugerido(placa).catch(() => null),
      ]);
      const definicion = await cliente.catalogos.definicion(tipoCodigo, { organismoId: borrador.organismoId });
      aplicarSolicitud(borrador);
      setCargado({
        definicion: definicionDelPunto(definicion, matriz),
        avaluo: sugerido?.valorSugerido != null && sugerido.valorSugerido > 0 ? sugerido.valorSugerido : null,
      });
    } catch (e) {
      const mensaje = mensajeDeFallo(e);
      setError(mensaje);
      onAviso("No pudimos cargar los requisitos", mensaje, "alerta");
    } finally {
      setCargando(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- onAviso cambia en cada render del padre; recarga explícita con Reintentar
  }, [cliente, tipoCodigo, solicitudId, placa, aplicarSolicitud]);

  useEffect(() => {
    const id = window.setTimeout(() => void cargar(), 0);
    return () => window.clearTimeout(id);
  }, [cargar]);

  const gate = gateGuardado && gateGuardado.base === solicitud ? gateGuardado.resultado : null;

  useEffect(() => {
    if (gate && !gate.puedeContinuarPago) pendientesRef.current?.focus({ preventScroll: false });
  }, [gate]);

  const estadoRef = useRef<EstadoSeccion>({ completo: false });
  const onEstado = useCallback((e: EstadoSeccion) => {
    estadoRef.current = e;
    setEstado(e);
  }, []);

  const api = useMemo<ApiWizard | null>(() => {
    if (!cargado || !solicitud) return null;
    return {
      cliente,
      definicion: cargado.definicion,
      solicitud,
      contexto: null,
      recargar,
      guardarConVersion,
      registrarGuardado,
      reportarError: () => undefined,
      irAPaso: () => undefined,
      canal: "punto",
      avaluoSugerido: cargado.avaluo,
    };
  }, [cliente, cargado, solicitud, recargar, guardarConVersion, registrarGuardado]);

  async function continuar() {
    setIntentoAvance((n) => n + 1);
    setEvaluando(true);
    try {
      // Guarda lo pendiente (valor de venta, prenda…). Un 422 ya queda marcado bajo su campo: el gate de abajo dice qué falta.
      await guardarPendientes();
      // El RUNT reporta prenda y falta decidir: no se pasa a Pago (FLIT 2 exige la decisión).
      if (estadoRef.current.prendaSinDecidir) {
        onAviso("Falta la decisión de prenda", "El RUNT reporta prenda o gravamen: elige qué se hará con ella para continuar.", "alerta");
        return;
      }
      const resultado = await cliente.punto.requisitosCompletitud({ tipoCodigo });
      setGateGuardado({ resultado, base: vigente() });
      if (resultado.puedeContinuarPago) onContinuarPago();
    } catch (e) {
      onAviso("No se pudo validar", mensajeDeFallo(e), "alerta");
    } finally {
      setEvaluando(false);
    }
  }

  if (cargando) {
    return (
      <Card title="Requisitos" id="punto-requisitos">
        <div className="relative min-h-[12rem]" aria-busy="true">
          <MobilityLoader mensaje="Cargando requisitos…" />
        </div>
      </Card>
    );
  }

  const seccion = cargado ? seccionDeRequisitos(cargado.definicion) : null;
  if (error || !cargado || !api || !seccion) {
    return (
      <Card title="Requisitos" id="punto-requisitos-error">
        <div role="alert" className="flex flex-col items-start gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
          <p className="flex gap-2">
            <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
            <span>{error ?? "No hay requisitos para mostrar."}</span>
          </p>
          <Btn tone="secondary" onClick={() => void cargar()}>
            Reintentar
          </Btn>
        </div>
        <div className="mt-4">
          <Nav onBack={onAtras} onNext={onAtras} nextLabel="Volver" />
        </div>
      </Card>
    );
  }

  const faltan = gate !== null && !gate.puedeContinuarPago;
  const motivo = intentoAvance > 0 && (!estado.completo || faltan) ? (estado.bloqueo ?? MOTIVO_REQUISITOS) : undefined;

  return (
    <div className="space-y-6" id="punto-requisitos">
      <ProveedorWizard value={api}>
        <FormularioRequisitos seccion={seccion} activa intentoAvance={intentoAvance} onEstado={onEstado} />
      </ProveedorWizard>

      {faltan && (
        <div
          ref={pendientesRef}
          tabIndex={-1}
          role="alert"
          id="punto-requisitos-pendientes"
          className="flex flex-col gap-2 rounded-[9px] border border-alert bg-alert/10 p-4 text-sm text-alert"
        >
          <p className="flex gap-2 font-medium">
            <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
            <span>Para continuar a Pago falta:</span>
          </p>
          <ul className="list-disc space-y-1 pl-9">
            {gate.pendientes.map((p) => (
              <li key={p.codigo}>{p.nombre}</li>
            ))}
          </ul>
        </div>
      )}

      <Nav
        onBack={onAtras}
        onNext={() => void continuar()}
        nextLabel={evaluando ? "Validando…" : "Continuar"}
        ocupado={evaluando}
        motivo={motivo}
      />
    </div>
  );
}

"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiError } from "@/lib/api";
import { FUENTES, ordenarFuentes, type Configuracion, type Fuente } from "@/lib/configuracion";
import {
  FUENTE_SIEMPRE,
  NOMBRE_MAXIMO,
  armarAlcance,
  fuentesOfrecidas,
  listarTiposTramite,
  type CabeceraEntrada,
  type ModoDiagnostico,
  type TipoTramite,
} from "@/lib/diagnosticos/api";
import type { EstadoTipos } from "./SeccionModo";

export interface ErroresCabecera {
  nombre?: string;
  modo?: string;
  tipo?: string;
  alcance?: string;
}

type CargaTipos = { tipo: "cargando" } | { tipo: "error"; mensaje: string; hasta: number } | { tipo: "listo"; tipos: TipoTramite[] };

const CODIGOS: ReadonlySet<string> = new Set(FUENTES.map((f) => f.codigo));

/** Valida el nombre como la API (CabeceraEntrada: 1..150 caracteres sin contar los espacios de los extremos). */
export function errorNombre(nombre: string): string | undefined {
  const limpio = nombre.trim();
  if (!limpio) return "Escribe el nombre del diagnóstico.";
  if (limpio.length > NOMBRE_MAXIMO) return `El nombre admite hasta ${NOMBRE_MAXIMO} caracteres.`;
  return undefined;
}

/** Lleva el `campo` de un 422 de la cabecera (ErrorRegla) al campo de la pantalla; null si no es de un campo. */
function campoDe(error: unknown): keyof ErroresCabecera | null {
  if (!(error instanceof ApiError) || error.status !== 422 || !error.campo) return null;
  if (error.campo === "nombre") return "nombre";
  if (error.campo === "modo") return "modo";
  if (error.campo === "tipo_tramite_codigo") return "tipo";
  if (error.campo.startsWith("alcance")) return "alcance";
  return null;
}

/**
 * Estado compartido del formulario de parametrización y de su versión compacta (HU #13752): nombre, modo, tipo de
 * trámite y alcance, con las fuentes ofrecidas según el contrato y `fuentes_disponibles` del backend (HU #13865), las
 * por defecto premarcadas, RUNT siempre y las obligatorias del tipo (solo en Con Análisis IA). Carga el catálogo de tipos y, si responde 503, espera Retry-After antes
 * de permitir el reintento.
 */
export function useParametrizacion(configuracion: Configuracion, nombreInicial = "") {
  const disponibles = configuracion.fuentes_disponibles;
  const habilitadas = useMemo(() => fuentesOfrecidas(configuracion.contrato.fuentes_habilitadas, configuracion.fuentes_disponibles), [configuracion]);
  const [nombre, setNombreCrudo] = useState(nombreInicial);
  const [modo, setModoCrudo] = useState<ModoDiagnostico>("SIMPLE");
  const [tipoCodigo, setTipoCrudo] = useState("");
  const [seleccionadas, setSeleccionadas] = useState<Fuente[]>(() =>
    ordenarFuentes([FUENTE_SIEMPRE, ...configuracion.operacion.fuentes_por_defecto.filter((f) => habilitadas.includes(f))]),
  );
  const [errores, setErrores] = useState<ErroresCabecera>({});

  const [intento, setIntento] = useState(0);
  const [carga, setCarga] = useState<CargaTipos>({ tipo: "cargando" });
  const [ahora, setAhora] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    listarTiposTramite(controller.signal)
      .then((tipos) => setCarga({ tipo: "listo", tipos }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        const instante = Date.now();
        const espera = e instanceof ApiError ? (e.retryAfter ?? 0) : 0;
        const mensaje =
          e instanceof ApiError && e.code === "CATALOGO_NO_DISPONIBLE"
            ? "El catálogo de tipos de trámite no está disponible. Intenta de nuevo en un momento."
            : e instanceof Error
              ? e.message
              : "No se pudieron cargar los tipos de trámite.";
        setAhora(instante);
        setCarga({ tipo: "error", mensaje, hasta: instante + espera * 1000 });
      });
    return () => controller.abort();
  }, [intento]);

  // Cuenta regresiva de Retry-After: el botón Reintentar se habilita solo cuando vence.
  useEffect(() => {
    if (carga.tipo !== "error") return;
    const reloj = setInterval(() => {
      const instante = Date.now();
      setAhora(instante);
      if (instante >= carga.hasta) clearInterval(reloj);
    }, 250);
    return () => clearInterval(reloj);
  }, [carga]);

  const reintentarTipos = useCallback(() => {
    setCarga({ tipo: "cargando" });
    setIntento((i) => i + 1);
  }, []);

  const tipos: EstadoTipos =
    carga.tipo === "error" ? { tipo: "error", mensaje: carga.mensaje, segundosRestantes: Math.max(0, Math.ceil((carga.hasta - ahora) / 1000)) } : carga;

  const tipoElegido = modo === "CON_ANALISIS" && carga.tipo === "listo" ? carga.tipos.find((t) => t.codigo === tipoCodigo) : undefined;
  const obligatorias = useMemo(
    () => ordenarFuentes((tipoElegido?.fuentesObligatorias ?? []).filter((f): f is Fuente => CODIGOS.has(f))),
    [tipoElegido],
  );
  const efectivas = ordenarFuentes([
    FUENTE_SIEMPRE,
    ...seleccionadas.filter((f) => habilitadas.includes(f)),
    ...obligatorias.filter((f) => habilitadas.includes(f)),
  ]);

  const limpiar = (campo: keyof ErroresCabecera) => setErrores((actual) => (actual[campo] ? { ...actual, [campo]: undefined } : actual));

  function setNombre(valor: string) {
    setNombreCrudo(valor);
    limpiar("nombre");
  }
  function setModo(valor: ModoDiagnostico) {
    setModoCrudo(valor);
    setErrores((actual) => ({ ...actual, modo: undefined, tipo: undefined }));
  }
  function setTipo(valor: string) {
    setTipoCrudo(valor);
    limpiar("tipo");
  }
  function alternar(fuente: Fuente) {
    if (fuente === FUENTE_SIEMPRE || obligatorias.includes(fuente)) return;
    setSeleccionadas((actual) => (actual.includes(fuente) ? actual.filter((f) => f !== fuente) : ordenarFuentes([...actual, fuente])));
    limpiar("alcance");
  }

  /** Valida en pantalla lo mismo que la API valida primero; devuelve false y pinta los errores si algo falta. */
  function validar(): boolean {
    const nuevos: ErroresCabecera = { nombre: errorNombre(nombre) };
    if (modo === "CON_ANALISIS" && !tipoCodigo) nuevos.tipo = "Elige el tipo de trámite.";
    setErrores(nuevos);
    return !nuevos.nombre && !nuevos.tipo;
  }

  function cabecera(): CabeceraEntrada {
    return {
      nombre: nombre.trim(),
      modo,
      tipo_tramite_codigo: modo === "CON_ANALISIS" ? tipoCodigo : null,
      alcance: armarAlcance(efectivas),
    };
  }

  /** Pinta en su campo un 422 de la API; devuelve false si el error no es de un campo. */
  function aplicarErrorApi(error: unknown): boolean {
    const campo = campoDe(error);
    if (!campo) return false;
    setErrores({ [campo]: (error as Error).message });
    return true;
  }

  return {
    nombre,
    setNombre,
    modo,
    setModo,
    tipoCodigo,
    setTipo,
    tipos,
    reintentarTipos,
    habilitadas,
    disponibles,
    seleccionadas: efectivas,
    obligatorias,
    alternar,
    errores,
    validar,
    cabecera,
    aplicarErrorApi,
  };
}

"use client";

/**
 * Encadenamiento del prellenado standalone y estado de hidratación por bloque — HU #12209
 * (Feature #12201, CF-25).
 *
 * <p>Un bloque es «vehículo», «transferente» o «adquirente». Cada uno tiene su propia consulta, su
 * propia cadena de fuentes y su propio estado de error: que el RUNT del vehículo esté caído no
 * puede impedir hidratar al transferente ni, mucho menos, generar el documento.</p>
 *
 * Uso de ejemplo:
 * ```tsx
 * const vehiculo = usePrefillBloque({
 *   consultar: () => prefillVehiculo({ placa }),
 *   valores: form.vehiculo,
 *   onAplicar: (parche) => setForm((p) => ({ ...p, vehiculo: { ...p.vehiculo, ...parche } })),
 *   siempreEditables: CAMPOS_VEHICULO_SIEMPRE_EDITABLES,
 *   siempreManuales: CAMPOS_VEHICULO_SIEMPRE_MANUALES,
 * });
 * ```
 */
import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/api/types";
import type { PrefillFuente, PrefillResult } from "@/lib/api/types-generacion-documental";
import {
  aplicarPrellenado,
  type CampoHidratado,
  type DiscrepanciaPrellenado,
} from "./prefill-hidratacion";

/** Etiqueta legible de cada fuente. La interfaz nunca muestra el código crudo. */
export const ETIQUETA_FUENTE: Record<string, string> = {
  RUNT: "RUNT",
  RUES: "RUES",
  DIRECTORIO_RL: "Directorio de representantes legales",
  CONTACT_LOOKUP: "Datos de contacto",
};

export function etiquetaFuente(fuente: PrefillFuente | null | undefined): string {
  if (!fuente) return "la fuente consultada";
  return ETIQUETA_FUENTE[fuente as string] ?? (fuente as string);
}

/**
 * Orden de consulta por tipo de parte (CF-25). Es una constante y no un comentario porque la
 * interfaz lo <b>enuncia</b>: el usuario tiene que poder saber qué se consultó y en qué orden.
 *
 * <p>Persona jurídica: primero el directorio de representantes legales del tenant y, solo si no
 * responde, RUES. Persona natural: primero RUNT persona y, si no responde, `contact-lookup`.</p>
 */
export const CADENA_PERSONA_JURIDICA: readonly PrefillFuente[] = ["DIRECTORIO_RL", "RUES"];
export const CADENA_PERSONA_NATURAL: readonly PrefillFuente[] = ["RUNT", "CONTACT_LOOKUP"];

export type EstadoPrefill = "idle" | "consultando" | "hidratado" | "sin-antecedente" | "error";

export interface UsePrefillBloqueOptions {
  /** Ejecuta la consulta. El hook no conoce el endpoint: lo inyecta el componente. */
  consultar: () => Promise<PrefillResult>;
  /**
   * Valores vigentes del bloque en el formulario. Se acepta el objeto tipado del formulario tal
   * cual (`TransferVehiculoInput`, `TransferParteInput`): sus interfaces no declaran índice de
   * cadena, así que exigir `Record<string, string>` obligaría a un cast en cada llamada.
   */
  valores: object;
  /** Escribe en el formulario SOLO los campos que el prellenado puede escribir. */
  onAplicar: (parche: Record<string, string>) => void;
  siempreEditables?: readonly string[];
  siempreManuales?: readonly string[];
}

export interface PrefillBloque {
  estado: EstadoPrefill;
  fuente: PrefillFuente | null;
  /** DV del NIT devuelto por el backend, cuando el endpoint lo entrega (persona jurídica). */
  dv: string | null;
  hidratados: Record<string, CampoHidratado>;
  discrepancias: DiscrepanciaPrellenado[];
  /** Mensaje del fallo de la fuente (502 y compañía). El formulario sigue utilizable. */
  error: string | null;
  ejecutar: () => Promise<void>;
  /** Acción explícita para liberar un campo bloqueado (adenda §15.1). */
  liberar: (campo: string) => void;
  /** El usuario decide adoptar el valor de la fuente. Nunca ocurre solo. */
  adoptarValorFuente: (campo: string) => void;
  descartarDiscrepancia: (campo: string) => void;
  marcarTocado: (campo: string) => void;
  reiniciar: () => void;
}

export function usePrefillBloque({
  consultar,
  valores,
  onAplicar,
  siempreEditables,
  siempreManuales,
}: UsePrefillBloqueOptions): PrefillBloque {
  const [estado, setEstado] = useState<EstadoPrefill>("idle");
  const [fuente, setFuente] = useState<PrefillFuente | null>(null);
  const [dv, setDv] = useState<string | null>(null);
  const [hidratados, setHidratados] = useState<Record<string, CampoHidratado>>({});
  const [discrepancias, setDiscrepancias] = useState<DiscrepanciaPrellenado[]>([]);
  const [error, setError] = useState<string | null>(null);

  // La consulta es asíncrona: al aplicar el resultado hay que leer los valores VIGENTES, no los
  // capturados cuando arrancó. El usuario puede teclear mientras la respuesta viaja.
  const valoresRef = useRef(valores as Record<string, string | undefined>);
  useEffect(() => {
    valoresRef.current = valores as Record<string, string | undefined>;
  }, [valores]);

  const tocadosRef = useRef<Set<string>>(new Set());
  const consultarRef = useRef(consultar);
  useEffect(() => {
    consultarRef.current = consultar;
  }, [consultar]);
  const onAplicarRef = useRef(onAplicar);
  useEffect(() => {
    onAplicarRef.current = onAplicar;
  }, [onAplicar]);

  const marcarTocado = useCallback((campo: string) => {
    tocadosRef.current.add(campo);
  }, []);

  const ejecutar = useCallback(async () => {
    setEstado("consultando");
    setError(null);
    setDiscrepancias([]);
    tocadosRef.current = new Set();

    try {
      const respuesta = await consultarRef.current();

      if (!respuesta?.found) {
        // Sin antecedente NO es un fallo: es un 200 con `found:false`. Todo queda manual.
        setEstado("sin-antecedente");
        setFuente(null);
        setDv(null);
        setHidratados({});
        return;
      }

      const resultado = aplicarPrellenado({
        valoresActuales: valoresRef.current,
        tocados: tocadosRef.current,
        campos: respuesta.fields ?? [],
        fuenteBloque: respuesta.source ?? null,
        siempreEditables,
        siempreManuales,
      });

      if (Object.keys(resultado.parche).length > 0) {
        onAplicarRef.current(resultado.parche);
      }
      setFuente(respuesta.source ?? null);
      setDv(respuesta.dv ?? null);
      setHidratados(resultado.hidratados);
      setDiscrepancias(resultado.discrepancias);
      setEstado("hidratado");
    } catch (e) {
      // Fuente caída: el bloque muestra el error con opción de reintentar y **nada se bloquea**.
      // El documento se puede generar igual con captura manual (CF-25, escenario «fuente caída»).
      setEstado("error");
      setHidratados({});
      setFuente(null);
      setDv(null);
      setError(
        e instanceof ApiError
          ? e.message
          : "No se pudo consultar la fuente. Puedes completar los datos manualmente.",
      );
    }
  }, [siempreEditables, siempreManuales]);

  const liberar = useCallback((campo: string) => {
    setHidratados((prev) => {
      const actual = prev[campo];
      if (!actual) return prev;
      return { ...prev, [campo]: { ...actual, bloqueado: false } };
    });
  }, []);

  const descartarDiscrepancia = useCallback((campo: string) => {
    setDiscrepancias((prev) => prev.filter((d) => d.campo !== campo));
  }, []);

  // El efecto NO va dentro del updater de estado: en StrictMode React invoca los updaters dos
  // veces y el parche se aplicaría por duplicado. Se resuelve leyendo la discrepancia de un ref.
  const discrepanciasRef = useRef(discrepancias);
  useEffect(() => {
    discrepanciasRef.current = discrepancias;
  }, [discrepancias]);

  const adoptarValorFuente = useCallback((campo: string) => {
    const encontrada = discrepanciasRef.current.find((d) => d.campo === campo);
    if (!encontrada) return;
    onAplicarRef.current({ [campo]: encontrada.valorFuente });
    setHidratados((prev) => ({
      ...prev,
      // Adoptar el valor de la fuente no lo bloquea: fue una decisión explícita del usuario y
      // tiene que poder deshacerla escribiendo encima.
      [campo]: { fuente: encontrada.fuente, bloqueado: false },
    }));
    setDiscrepancias((prev) => prev.filter((d) => d.campo !== campo));
  }, []);

  const reiniciar = useCallback(() => {
    setEstado("idle");
    setFuente(null);
    setDv(null);
    setHidratados({});
    setDiscrepancias([]);
    setError(null);
    tocadosRef.current = new Set();
  }, []);

  return {
    estado,
    fuente,
    dv,
    hidratados,
    discrepancias,
    error,
    ejecutar,
    liberar,
    adoptarValorFuente,
    descartarDiscrepancia,
    marcarTocado,
    reiniciar,
  };
}

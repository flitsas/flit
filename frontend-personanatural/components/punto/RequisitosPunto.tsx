"use client";

// HU #14245 / #14244 — Requisitos del canal OT: misma matriz/adjuntos/comercial del portal PN (FLIT 2).
import { useCallback, useEffect, useMemo, useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { Card, Nav } from "@/components/tramite/piezas";
import type {
  Adjunto,
  ClientePortal,
  ItemRequisitoPunto,
  MatrizRequisitosPunto,
  Solicitud,
} from "@/lib/api/cliente";
import { mensajeDeFallo } from "@/lib/api/errores";
import { textoMonedaCompleta, numeroMoneda } from "@/lib/formato";
import {
  ACCEPT_ARCHIVOS,
  AYUDA_ARCHIVOS,
  adjuntoDe,
  errorDeArchivo,
} from "@/lib/tramite/requisitos";

const COLOR_OK = "#8CC63F";
const COLOR_PENDIENTE = "#F9AC00";
const COLOR_ERROR = "#C10000";
const COLOR_CONTINUAR = "#557EFF";
const COLOR_VOLVER = "#162744";

type Props = {
  cliente: ClientePortal;
  solicitudId: string;
  tipoCodigo: string;
  placa: string;
  onAtras: () => void;
  onContinuarPago: () => void;
  onAviso: (titulo: string, mensaje: string, tono?: "alerta" | "exito" | "info") => void;
};

type EstadoItem = "pendiente" | "cargando" | "cargado" | "error";

export function RequisitosPunto({
  cliente,
  solicitudId,
  tipoCodigo,
  placa,
  onAtras,
  onContinuarPago,
  onAviso,
}: Props) {
  const [matriz, setMatriz] = useState<MatrizRequisitosPunto | null>(null);
  const [solicitud, setSolicitud] = useState<Solicitud | null>(null);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [estados, setEstados] = useState<Record<string, EstadoItem>>({});
  const [erroresArchivo, setErroresArchivo] = useState<Record<string, string>>({});
  const [valorVenta, setValorVenta] = useState("");
  const [fuenteAvaluo, setFuenteAvaluo] = useState<string>("");
  const [guardandoValor, setGuardandoValor] = useState(false);
  const [evaluando, setEvaluando] = useState(false);

  const sincronizarDesdeSolicitud = useCallback((s: Solicitud, grupos: MatrizRequisitosPunto["grupos"]) => {
    setSolicitud(s);
    const next: Record<string, EstadoItem> = {};
    for (const g of grupos) {
      for (const it of g.items) {
        const key = claveItem(g.parte, it.docCode);
        next[key] = adjuntoDe(s.adjuntos, it.docCode) ? "cargado" : "pendiente";
      }
    }
    setEstados(next);
    const valor = s.datos.comercial?.valor;
    if (valor != null && valor > 0) setValorVenta(textoMonedaCompleta(valor));
  }, []);

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const [m, s, sugerido] = await Promise.all([
        cliente.punto.requisitos(tipoCodigo),
        cliente.solicitudes.obtener(solicitudId),
        cliente.punto.requisitosValorSugerido(placa).catch(() => null),
      ]);
      setMatriz(m);
      sincronizarDesdeSolicitud(s, m.grupos);

      if (sugerido?.valorSugerido != null && sugerido.valorSugerido > 0) {
        setFuenteAvaluo(sugerido.fuente);
        const yaHayValor = s.datos.comercial?.valor != null && s.datos.comercial.valor > 0;
        if (!yaHayValor) setValorVenta(textoMonedaCompleta(sugerido.valorSugerido));
      } else {
        setFuenteAvaluo(sugerido?.fuente ?? "no_disponible");
      }
    } catch (e) {
      const msg = mensajeDeFallo(e);
      setError(msg);
      onAviso("No pudimos cargar los requisitos", msg, "alerta");
    } finally {
      setCargando(false);
    }
  }, [cliente, tipoCodigo, solicitudId, placa, onAviso, sincronizarDesdeSolicitud]);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  const docCodesCargados = useMemo(() => {
    if (!solicitud) return [] as string[];
    return [...new Set(solicitud.adjuntos.map((a) => a.tipo))];
  }, [solicitud]);

  const [puedeContinuar, setPuedeContinuar] = useState(false);
  const [pendientesGate, setPendientesGate] = useState<string[]>([]);
  const [mostrarValorVenta, setMostrarValorVenta] = useState(false);

  useEffect(() => {
    if (!matriz || !solicitud) return;
    let cancelado = false;
    void (async () => {
      try {
        const valor = numeroMoneda(valorVenta);
        const c = await cliente.punto.requisitosCompletitud({
          tipoCodigo,
          docCodesCargados,
          valorVenta: valor > 0 ? valor : 0,
        });
        if (cancelado) return;
        setPuedeContinuar(c.puedeContinuarPago);
        setPendientesGate(c.pendientes);
        setMostrarValorVenta(
          c.requiereValorVenta ||
            (solicitud.datos.comercial?.valor != null && solicitud.datos.comercial.valor > 0) ||
            c.pendientes.includes("valor_venta"),
        );
      } catch {
        if (!cancelado) {
          setPuedeContinuar(false);
          setPendientesGate([]);
        }
      }
    })();
    return () => {
      cancelado = true;
    };
  }, [cliente, matriz, solicitud, tipoCodigo, docCodesCargados, valorVenta]);

  async function onSeleccionarArchivo(parte: string | null, item: ItemRequisitoPunto, file: File | null) {
    if (!file || !solicitud) return;
    const key = claveItem(parte, item.docCode);
    const rechazoLocal = errorDeArchivo(file);
    if (rechazoLocal) {
      setEstados((s) => ({ ...s, [key]: "error" }));
      setErroresArchivo((e) => ({ ...e, [key]: rechazoLocal }));
      onAviso("Archivo no válido", rechazoLocal, "alerta");
      return;
    }

    setEstados((s) => ({ ...s, [key]: "cargando" }));
    setErroresArchivo((e) => {
      const { [key]: _, ...rest } = e;
      return rest;
    });
    try {
      const previo = adjuntoDe(solicitud.adjuntos, item.docCode);
      await cliente.adjuntos.subir(solicitudId, item.docCode, file);
      if (previo) {
        await cliente.adjuntos.eliminar(solicitudId, previo.id).catch(() => undefined);
      }
      const actualizada = await cliente.solicitudes.obtener(solicitudId);
      setSolicitud(actualizada);
      setEstados((s) => ({ ...s, [key]: "cargado" }));
    } catch (e) {
      setEstados((s) => ({ ...s, [key]: "error" }));
      const msg = mensajeDeFallo(e);
      setErroresArchivo((err) => ({ ...err, [key]: msg }));
      onAviso("No se pudo cargar el archivo", msg, "alerta");
    }
  }

  async function guardarValorSiAplica() {
    if (!solicitud || !mostrarValorVenta) return solicitud;
    const valor = numeroMoneda(valorVenta);
    if (valor <= 0) return solicitud;
    setGuardandoValor(true);
    try {
      const actualizada = await cliente.solicitudes.guardarComercial(solicitudId, {
        version: solicitud.version,
        valorComercial: valor,
      });
      setSolicitud(actualizada);
      return actualizada;
    } finally {
      setGuardandoValor(false);
    }
  }

  async function onContinuar() {
    setEvaluando(true);
    try {
      await guardarValorSiAplica();
      const valor = numeroMoneda(valorVenta);
      const c = await cliente.punto.requisitosCompletitud({
        tipoCodigo,
        docCodesCargados,
        valorVenta: valor > 0 ? valor : 0,
      });
      setPuedeContinuar(c.puedeContinuarPago);
      setPendientesGate(c.pendientes);
      if (!c.puedeContinuarPago) {
        onAviso(
          "Faltan requisitos",
          `Pendientes: ${c.pendientes.join(", ") || "obligatorios incompletos"}.`,
          "alerta",
        );
        return;
      }
      onContinuarPago();
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

  if (error || !matriz) {
    return (
      <Card title="Requisitos" id="punto-requisitos-error">
        <p className="text-sm" style={{ color: COLOR_ERROR }} role="alert">
          {error ?? "No hay requisitos para mostrar."}
        </p>
        <Nav onBack={onAtras} onNext={() => void cargar()} nextLabel="Reintentar" />
      </Card>
    );
  }

  return (
    <Card title="Requisitos" id="punto-requisitos">
      <p className="mb-4 text-sm text-[#162744]/opacity-80">
        Mismos documentos que FLIT 2.0 para este tipo y organismo. Solo quien radica carga aquí; {AYUDA_ARCHIVOS}.
      </p>

      {matriz.grupos.map((g) => (
        <section key={`${g.parte ?? "docs"}-${g.ordinal}`} className="mb-6" aria-label={g.etiqueta}>
          <h3 className="mb-2 text-base font-semibold text-[#162744]">{g.etiqueta}</h3>
          <ul className="space-y-3">
            {g.items.map((it) => {
              const key = claveItem(g.parte, it.docCode);
              const estado = estados[key] ?? "pendiente";
              const adj = solicitud ? adjuntoDe(solicitud.adjuntos, it.docCode) : undefined;
              const color =
                estado === "cargado"
                  ? COLOR_OK
                  : estado === "error"
                    ? COLOR_ERROR
                    : estado === "cargando"
                      ? COLOR_CONTINUAR
                      : COLOR_PENDIENTE;
              return (
                <li
                  key={key}
                  className="flex flex-col gap-2 rounded-lg border border-[#E5E7EB] p-3 sm:flex-row sm:items-center sm:justify-between"
                >
                  <div>
                    <p className="font-medium text-[#162744]">
                      {it.nombre}{" "}
                      <span className="text-xs font-normal uppercase tracking-wide" style={{ color }}>
                        {it.clase}
                        {estado === "cargado"
                          ? " · cargado"
                          : estado === "error"
                            ? " · error"
                            : estado === "cargando"
                              ? " · subiendo…"
                              : " · pendiente"}
                      </span>
                    </p>
                    {adj && <p className="text-xs text-[#162744]/opacity-70">{nombreAdjunto(adj)}</p>}
                    {erroresArchivo[key] && (
                      <p className="text-xs" style={{ color: COLOR_ERROR }}>
                        {erroresArchivo[key]}
                      </p>
                    )}
                  </div>
                  <label
                    className="inline-flex cursor-pointer items-center justify-center rounded-full px-4 py-2 text-sm font-semibold text-white"
                    style={{ background: COLOR_CONTINUAR }}
                  >
                    {estado === "cargado" ? "Reemplazar" : estado === "cargando" ? "Subiendo…" : "Cargar"}
                    <input
                      type="file"
                      className="sr-only"
                      accept={ACCEPT_ARCHIVOS}
                      disabled={estado === "cargando"}
                      onChange={(e) => void onSeleccionarArchivo(g.parte, it, e.target.files?.[0] ?? null)}
                    />
                  </label>
                </li>
              );
            })}
          </ul>
        </section>
      ))}

      {mostrarValorVenta && (
        <div className="mb-4">
          <label htmlFor="punto-valor-venta" className="mb-1 block text-sm font-medium text-[#162744]">
            Valor de venta
          </label>
          <input
            id="punto-valor-venta"
            inputMode="numeric"
            className="w-full max-w-xs rounded-lg border border-[#E5E7EB] px-3 py-2 text-[#162744]"
            value={valorVenta}
            onChange={(e) => setValorVenta(e.target.value.replace(/[^\d]/g, ""))}
            onBlur={() => {
              const n = numeroMoneda(valorVenta);
              if (n > 0) setValorVenta(textoMonedaCompleta(n));
              void guardarValorSiAplica().catch((err) => onAviso("No se guardó el valor", mensajeDeFallo(err), "alerta"));
            }}
            placeholder="Mayor a cero"
            aria-describedby="punto-valor-ayuda"
          />
          <p id="punto-valor-ayuda" className="mt-1 text-xs text-[#162744]/opacity-70">
            Avalúo sugerido (Fasecolda):{" "}
            {fuenteAvaluo === "fasecolda" || (fuenteAvaluo && fuenteAvaluo !== "sin_datos" && fuenteAvaluo !== "no_disponible" && fuenteAvaluo !== "sin_placa")
              ? `precargado (${fuenteAvaluo}); puedes editarlo.`
              : "No disponible — escríbelo tú."}
            {guardandoValor ? " Guardando…" : ""}
          </p>
        </div>
      )}

      <div className="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          className="rounded-full px-5 py-2 text-sm font-semibold text-white"
          style={{ background: COLOR_VOLVER }}
          onClick={onAtras}
        >
          Volver
        </button>
        <button
          type="button"
          className="rounded-full px-5 py-2 text-sm font-semibold text-white disabled:opacity-50"
          style={{ background: COLOR_CONTINUAR }}
          disabled={evaluando || !puedeContinuar}
          onClick={() => void onContinuar()}
        >
          {evaluando ? "Validando…" : "Continuar"}
        </button>
      </div>
      {!puedeContinuar && pendientesGate.length > 0 && (
        <p className="mt-2 text-sm" style={{ color: COLOR_PENDIENTE }} role="status">
          Falta: {pendientesGate.join(", ")}
        </p>
      )}
    </Card>
  );
}

function claveItem(parte: string | null, docCode: string) {
  return `${parte ?? "_"}::${docCode}`;
}

function nombreAdjunto(a: Adjunto) {
  return a.nombreArchivo;
}

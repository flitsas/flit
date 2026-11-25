"use client";

import { useState, type ReactNode } from "react";
import { notFound } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";
import { InlineAlert, type InlineAlertTone } from "@flit/ui/InlineAlert";
import { ListaDefinicion } from "@flit/ui/ListaDefinicion";
import { VehicleLoader } from "@flit/ui/Loader";
import { Placa } from "@flit/ui/Placa";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";
import { BOTON_REINTENTAR_CLS, TARJETA_CLS, TEXTO_SECUNDARIO_CLS } from "@/components/comun/estilos";
import { ChipEstado, Marca } from "@/components/liquidaciones/ChipEstado";
import { DialogoRelanzar } from "@/components/liquidaciones/DialogoRelanzar";
import { EligeCompania } from "@/components/liquidaciones/EligeCompania";
import { ListaRecibos } from "@/components/liquidaciones/ListaRecibos";
import { Monto, TablaVigencias } from "@/components/liquidaciones/TablaVigencias";
import { ApiError } from "@/lib/api";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { esErrorDeEmpresa, mensajeErrorFijo } from "@/lib/errores";
import { obtenerItem, obtenerLiquidacion, useCargaConSondeo } from "@/lib/liquidaciones/api";
import {
  ERRORES_LIQUIDACIONES,
  PANTALLA,
  chipItem,
  esRelanzable,
  itemEnCurso,
  nombreCampoFaltante,
  nombreClaseError,
  nombreTipo,
} from "@/lib/liquidaciones/estados";
import { formatoDocumento, formatoFecha, formatoFechaDia, formatoHora, formatoSecretaria, idCorto } from "@/lib/liquidaciones/formato";
import type { ItemDetalle, Liquidacion } from "@/lib/liquidaciones/tipos";

interface Props {
  liquidacionId: string;
  itemId: string;
  /** impuestos.liquidaciones.crear: «Relanzar». */
  puedeRelanzar: boolean;
  /** impuestos.recibos.descargar: «Descargar recibo». */
  puedeDescargar: boolean;
  /** El Super Admin ve además «Código: {errorCodigo}». */
  esSuperAdmin: boolean;
}

interface Vista {
  item: ItemDetalle;
  /** Cabecera del lote para la miga y el tipo; null si no se pudo leer (no bloquea la pantalla). */
  lote: Liquidacion | null;
}

const TITULO_CLS = "text-lg font-semibold text-flit-primary";

/** Observaciones del proveedor: textos de `warnings`, `validations` (campo y mensaje; nunca `sent`) y `file_errors`. */
function observaciones(resultado: Record<string, unknown> | null) {
  const lista = (clave: string) => (Array.isArray(resultado?.[clave]) ? (resultado?.[clave] as unknown[]) : []);
  const texto = (v: unknown, k: string) => (v && typeof v === "object" && typeof (v as Record<string, unknown>)[k] === "string" ? ((v as Record<string, unknown>)[k] as string) : null);
  const advertencias = lista("warnings").map((w) => texto(w, "message")).filter((t): t is string => Boolean(t));
  const validaciones = lista("validations")
    .map((v) => {
      const mensaje = texto(v, "message");
      const campo = texto(v, "field");
      return mensaje ? (campo ? `${campo}: ${mensaje}` : mensaje) : null;
    })
    .filter((t): t is string => Boolean(t));
  const archivos = lista("file_errors").map((f) => texto(f, "message")).filter((t): t is string => Boolean(t));
  return { advertencias, validaciones, archivos, hay: advertencias.length + validaciones.length + archivos.length > 0 };
}

/** Aviso de resultado según el estado (spec-visual §3.6), estático (vivo=false). */
function avisos(item: ItemDetalle): Array<{ tono: InlineAlertTone; titulo?: string; texto: ReactNode }> {
  const lista: Array<{ tono: InlineAlertTone; titulo?: string; texto: ReactNode }> = [];
  if (itemEnCurso(item.estado) && item.estado !== "INCIERTO")
    lista.push({
      tono: "info",
      texto: (
        <span className="flex flex-wrap items-center gap-2">
          <EtiquetaEnProceso nombreAccesible="Placa en proceso" />
          Esperando el resultado del proveedor. Esta página se actualiza sola.
        </span>
      ),
    });
  if (item.estado === "INCIERTO") lista.push({ tono: "pending", texto: PANTALLA.detalleIncierto });
  if (item.estado === "INCIERTO_REQUIERE_DECISION")
    lista.push({ tono: "warning", texto: "No se pudo confirmar si el proveedor recibió la solicitud. Puedes relanzarla; si sí la recibió, puede quedar duplicada." });
  if (item.estado === "ERROR")
    lista.push({
      tono: "error",
      titulo: item.errorClase ? nombreClaseError(item.errorClase) : "El proveedor no pudo liquidar esta placa",
      texto: item.errorMensaje?.trim() || "El proveedor no pudo liquidar esta placa.",
    });
  if (item.estado === "DATOS_INCOMPLETOS") lista.push({ tono: "warning", texto: `Faltan datos del RUNT para liquidar: ${nombreCampoFaltante(item.campoFaltante)}.` });
  if (item.estado === "DEPARTAMENTO_NO_HABILITADO")
    lista.push({ tono: "info", texto: `El departamento ${item.departamento?.nombre ?? ""} no está habilitado en el contrato de tu compañía.`.replace("  ", " ") });
  if (item.estado === "SIN_RESULTADO_LOTE_CERRADO") lista.push({ tono: "info", texto: "El lote se cerró antes de recibir el resultado de esta placa." });
  if (item.parcial) lista.push({ tono: "pending", texto: "Resultado parcial: el proveedor no pudo liquidar todas las vigencias." });
  if (item.resultadoTardio) lista.push({ tono: "info", texto: "Este resultado llegó después de cerrar el lote." });
  if (item.posibleDuplicado) lista.push({ tono: "warning", texto: "Esta liquidación es un relanzamiento: puede estar duplicada en el proveedor." });
  return lista;
}

/**
 * Detalle de la placa (spec-visual §3.6, F2b-CF7). Cabecera con la placa, el chip vivo del estado, las marcas y
 * «Relanzar» (solo en placas relanzables y con permiso). Aviso de resultado por estado; vehículo y propietario (sin
 * dirección, correo ni teléfono); totales y vigencias con «Sin dato» para cada null; recibos por el BFF; observaciones
 * del proveedor plegadas. Se sondea mientras la placa espera resultado. 404 → notFound(); otro error con Reintentar.
 * El Super Admin lee con ?empresa (regla del backend 1.1.0): sin compañía elegida no se pide nada y se le pide elegir
 * una; un error de compañía (p. ej. 404 EMPRESA_NO_ENCONTRADA) va en línea con su literal, no a la 404.
 */
export function DetalleItem(props: Props) {
  const { empresa } = useEmpresaSeleccionada();
  if (props.esSuperAdmin && !empresa) return <EligeCompania />;
  return <VistaItem {...props} />;
}

function VistaItem({ liquidacionId, itemId, puedeRelanzar, puedeDescargar, esSuperAdmin }: Props) {
  const { empresa } = useEmpresaSeleccionada();
  const [relanzando, setRelanzando] = useState(false);
  const { carga, reintentar } = useCargaConSondeo<Vista>(
    `${liquidacionId}/${itemId}/${empresa ?? ""}`,
    async (signal) => {
      const [item, lote] = await Promise.all([
        obtenerItem(liquidacionId, itemId, { empresa, signal }),
        obtenerLiquidacion(liquidacionId, { empresa, signal }).catch(() => null),
      ]);
      return { item, lote };
    },
    (v) => itemEnCurso(v.item.estado),
  );

  if (carga.tipo === "cargando") return <VehicleLoader label="Cargando la placa…" />;
  if (carga.tipo === "error") {
    if (carga.error instanceof ApiError && carga.error.status === 404 && !esErrorDeEmpresa(carga.error)) notFound();
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la placa"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {mensajeErrorFijo(carga.error, "Revisa tu conexión e intenta de nuevo.", ERRORES_LIQUIDACIONES)}
      </InlineAlert>
    );
  }

  const { item, lote } = carga.dato;
  const masiva = lote?.tipo === "MASIVA";
  const obs = observaciones(item.resultado);
  const vencimientos = item.periodos.map((p) => p.vencimiento).filter((v): v is string => Boolean(v)).sort();
  const pendientes = item.periodos.filter((p) => !p.noLiquidable).length;
  const relanzable = puedeRelanzar && esRelanzable(item);

  return (
    <div className="flex flex-col gap-4">
      <CabeceraModulo
        titulo={item.placa}
        tituloClassName="uppercase tracking-[0.12em]"
        icono="pesos"
        descripcion={`${lote ? `Liquidación ${nombreTipo(lote.tipo).toLowerCase()} · ` : ""}${formatoFecha(item.creadaEn)}${esSuperAdmin ? ` · ${item.empresa.nombre}` : ""}`}
        miga={[
          { etiqueta: "Liquidaciones", href: hrefConEmpresa("/liquidaciones", empresa) },
          ...(masiva ? [{ etiqueta: `Lote ${idCorto(liquidacionId)}`, href: hrefConEmpresa(`/liquidaciones/${encodeURIComponent(liquidacionId)}`, empresa) }] : []),
          { etiqueta: item.placa },
        ]}
        acciones={
          <div className="flex flex-wrap items-center gap-2">
            <ChipEstado chip={chipItem(item)} vivo />
            {item.parcial ? <Marca tono="purple">{PANTALLA.marcaParcial}</Marca> : null}
            {item.posibleDuplicado ? <Marca tono="warning">{PANTALLA.marcaPosibleDuplicado}</Marca> : null}
            {item.resultadoTardio ? <Marca tono="info">{PANTALLA.marcaResultadoTardio}</Marca> : null}
            {relanzable ? (
              <Button size="sm" onClick={() => setRelanzando(true)}>
                {PANTALLA.accionRelanzar}
              </Button>
            ) : null}
          </div>
        }
      />
      {carga.desactualizadoDesde ? (
        <InlineAlert
          tone="warning"
          vivo={false}
          title={`Datos desactualizados desde ${formatoHora(carga.desactualizadoDesde)}`}
          action={
            <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          }
        >
          Se muestra lo último que se leyó.
        </InlineAlert>
      ) : null}
      {avisos(item).map((a, i) => (
        <InlineAlert key={i} tone={a.tono} title={a.titulo} vivo={false}>
          {a.texto}
        </InlineAlert>
      ))}
      {esSuperAdmin && item.errorCodigo ? <p className={TEXTO_SECUNDARIO_CLS}>{`Código: ${item.errorCodigo}`}</p> : null}
      <div className="grid gap-4 lg:grid-cols-2">
        <section aria-labelledby="item-vehiculo" className={`${TARJETA_CLS} flex flex-col gap-3`}>
          <h2 id="item-vehiculo" className={TITULO_CLS}>
            Vehículo y propietario
          </h2>
          <ListaDefinicion
            columnas={2}
            vacio="Sin dato"
            items={[
              { termino: "Placa", valor: <Placa placa={item.placa} /> },
              { termino: "Departamento", valor: item.departamento?.nombre },
              // Secretaria (contrato 1.1.0, delta 5): hoy el backend solo trae la ciudad; el nombre llega null.
              { termino: "Secretaría", valor: formatoSecretaria(item.secretaria) },
              { termino: "Propietario", valor: item.propietario?.nombre },
              {
                termino: "Documento",
                valor: item.propietario?.documentoNumero ? formatoDocumento(item.propietario.documentoTipo, item.propietario.documentoNumero) : null,
              },
            ]}
          />
        </section>
        <section aria-labelledby="item-totales" className={`${TARJETA_CLS} flex flex-col gap-3`}>
          <h2 id="item-totales" className={TITULO_CLS}>
            Totales
          </h2>
          <ListaDefinicion
            vacio="Sin dato"
            items={[
              { termino: "Total a pagar", valor: <Monto valor={item.totalAPagar} className="text-base font-bold" /> },
              { termino: "Vigencias pendientes", valor: String(pendientes) },
              { termino: "Vencimiento más próximo", valor: vencimientos[0] ? formatoFechaDia(vencimientos[0]) : null },
            ]}
          />
        </section>
      </div>
      <section aria-labelledby="item-vigencias" className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <h2 id="item-vigencias" className={TITULO_CLS}>
          Vigencias
        </h2>
        <TablaVigencias periodos={item.periodos} />
      </section>
      <section aria-labelledby="item-recibos" className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <h2 id="item-recibos" className={TITULO_CLS}>
          Recibos
        </h2>
        <ListaRecibos liquidacionId={liquidacionId} itemId={itemId} recibos={item.recibos} empresa={empresa} puedeDescargar={puedeDescargar} />
      </section>
      {obs.hay ? (
        <TarjetaPlegable titulo="Observaciones del proveedor" nivelTitulo={2}>
          <div className="flex flex-col gap-3 px-4 pb-4">
            {(
              [
                ["Advertencias", obs.advertencias],
                ["Validaciones", obs.validaciones],
                ["Archivos con error", obs.archivos],
              ] as const
            ).map(([titulo, textos]) =>
              textos.length > 0 ? (
                <div key={titulo} className="flex flex-col gap-1">
                  <h3 className="text-sm font-semibold text-flit-primary">{titulo}</h3>
                  <ul className="list-disc pl-5 text-sm text-flit-primary">
                    {textos.map((t, i) => (
                      <li key={i}>{t}</li>
                    ))}
                  </ul>
                </div>
              ) : null,
            )}
          </div>
        </TarjetaPlegable>
      ) : null}
      <DialogoRelanzar open={relanzando} liquidacionId={liquidacionId} item={item} empresa={empresa} onClose={() => setRelanzando(false)} />
    </div>
  );
}

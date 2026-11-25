"use client";

import { useEffect, useState, type ReactNode } from "react";
import { notFound, useRouter } from "next/navigation";
import { BarraProgreso } from "@flit/ui/BarraProgreso";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { ChipFiltro, EMBUDO_CLS } from "@flit/ui/ChipsFiltro";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import { usePaginacion } from "@flit/ui/usePaginacion";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, CAMPO_CLS, ETIQUETA_CLS, TARJETA_CLS } from "@/components/comun/estilos";
import { ChipEstado } from "@/components/liquidaciones/ChipEstado";
import { DialogoCerrarLote } from "@/components/liquidaciones/DialogoCerrarLote";
import { EligeCompania } from "@/components/liquidaciones/EligeCompania";
import { DialogoRelanzar } from "@/components/liquidaciones/DialogoRelanzar";
import { GridItems } from "@/components/liquidaciones/GridItems";
import { ApiError } from "@/lib/api";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { esErrorDeEmpresa, mensajeErrorFijo } from "@/lib/errores";
import { listarItems, obtenerLiquidacion, useCargaConSondeo } from "@/lib/liquidaciones/api";
import { CONTADORES, ERRORES_LIQUIDACIONES, PANTALLA, chipLote, loteAbierto, terminadas } from "@/lib/liquidaciones/estados";
import { formatoFecha, formatoHora, idCorto, normalizarPlaca } from "@/lib/liquidaciones/formato";
import type { Contadores, EstadoItem, Item, Liquidacion, Pagina } from "@/lib/liquidaciones/tipos";

interface Props {
  liquidacionId: string;
  /** impuestos.liquidaciones.cerrar (Administrador Impuestos) o Super Admin: «Cerrar lote» (DA-6b). */
  puedeCerrar: boolean;
  /** impuestos.liquidaciones.crear: «Relanzar» en las placas relanzables. */
  puedeRelanzar: boolean;
  esSuperAdmin: boolean;
}

interface Vista {
  lote: Liquidacion;
  items: Pagina<Item>;
}

/**
 * Embudo del lote: los contadores que equivalen a un único estado de ítem filtran la tabla (?estado= de GET .../items);
 * los demás (con deuda, paz y salvo, inciertas, pendientes) se muestran como cifras porque el contrato filtra por un solo
 * estado.
 */
const FILTROS: Array<{ clave: keyof Contadores; estado: EstadoItem }> = [
  { clave: "exitosas", estado: "EXITOSO" },
  { clave: "error", estado: "ERROR" },
  { clave: "datosIncompletos", estado: "DATOS_INCOMPLETOS" },
  { clave: "departamentoNoHabilitado", estado: "DEPARTAMENTO_NO_HABILITADO" },
  { clave: "sinResultado", estado: "SIN_RESULTADO_LOTE_CERRADO" },
];
const SOLO_CIFRA: Array<keyof Contadores> = ["conDeuda", "pazYSalvo", "incierto", "pendientes"];

/**
 * Detalle del lote (spec-visual §3.5, F3-CF5, DA-6b). Cabecera con el chip vivo del estado y «Cerrar lote» (Admin o SA,
 * solo con el lote abierto), avance con el embudo de contadores y la tabla de placas con búsqueda, paginación y acciones.
 * Se sondea mientras el lote está abierto (5 s, se duplica hasta 30 s sin cambios); si el sondeo falla, se avisa sin
 * borrar lo leído. Una liquidación individual redirige al detalle de su placa. 404 → notFound(); otro error, Reintentar.
 * El Super Admin lee con ?empresa (regla del backend 1.1.0): sin compañía elegida no se pide nada y se le pide elegir una;
 * un error de compañía (EMPRESA_NO_ENCONTRADA, EMPRESA_REQUERIDA, EMPRESA_SOLO_SUPER_ADMIN) va en línea, no a la 404.
 */
export function DetalleLote(props: Props) {
  const { empresa } = useEmpresaSeleccionada();
  if (props.esSuperAdmin && !empresa) return <EligeCompania />;
  return <VistaLote {...props} />;
}

function VistaLote({ liquidacionId, puedeCerrar, puedeRelanzar, esSuperAdmin }: Props) {
  const router = useRouter();
  const { empresa } = useEmpresaSeleccionada();
  const pg = usePaginacion("impuestos.lote.placas");
  const [estado, setEstado] = useState<EstadoItem | null>(null);
  const [placa, setPlaca] = useState("");
  const [placaTexto, setPlacaTexto] = useState("");
  const [cerrando, setCerrando] = useState(false);
  const [relanzar, setRelanzar] = useState<Item | null>(null);

  const { carga, reintentar, reemplazar } = useCargaConSondeo<Vista>(
    [liquidacionId, empresa ?? "", estado ?? "", placa, pg.page, pg.pageSize].join("|"),
    async (signal) => {
      const [lote, items] = await Promise.all([
        obtenerLiquidacion(liquidacionId, { empresa, signal }),
        listarItems(liquidacionId, { estado, placa, page: pg.page, pageSize: pg.pageSize }, { empresa, signal }),
      ]);
      return { lote, items };
    },
    (v) => loteAbierto(v.lote.estado),
  );

  // La individual va al detalle de su única placa: el itemId de la cabecera (contrato 1.1.0) o, si no viene, items[0].
  const itemIndividual = carga.tipo === "listo" && carga.dato.lote.tipo === "INDIVIDUAL" ? (carga.dato.lote.itemId ?? carga.dato.items.items[0]?.id) : null;
  const destinoIndividual = itemIndividual
    ? hrefConEmpresa(`/liquidaciones/${encodeURIComponent(liquidacionId)}/items/${encodeURIComponent(itemIndividual)}`, empresa)
    : null;
  useEffect(() => {
    if (destinoIndividual) router.replace(destinoIndividual);
  }, [destinoIndividual, router]);

  if (carga.tipo === "cargando" || destinoIndividual) return <VehicleLoader label="Cargando el lote…" />;
  if (carga.tipo === "error") {
    if (carga.error instanceof ApiError && carga.error.status === 404 && !esErrorDeEmpresa(carga.error)) notFound();
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar el lote"
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

  const { lote, items } = carga.dato;
  const hechas = terminadas(lote);
  const abierto = loteAbierto(lote.estado);
  const actorCierre = lote.cerradaPor?.nombre?.trim() || null;
  const filtrado = Boolean(estado || placa);
  const filtrar = (e: EstadoItem | null) => {
    setEstado(e);
    pg.setPage(1);
  };
  const quitarFiltro = () => {
    setEstado(null);
    setPlaca("");
    setPlacaTexto("");
    pg.setPage(1);
  };

  let tabla: ReactNode;
  if (items.items.length === 0 && filtrado) {
    tabla = (
      <EstadoVacio
        nivelTitulo={3}
        titulo="Ninguna placa coincide con el filtro"
        acciones={[
          <button key="quitar" type="button" onClick={quitarFiltro} className={BOTON_SECUNDARIO_CLS}>
            Quitar filtro
          </button>,
        ]}
      />
    );
  } else if (items.items.length === 0) {
    tabla = <EstadoVacio nivelTitulo={3} titulo="Este lote todavía no tiene placas en proceso" mensaje="Se actualiza solo en unos segundos." />;
  } else {
    tabla = (
      <div className="flex flex-col gap-3">
        <GridItems liquidacionId={liquidacionId} items={items.items} empresa={empresa} puedeRelanzar={puedeRelanzar} onRelanzar={setRelanzar} />
        <Pagination
          page={items.page}
          pageSize={items.pageSize}
          totalCount={items.total}
          onPageChange={pg.setPage}
          onPageSizeChange={pg.setPageSize}
          ariaLabel="Paginación de placas"
          noun="placas"
        />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <CabeceraModulo
        titulo="Liquidación masiva"
        icono="pesos"
        miga={[{ etiqueta: "Liquidaciones", href: hrefConEmpresa("/liquidaciones", empresa) }, { etiqueta: `Lote ${idCorto(lote.id)}` }]}
        descripcion={`ID ${idCorto(lote.id)} · Creada el ${formatoFecha(lote.creadaEn)} · Por ${lote.creadaPor?.nombre ?? ""}${esSuperAdmin ? ` · ${lote.empresa?.nombre ?? ""}` : ""}`}
        acciones={
          <div className="flex flex-wrap items-center gap-2">
            <ChipEstado chip={chipLote(lote.estado)} vivo />
            {puedeCerrar && abierto ? (
              <Button variant="alerta" size="sm" onClick={() => setCerrando(true)}>
                {PANTALLA.accionCerrarLote}
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
      {lote.estado === "CERRADO_MANUALMENTE" ? (
        <InlineAlert tone="info" vivo={false}>
          <p>
            {/* Un nombre solo de espacios es lo mismo que no tener actor: nunca «por  .». */}
            {actorCierre
              ? `Cerrado manualmente el ${formatoFecha(lote.cerradaEn)} por ${actorCierre}.`
              : `Cerrado manualmente el ${formatoFecha(lote.cerradaEn)}.`}
          </p>
          {/* motivoCierre (contrato 1.1.0, delta 5): texto del usuario, siempre como texto plano. */}
          {lote.motivoCierre?.trim() ? <p>{`Motivo: ${lote.motivoCierre.trim()}`}</p> : null}
        </InlineAlert>
      ) : null}
      <section aria-labelledby="lote-avance" className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <h2 id="lote-avance" className="text-lg font-semibold text-flit-primary">
          Avance del lote
        </h2>
        <BarraProgreso etiqueta="Avance del lote" valor={hechas} maximo={lote.totalItems} textoValor={`${hechas} de ${lote.totalItems} placas terminadas`} />
        <p className="text-sm text-flit-primary">{`${hechas} de ${lote.totalItems} placas terminadas`}</p>
        {abierto ? <p className={AYUDA_CLS}>Se actualiza solo mientras el lote está en proceso.</p> : null}
        <div role="group" aria-label="Filtros del lote" className={EMBUDO_CLS}>
          <ChipFiltro cifra={lote.totalItems} etiqueta="Todas" activo={estado === null} onClick={() => filtrar(null)} />
          {FILTROS.map((f) => (
            <ChipFiltro key={f.clave} cifra={lote.contadores[f.clave]} etiqueta={CONTADORES[f.clave]} activo={estado === f.estado} onClick={() => filtrar(f.estado)} />
          ))}
        </div>
        <dl className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-flit-primary">
          {SOLO_CIFRA.map((c) => (
            <div key={c} className="flex gap-1">
              <dt className="text-muted-foreground">{CONTADORES[c]}:</dt>
              <dd className="font-semibold tabular-nums">{lote.contadores[c]}</dd>
            </div>
          ))}
        </dl>
      </section>
      <section aria-labelledby="lote-placas" className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <div className="flex flex-wrap items-end justify-between gap-3">
          <h2 id="lote-placas" className="text-lg font-semibold text-flit-primary">
            Placas del lote
          </h2>
          <form
            role="search"
            aria-label="Buscar placa del lote"
            className="flex flex-col gap-1"
            onSubmit={(e) => {
              e.preventDefault();
              setPlaca(normalizarPlaca(placaTexto));
              pg.setPage(1);
            }}
          >
            <label htmlFor="lote-buscar" className={ETIQUETA_CLS}>
              Buscar placa
            </label>
            <input
              id="lote-buscar"
              type="search"
              maxLength={10}
              autoComplete="off"
              value={placaTexto}
              onChange={(e) => setPlacaTexto(e.target.value.toUpperCase())}
              className={`${CAMPO_CLS} w-44 uppercase tracking-[0.08em]`}
            />
          </form>
        </div>
        {tabla}
      </section>
      <DialogoCerrarLote
        open={cerrando}
        liquidacionId={liquidacionId}
        empresa={empresa}
        onClose={() => setCerrando(false)}
        onCerrado={(cerrada) => {
          setCerrando(false);
          reemplazar({ lote: cerrada, items });
        }}
      />
      {relanzar ? <DialogoRelanzar open liquidacionId={liquidacionId} item={relanzar} empresa={empresa} onClose={() => setRelanzar(null)} /> : null}
    </div>
  );
}

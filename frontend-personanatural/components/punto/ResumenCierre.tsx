"use client";

// HU #14465 — Confirmación final del canal OT (paso 5 · Envío). Replica el paso 5 del diseño de referencia (Lovable,
// tramite-traspaso.tsx): tarjeta central con círculo, título, tabla de cabecera azul, seguimiento de 5 pasos y el párrafo de
// consulta; debajo, la rejilla de tarjetas (Vehículo, Actores, Condiciones Comerciales, Valor Total) con los datos reales.
// En lugar del «Comprobante de pago / Pendiente de aprobación» de la pasarela se muestra el pago en caja.
import { Check, Clock, FileText, X } from "lucide-react";
import { RadicadoTracking } from "@/components/modales/RadicadoTracking";
import { Card } from "@/components/tramite/piezas";
import type { CierrePuntoRespuesta } from "@/lib/api/cliente";
import { formatearHora12, formatearPesos } from "@/lib/formato";
import { pasosDeCierre } from "@/lib/punto/seguimiento";
import { ETIQUETA_ACCION_PRENDA } from "@/lib/tramite/prenda";

const MEDIOS: Record<string, string> = { caja: "Caja", sin_cobro: "Sin cobro" };
const MEDIOS_CAJA: Record<string, string> = { efectivo: "Efectivo", datafono: "Datáfono", transferencia: "Transferencia", otro: "Otro" };

/** «Efectivo», «Datáfono»… o «Sin cobro». Un medio que no conocemos se muestra como «Otro». */
export function etiquetaMedio(cierre: Pick<CierrePuntoRespuesta, "medio" | "medioCaja">): string {
  if (cierre.medio === "sin_cobro") return MEDIOS.sin_cobro;
  const caja = cierre.medioCaja ? MEDIOS_CAJA[cierre.medioCaja] : undefined;
  return caja ?? (cierre.medio === "caja" ? "Otro" : (MEDIOS[cierre.medio] ?? "Otro"));
}

const TONOS = {
  registrado: { circulo: "bg-success", texto: "text-success", Icono: Check, titulo: "¡Muchas gracias, tu trámite se ha enviado con éxito!" },
  en_registro: { circulo: "bg-warning", texto: "text-warning", Icono: Clock, titulo: "Tu trámite está en proceso de registro" },
  rechazado: { circulo: "bg-destructive", texto: "text-destructive", Icono: X, titulo: "No pudimos registrar tu trámite." },
} as const;

const CELDA = "border-t border-border p-4 text-center align-middle";
const INSIGNIA_OK = "rounded-full bg-success/10 px-3 py-1 text-xs font-semibold text-success";

const PARRAFO_CONSULTA = "Para consultar el estado de tu trámite o la respuesta del Organismo de Tránsito, utiliza tu ID de trámite o el número de radicado.";
const PARRAFO_CORREO = "También hemos enviado una copia de esta confirmación a tu correo electrónico registrado.";

/** «Prenda: No» sin decisión, o «Prenda: Sí · Levantar la prenda inscrita»; nunca el código interno de la decisión. */
export function textoPrenda(tiene: boolean, decision?: string | null): string {
  const etiqueta = decision === "registrar" || decision === "levantar" ? ETIQUETA_ACCION_PRENDA[decision] : decision === "sin_prenda" && tiene ? "No hacer nada con la prenda" : null;
  return `Prenda: ${tiene ? "Sí" : "No"}${tiene && etiqueta ? ` · ${etiqueta}` : ""}`;
}

/** Texto plano de la confirmación (lo que ve el ciudadano), para el comprobante en PDF. */
export function lineasComprobante(cierre: CierrePuntoRespuesta, placa?: string, tipoNombre?: string): string[] {
  const r = cierre.resumen;
  const lineas = [
    "FLIT - Comprobante del trámite",
    "",
    `ID del trámite: ${cierre.codigoPublico}`,
    `Tipo: ${tipoNombre || cierre.tipoTramite || "—"}`,
    `Nº radicado: ${cierre.radicadoFlit2 ?? "En registro"}`,
    `Organismo: ${cierre.recogeEn.organismo}`,
  ];
  const p = placa || cierre.placa;
  if (p) lineas.push(`Placa: ${p}`);
  if (r?.actores.length) lineas.push("", "Actores:", ...r.actores.map((a) => `  ${a.parte}: ${a.nombre}`));
  if (r?.valorVenta != null) lineas.push("", `Valor de venta: ${formatearPesos(r.valorVenta)}`);
  if (r?.tienePrenda != null) lineas.push(textoPrenda(r.tienePrenda, r.decisionPrenda));
  if (r?.documentos.length) lineas.push("", "Documentos cargados:", ...r.documentos.map((d) => `  ${d.nombre}`));
  lineas.push("", `Valor total del trámite: ${formatearPesos(cierre.valorCop)}`);
  if (r?.desglose.length) lineas.push(...r.desglose.map((l) => `  ${l.nombre}: ${formatearPesos(l.valorCop)}`));
  lineas.push(`Pago: ${etiquetaMedio(cierre)} (${cierre.medio === "sin_cobro" ? "sin cobro" : "pagado en caja"})`);
  return lineas;
}

export function ResumenCierre({
  cierre,
  titulo = "h2",
  placa,
  tipoNombre,
  radicaEn,
}: {
  cierre: CierrePuntoRespuesta;
  titulo?: "h2" | "h3";
  /** Si se conocen (el cierre del asistente), tienen prioridad sobre lo que informa el servidor. */
  placa?: string | undefined;
  tipoNombre?: string | undefined;
  /** Cuándo se envía al organismo el trámite que sigue en registro (ISO); sin él no se muestra la hora. */
  radicaEn?: string | null | undefined;
}) {
  const tono = TONOS[cierre.estadoCierre] ?? TONOS.en_registro;
  const Titulo = titulo;
  const Icono = tono.Icono;
  const radicado = cierre.radicadoFlit2 ?? (cierre.estadoCierre === "rechazado" ? "—" : "En registro");
  const r = cierre.resumen;
  const placaVisible = placa || cierre.placa || undefined;
  const tipo = tipoNombre || cierre.tipoTramite || "—";
  const sinCobro = cierre.medio === "sin_cobro";

  return (
    <section aria-label="Cierre del trámite" className="space-y-6">
      <Card>
        <div className="flex flex-col items-center text-center">
          <div
            role={cierre.estadoCierre === "rechazado" ? "alert" : "status"}
            className="flex flex-col items-center"
            data-estado={cierre.estadoCierre}
          >
            <span aria-hidden className={`grid h-16 w-16 place-items-center rounded-full ${tono.circulo} text-primary-foreground`}>
              <Icono className="h-9 w-9" />
            </span>
            <Titulo className={`mt-4 text-2xl font-bold leading-tight sm:text-[32px] ${tono.texto}`}>{tono.titulo}</Titulo>
            {cierre.estadoCierre === "en_registro" && formatearHora12(radicaEn) && (
              <p className="mt-2 text-sm" data-testid="radica-en">
                Se envía al organismo a las {formatearHora12(radicaEn)}
              </p>
            )}
            {cierre.estadoCierre === "rechazado" && cierre.motivo && <p className="mt-2 text-sm">{cierre.motivo}</p>}
            {cierre.estadoCierre === "rechazado" && <p className="mt-1 text-sm">Acércate al organismo con este código: {cierre.codigoPublico}</p>}
          </div>

          <div className="mt-6 w-full overflow-x-auto rounded-[9px] border border-border">
            <table className="w-full min-w-[600px] text-center text-sm">
              <thead className="bg-primary text-primary-foreground">
                <tr>
                  {["ID del trámite", "Tipo", "Nº radicado", "Organismo"].map((t) => (
                    <th key={t} scope="col" className="p-4 text-center align-middle font-bold">
                      {t}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="bg-card">
                <tr>
                  <td className={`${CELDA} font-semibold`} data-testid="codigo-cierre">
                    {cierre.codigoPublico}
                  </td>
                  <td className={CELDA} data-testid="tipo-cierre">
                    {tipo}
                  </td>
                  <td className={CELDA} data-testid="radicado-flit2">
                    {radicado}
                  </td>
                  <td className={CELDA}>{cierre.recogeEn.organismo}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <RadicadoTracking pasos={pasosDeCierre(cierre.estadoCierre)} />

          <p className="mt-2 max-w-2xl">
            {PARRAFO_CONSULTA}
            {r?.correoEnviado ? ` ${PARRAFO_CORREO}` : ""}
          </p>
        </div>
      </Card>

      <div className="grid gap-4 md:grid-cols-2">
        {placaVisible && (
          <Card title="Vehículo" id="cierre-vehiculo">
            <p>{placaVisible}</p>
            <p className="text-xs font-light text-muted-foreground">{cierre.recogeEn.organismo}</p>
          </Card>
        )}
        {r != null && r.actores.length > 0 && (
          <Card title="Actores" id="cierre-actores">
            {r.actores.map((a, i) => (
              <p key={`${a.parte}-${i}`}>
                {a.parte}: {a.nombre}
              </p>
            ))}
          </Card>
        )}
        {r != null && (r.valorVenta != null || r.tienePrenda != null || r.documentos.length > 0) && (
          <Card title="Condiciones Comerciales" id="cierre-condiciones">
            {r.valorVenta != null && <p>Venta: {formatearPesos(r.valorVenta)}</p>}
            {r.tienePrenda != null && <p>{textoPrenda(r.tienePrenda, r.decisionPrenda)}</p>}
            {r.documentos.length > 0 && <p className="text-xs font-light text-muted-foreground">{r.documentos.map((d) => d.nombre).join(", ")}</p>}
          </Card>
        )}
        <Card title="Valor Total del Trámite" id="cierre-valor">
          <p className="text-2xl font-bold">{formatearPesos(cierre.valorCop)}</p>
          {r?.desglose.map((l, i) => (
            <p key={`${l.nombre}-${i}`} className="mt-1">
              {l.nombre}: {formatearPesos(l.valorCop)}
            </p>
          ))}
        </Card>
      </div>

      <Card title="Pago en caja" id="cierre-pago">
        <div className="flex flex-col items-center gap-3 rounded-[9px] border border-border p-4 text-center sm:flex-row sm:text-left">
          <FileText aria-hidden className="h-10 w-10 shrink-0 text-primary" />
          <div className="min-w-0 flex-1">
            <p className="font-medium">
              Medio: <span data-testid="medio-cierre">{etiquetaMedio(cierre)}</span>
            </p>
            <p className="text-sm">Valor pagado: {formatearPesos(cierre.valorCop)}</p>
            <span className={`mt-1 inline-block ${INSIGNIA_OK}`}>{sinCobro ? "Sin cobro" : "Pagado en caja"}</span>
          </div>
        </div>
      </Card>

      {r != null && r.documentos.length > 0 && (
        <Card title={`Documentos cargados del ${/traspaso/i.test(tipo) ? "traspaso" : "trámite"}`} id="cierre-documentos">
          <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {r.documentos.map((d, i) => (
              <li key={`${d.nombre}-${i}`} className="flex h-full flex-col items-center gap-3 rounded-[9px] border border-border p-4 text-center">
                <FileText aria-hidden className="h-7 w-7 text-primary" />
                <p className="break-all text-sm font-medium">{d.nombre}</p>
                <span className={INSIGNIA_OK}>Cargado</span>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </section>
  );
}

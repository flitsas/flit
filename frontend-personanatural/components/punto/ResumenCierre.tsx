"use client";

// HU #14465 — Contenido del cierre del canal OT: ID del trámite (código público) con copiar, dónde recogerlo, valor y medio.
// Lo usan la pantalla de cierre (CierrePunto) y «Consultar mi trámite» del punto cuando el trámite tiene pago.
// Diseño: paso 5 del repo de UI (círculo de icono + título grande + tabla con encabezado primary + tarjetas en dos columnas).
import { Check, Clock, Copy, X } from "lucide-react";
import { useState } from "react";
import { boton } from "@/components/landing/estilos";
import { Card } from "@/components/tramite/piezas";
import type { CierrePuntoRespuesta } from "@/lib/api/cliente";
import { formatearPesos } from "@/lib/formato";

const MEDIOS: Record<string, string> = { caja: "Caja", sin_cobro: "Sin cobro" };
const MEDIOS_CAJA: Record<string, string> = { efectivo: "Efectivo", datafono: "Datáfono", transferencia: "Transferencia", otro: "Otro" };

/** «Efectivo», «Datáfono»… o «Sin cobro». Un medio que no conocemos se muestra como «Otro». */
export function etiquetaMedio(cierre: Pick<CierrePuntoRespuesta, "medio" | "medioCaja">): string {
  if (cierre.medio === "sin_cobro") return MEDIOS.sin_cobro;
  const caja = cierre.medioCaja ? MEDIOS_CAJA[cierre.medioCaja] : undefined;
  return caja ?? (cierre.medio === "caja" ? "Otro" : (MEDIOS[cierre.medio] ?? "Otro"));
}

const TONOS = {
  registrado: { circulo: "bg-success", texto: "text-success", Icono: Check, titulo: "¡Tu trámite quedó! Recoge en este organismo." },
  en_registro: { circulo: "bg-primary", texto: "text-primary", Icono: Clock, titulo: "Tu trámite está en proceso de registro" },
  rechazado: { circulo: "bg-destructive", texto: "text-destructive", Icono: X, titulo: "No pudimos registrar tu trámite." },
} as const;

const CELDA = "border-t border-border p-4 text-center align-middle";

export function ResumenCierre({
  cierre,
  titulo = "h3",
  placa,
  tipoNombre,
}: {
  cierre: CierrePuntoRespuesta;
  titulo?: "h2" | "h3";
  /** Si se conocen (el cierre del asistente), completan la columna «Tipo» y la tarjeta «Vehículo». */
  placa?: string | undefined;
  tipoNombre?: string | undefined;
}) {
  const [copiado, setCopiado] = useState(false);
  const tono = TONOS[cierre.estadoCierre] ?? TONOS.en_registro;
  const Titulo = titulo;
  const Icono = tono.Icono;
  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(cierre.codigoPublico);
      setCopiado(true);
    } catch {
      setCopiado(false);
    }
  };
  const radicado = cierre.radicadoFlit2 ?? (cierre.estadoCierre === "rechazado" ? "—" : "En registro");

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
                  <td className={CELDA}>{tipoNombre || "—"}</td>
                  <td className={CELDA} data-testid="radicado-flit2">
                    {radicado}
                  </td>
                  <td className={CELDA}>{cierre.recogeEn.organismo}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <button
            type="button"
            className={boton(cierre.estadoCierre === "registrado" ? "positive" : "back", "mt-4 min-h-11 px-4", "none")}
            onClick={() => void copiar()}
          >
            {copiado ? <Check aria-hidden /> : <Copy aria-hidden />}
            {copiado ? "Copiado" : "Copiar ID"}
          </button>

          <p className="mt-4 max-w-2xl">Para consultar el estado de tu trámite usa tu ID de trámite en Consultar mi trámite.</p>
        </div>
      </Card>

      <div className="grid gap-4 md:grid-cols-2">
        <Card title="Valor pagado" id="cierre-valor">
          <p className="text-2xl font-bold">{formatearPesos(cierre.valorCop)}</p>
          <p className="mt-1 text-sm">
            Medio: <span className="font-medium">{etiquetaMedio(cierre)}</span>
          </p>
        </Card>
        {placa && (
          <Card title="Vehículo" id="cierre-vehiculo">
            <p>{[placa, tipoNombre].filter(Boolean).join(" · ")}</p>
            <p className="text-xs font-light text-muted-foreground">{cierre.recogeEn.organismo}</p>
          </Card>
        )}
      </div>
    </section>
  );
}

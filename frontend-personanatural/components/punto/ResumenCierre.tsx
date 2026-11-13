"use client";

// HU #14465 — Contenido del cierre del canal OT: ID del trámite (código público) con copiar, dónde recogerlo, valor y medio.
// Lo usan la pantalla de cierre (CierrePunto) y «Consultar mi trámite» del punto cuando el trámite tiene pago.
import { Check, Copy } from "lucide-react";
import { useState } from "react";
import { boton } from "@/components/landing/estilos";
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
  registrado: { caja: "bg-success/10 text-success", titulo: "Tu trámite quedó. Recoge en este organismo." },
  en_registro: { caja: "bg-primary/10 text-primary", titulo: "Tu trámite está en proceso de registro. Consúltalo después con este código." },
  rechazado: { caja: "bg-alert/10 text-alert", titulo: "No pudimos registrar tu trámite." },
} as const;

export function ResumenCierre({ cierre, titulo = "h3" }: { cierre: CierrePuntoRespuesta; titulo?: "h2" | "h3" }) {
  const [copiado, setCopiado] = useState(false);
  const tono = TONOS[cierre.estadoCierre] ?? TONOS.en_registro;
  const Titulo = titulo;
  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(cierre.codigoPublico);
      setCopiado(true);
    } catch {
      setCopiado(false);
    }
  };

  return (
    <section aria-label="Cierre del trámite" className="space-y-4">
      <div
        role={cierre.estadoCierre === "rechazado" ? "alert" : "status"}
        className={`rounded-[9px] p-4 ${tono.caja}`}
        data-estado={cierre.estadoCierre}
      >
        <Titulo className="text-xl font-bold">{tono.titulo}</Titulo>
        {cierre.estadoCierre === "registrado" && <p className="mt-1 text-sm">{cierre.recogeEn.organismo}</p>}
        {cierre.estadoCierre === "rechazado" && cierre.motivo && <p className="mt-1 text-sm">{cierre.motivo}</p>}
        {cierre.estadoCierre === "rechazado" && <p className="mt-1 text-sm">Acércate al organismo con este código: {cierre.codigoPublico}</p>}
      </div>

      <div className="flex flex-wrap items-end gap-6">
        <div>
          <p className="text-xs text-muted-foreground">ID del trámite</p>
          <div className="flex flex-wrap items-center gap-3">
            <p aria-label="ID del trámite" className="text-3xl font-bold tracking-wider" data-testid="codigo-cierre">
              {cierre.codigoPublico}
            </p>
            <button type="button" className={boton(cierre.estadoCierre === "registrado" ? "positive" : "back", "min-h-11 px-4", "none")} onClick={() => void copiar()}>
              {copiado ? <Check aria-hidden /> : <Copy aria-hidden />}
              {copiado ? "Copiado" : "Copiar ID"}
            </button>
          </div>
        </div>
        {cierre.radicadoFlit2 && (
          <div>
            <p className="text-xs text-muted-foreground">Radicado FLIT 2.0</p>
            <p className="text-xl font-semibold" data-testid="radicado-flit2">
              {cierre.radicadoFlit2}
            </p>
          </div>
        )}
      </div>

      <dl className="space-y-2 text-sm">
        <div className="flex justify-between gap-4">
          <dt className="text-muted-foreground">Valor pagado</dt>
          <dd className="font-medium">{formatearPesos(cierre.valorCop)}</dd>
        </div>
        <div className="flex justify-between gap-4">
          <dt className="text-muted-foreground">Medio</dt>
          <dd className="font-medium">{etiquetaMedio(cierre)}</dd>
        </div>
      </dl>
    </section>
  );
}

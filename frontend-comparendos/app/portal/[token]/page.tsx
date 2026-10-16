import { comparendosConfig } from "@/lib/config.server";
import { formatoFecha } from "@/lib/format/date";

// Portal del conductor (§7.17): público, sin login, por enlace único. Se consulta desde el servidor al gateway sin
// sesión; un enlace inválido, vencido o revocado muestra siempre lo mismo.
export const dynamic = "force-dynamic";

interface Portal {
  empresa: string | null;
  /** Conjunto de plantillas de la empresa: con «Renting» el portal no lleva nada de FLIT (como sus correos, decisión D9). */
  conjunto: "Flit" | "Renting";
  conductor: { nombres: string; documento: string } | null;
  comparendo: {
    numero: string;
    placa: string | null;
    fechaImposicion: string | null;
    hora: string | null;
    fechaNotificacion: string | null;
    organismo: string | null;
    infraccion: { codigo: string | null; descripcion: string | null };
    direccion: string | null;
    valorTexto: string | null;
    esFotomulta: boolean;
  };
  descuento: { ventana: string; texto: string };
  dondePagar: string;
  evidencia: { foto: string | null; documento: boolean };
  vence: string;
}

async function cargar(token: string): Promise<Portal | null> {
  const { apiOrigin, internalApiKey } = comparendosConfig();
  try {
    const r = await fetch(`${apiOrigin}/api/v1/comparendos/publico/portal/${encodeURIComponent(token)}`, {
      cache: "no-store",
      headers: internalApiKey ? { "x-internal-key": internalApiKey } : undefined,
    });
    return r.ok ? ((await r.json()) as Portal) : null;
  } catch {
    return null;
  }
}

function Dato({ etiqueta, valor }: { etiqueta: string; valor: string | null | undefined }) {
  return (
    <div className="flex flex-col gap-0.5 border-b border-[#EEF1F6] py-2 last:border-0">
      <dt className="text-xs uppercase tracking-wide text-slate-500">{etiqueta}</dt>
      <dd className="text-sm font-medium text-[#162744]">{valor || "—"}</dd>
    </div>
  );
}

export default async function PortalConductor({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  const portal = await cargar(token);

  if (!portal) {
    return (
      <main className="mx-auto flex min-h-full max-w-lg flex-col items-center justify-center gap-3 px-6 py-16 text-center">
        <h1 className="text-xl font-semibold text-[#162744]">Este enlace no está disponible</h1>
        <p className="text-sm text-slate-600">Puede haber vencido o ya no estar vigente. Si tienes dudas sobre tu comparendo, comunícate con la empresa que te lo envió.</p>
      </main>
    );
  }

  const c = portal.comparendo;
  // La marca de la gestora (§7.17) es su conjunto de plantillas, igual que en los correos (HtmlCorreo.Envolver).
  const marcaFlit = portal.conjunto !== "Renting";
  const fondoEncabezado = marcaFlit ? "#162744" : "#3A4250";
  const color =
    portal.descuento.ventana === "Descuento50" ? "#00A86B" : portal.descuento.ventana === "Descuento25" ? "#D98A00" : "#C2410C";
  // B7-3: el gris ocupa toda la pantalla (en escritorio la columna quedaba sobre fondo blanco) y la marca FLIT lleva su
  // logo; Renting no lleva nada de FLIT (D9) y todavía no hay logo de Renting en el repositorio: solo su nombre.
  return (
    <div className="min-h-screen bg-[#F4F6FA]">
      <main className="mx-auto flex min-h-full max-w-lg flex-col gap-4 px-4 py-6">
        <header className="flex flex-col gap-1 rounded-xl px-4 py-4 text-white" style={{ background: fondoEncabezado }}>
          {marcaFlit && (
            // eslint-disable-next-line @next/next/no-img-element -- SVG estático de public/assets, sin optimización
            <img src="/assets/logo-flit-white.svg" alt="FLIT" className="mb-2 h-6 w-auto self-start" />
          )}
          <p className="text-xs uppercase tracking-wide opacity-80">{portal.empresa ?? (marcaFlit ? "FLIT" : "Comparendos")}</p>
          <h1 className="text-xl font-semibold">Comparendo {c.numero}</h1>
          {portal.conductor && (
            <p className="text-sm opacity-90">
              {portal.conductor.nombres} · {portal.conductor.documento}
            </p>
          )}
        </header>

        <section className="rounded-xl border-l-4 bg-white p-4 shadow-sm" style={{ borderColor: color }}>
          <p className="text-sm font-semibold" style={{ color }}>
            {portal.descuento.texto}
          </p>
          <p className="mt-2 text-sm text-slate-600">{portal.dondePagar}</p>
        </section>

        <section className="rounded-xl bg-white p-4 shadow-sm">
          <dl>
            <Dato etiqueta="Placa" valor={c.placa} />
            <Dato etiqueta="Fecha de la infracción" valor={`${formatoFecha(c.fechaImposicion)}${c.hora ? `, ${c.hora.slice(0, 5)}` : ""}`} />
            <Dato etiqueta="Tipo" valor={c.esFotomulta ? "Fotomulta" : "Comparendo en vía"} />
            <Dato etiqueta="Organismo de tránsito" valor={c.organismo} />
            <Dato etiqueta="Infracción" valor={[c.infraccion.codigo, c.infraccion.descripcion].filter(Boolean).join(" — ")} />
            <Dato etiqueta="Dirección" valor={c.direccion} />
            <Dato etiqueta="Valor" valor={c.valorTexto} />
          </dl>
        </section>

        {(portal.evidencia.foto || portal.evidencia.documento) && (
          <section className="rounded-xl bg-white p-4 shadow-sm">
            <h2 className="mb-2 text-sm font-semibold text-[#162744]">Evidencia</h2>
            {portal.evidencia.foto ? (
              // eslint-disable-next-line @next/next/no-img-element -- la foto viene de la fuente con su propia URL
              <img src={portal.evidencia.foto} alt="Evidencia del comparendo" className="w-full rounded-lg" />
            ) : (
              <a className="text-sm text-[#3B4FD6] underline" href={`/publico/portal/${encodeURIComponent(token)}/evidencia`} target="_blank" rel="noreferrer">
                Ver el documento del comparendo
              </a>
            )}
          </section>
        )}

        <footer className="flex flex-col gap-1 pb-6 text-center text-xs text-slate-500">
          <span>Este enlace es personal y está disponible hasta el {formatoFecha(portal.vence)}.</span>
          <span>{marcaFlit ? "FLIT · Gestión de Comparendos" : (portal.empresa ?? "")}</span>
        </footer>
      </main>
    </div>
  );
}

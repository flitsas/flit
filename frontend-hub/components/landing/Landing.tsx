import { createElement } from "react";
import { ArrowRight, ArrowUpRight, Check, Globe, KeyRound, Mail, Palette, Plug, ShieldCheck, Sparkles } from "lucide-react";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { PRODUCTS, type ProductInfo } from "@/lib/products";
import { COMPANY, CONTACT, PILLARS, STEPS, demoMailto } from "@/lib/site";
import { LandingNav } from "./LandingNav";
import { ProductArt } from "@/components/ui/ProductIcon";
import { FLIT_PRIVACY_POLICY_URL } from "@flit/brand/types";
import { StatusChip } from "@/components/ui/StatusChip";
import { buttonClass } from "@flit/ui/Button";

// Landing de FLIT en la raíz del hub, sin sesión (B-11): qué es FLIT, sus productos, cómo empezar y contacto. Con el
// lenguaje del ingreso de la propuesta «Diseño Inteligente» (HU #14487, `@flit/ui/Ingreso`): portada y contacto en el
// panel de tinta (--di-ingreso-*, igual en los dos temas), ilustraciones de producto, tarjetas `.card` y botones píldora.
// Tiene modo oscuro. Sin precios por decisión de negocio. El texto vive en lib/products.ts y lib/site.ts.

const PILLAR_ICONS = { "key-round": KeyRound, plug: Plug, "shield-check": ShieldCheck, palette: Palette } as const;

const card =
  "rounded-[20px] border border-[color:var(--di-border)] bg-[color:var(--di-surface-elevated)] shadow-[var(--shadow-card)]";
const muted = "text-[color:var(--di-text-secondary)]";
const titulo = "text-[color:var(--di-heading)]";
const linea = "border-[color:var(--di-divider)]";

/** Fondo del panel de tinta del ingreso (`.ingreso__marca`): dos halos sobre el azul profundo. */
const TINTA = {
  background:
    "radial-gradient(60% 50% at 85% 10%, color-mix(in srgb, var(--di-ingreso-acento-2) 30%, transparent), transparent 70%)," +
    "radial-gradient(70% 60% at 0% 100%, color-mix(in srgb, var(--di-ingreso-azul) 38%, transparent), transparent 70%)," +
    "var(--di-ingreso-fondo)",
};
/** Texto con el degradado del titular del ingreso. */
const DEGRADADO_TINTA = { backgroundImage: "linear-gradient(90deg, var(--di-ingreso-acento-1), var(--di-ingreso-acento-2))" };
/** Botón de contorno claro sobre la tinta. */
const BOTON_TINTA =
  "inline-flex min-h-11 items-center gap-2 rounded-full border border-[color:color-mix(in_srgb,var(--di-ingreso-texto)_45%,transparent)] px-6 text-sm font-semibold text-[color:var(--di-ingreso-texto)] transition hover:bg-[color:color-mix(in_srgb,var(--di-ingreso-texto)_10%,transparent)]";

export function Landing({ loginUrl }: { loginUrl: string }) {
  return (
    <div className="min-h-full bg-[color:var(--di-surface)] text-[color:var(--di-text-primary)]">
      <LandingNav loginUrl={loginUrl} />
      <main>
        <Hero loginUrl={loginUrl} />
        <ProductsOverview />
        {PRODUCTS.map((p, i) => (
          <ProductSection key={p.code} product={p} reverse={i % 2 === 1} loginUrl={loginUrl} />
        ))}
        <WhyFlit />
        <About />
        <HowToStart />
        <Contact />
      </main>
      <Footer loginUrl={loginUrl} />
    </div>
  );
}

function SectionTitle({ eyebrow, title, text, center = false }: { eyebrow: string; title: string; text?: string; center?: boolean }) {
  return (
    <div className={center ? "mx-auto max-w-2xl text-center" : "max-w-2xl"}>
      <p className="text-xs font-bold uppercase tracking-[0.08em] text-[color:var(--di-highlight)]">{eyebrow}</p>
      <h2 className={`mt-3 text-3xl font-bold leading-tight md:text-4xl ${titulo}`}>{title}</h2>
      {text && <p className={`mt-4 text-base leading-relaxed ${muted}`}>{text}</p>}
    </div>
  );
}

function Hero({ loginUrl }: { loginUrl: string }) {
  return (
    <section id="inicio" className="relative overflow-hidden text-[color:var(--di-ingreso-texto)]" style={TINTA}>
      {/* El panel de tinta del ingreso, con su aro decorativo a la derecha. */}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -right-[320px] top-[10%] h-[640px] w-[640px] rounded-full border-[1.5px]"
        style={{ borderColor: "color-mix(in srgb, var(--di-ingreso-acento-2) 45%, transparent)" }}
      />
      <div className="relative mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 pb-20 [&>*]:min-w-0 pt-16 md:px-6 lg:grid-cols-[1.1fr_1fr] lg:pb-28 lg:pt-24">
        <div>
          <p
            className="inline-flex items-center gap-2 rounded-full border px-3 py-1 text-xs font-bold uppercase tracking-[0.08em] text-[color:var(--di-ingreso-acento-2)]"
            style={{
              background: "color-mix(in srgb, var(--di-ingreso-texto) 10%, transparent)",
              borderColor: "color-mix(in srgb, var(--di-ingreso-texto) 16%, transparent)",
            }}
          >
            <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
            {COMPANY.headline}
          </p>
          <h1 className="mt-6 text-4xl font-extrabold leading-[1.08] tracking-[-0.01em] md:text-5xl lg:text-6xl">
            Trámites, comparendos y vehículos{" "}
            <span className="bg-clip-text text-transparent" style={DEGRADADO_TINTA}>
              en una sola plataforma
            </span>
          </h1>
          <p className="mt-6 max-w-xl text-lg leading-relaxed text-[color:var(--di-ingreso-texto-2)]">{COMPANY.summary}</p>
          <div className="mt-8 flex flex-wrap gap-3">
            <a href={loginUrl} className={buttonClass({ className: "px-6" })}>
              Iniciar sesión <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </a>
            <a href={demoMailto()} className={BOTON_TINTA}>
              Solicitar una demostración
            </a>
          </div>
          <ul className="mt-10 flex flex-wrap gap-x-6 gap-y-2 text-sm text-[color:var(--di-ingreso-texto-2)]">
            {["Integrado con RUNT y SIMIT", "Una sola cuenta para todo", "Auditoría de cada operación"].map((t) => (
              <li key={t} className="inline-flex items-center gap-2">
                <Check className="h-4 w-4 text-[color:var(--di-ingreso-acento-2)]" aria-hidden="true" />
                {t}
              </li>
            ))}
          </ul>
        </div>

        {/* La suite de un vistazo: los tres productos en tarjetas claras sobre la tinta, como las flotantes del ingreso. */}
        <div
          className="relative rounded-[28px] border p-6 md:p-8"
          style={{
            background: "color-mix(in srgb, var(--di-ingreso-texto) 8%, transparent)",
            borderColor: "color-mix(in srgb, var(--di-ingreso-texto) 16%, transparent)",
          }}
        >
          <div className="flex items-center justify-between">
            <BrandLogo variant="white" className="h-8 w-auto" />
            <span
              className="rounded-full px-3 py-1 text-xs font-semibold"
              style={{ background: "color-mix(in srgb, var(--di-ingreso-texto) 14%, transparent)" }}
            >
              Suite FLIT
            </span>
          </div>
          <ul className="mt-8 space-y-3">
            {PRODUCTS.map((p) => (
              <li
                key={p.code}
                className="flex items-center gap-4 rounded-2xl bg-[color:var(--di-ingreso-tarjeta)] p-4 text-[color:var(--di-ingreso-fondo)] shadow-[var(--di-ingreso-sombra-tarjeta)]"
              >
                <ProductArt code={p.code} icon={p.icon} size="sm" muted={p.status === "soon"} />
                <div className="min-w-0 flex-1">
                  <p className="flex items-center gap-2 font-semibold">
                    {p.name}
                    <StatusChip status={p.status} />
                  </p>
                  <p className="truncate text-xs text-[color:var(--di-ingreso-tarjeta-texto-2)]">{p.tagline}</p>
                </div>
              </li>
            ))}
          </ul>
          <p className="mt-6 text-center text-xs font-medium text-[color:var(--di-ingreso-texto-2)]">Una cuenta · Todos tus productos</p>
        </div>
      </div>
    </section>
  );
}

function ProductsOverview() {
  return (
    <section id="productos" className="mx-auto max-w-7xl scroll-mt-20 px-4 py-20 md:px-6">
      <SectionTitle
        eyebrow="Productos"
        title="Una suite para toda la operación vehicular"
        text="Empieza por lo que más necesitas y suma productos cuando quieras: todos comparten la misma cuenta, los mismos usuarios y la misma información."
        center
      />
      <div className="mt-12 grid gap-5 md:grid-cols-3">
        {PRODUCTS.map((p) => (
          <a key={p.code} href={`#producto-${p.code}`} className={`${card} group flex flex-col p-6 transition hover:-translate-y-0.5 hover:border-[color:var(--di-border-strong)]`}>
            <div className="flex items-start justify-between gap-3">
              <ProductArt code={p.code} icon={p.icon} muted={p.status === "soon"} />
              <StatusChip status={p.status} />
            </div>
            <h3 className={`mt-4 text-lg font-semibold ${titulo}`}>{p.name}</h3>
            <p className={`mt-2 flex-1 text-sm leading-relaxed ${muted}`}>{p.tagline}</p>
            <span className="mt-5 inline-flex items-center gap-1 text-sm font-semibold text-[color:var(--di-action)]">
              Conocer más <ArrowRight className="h-4 w-4 transition group-hover:translate-x-0.5" aria-hidden="true" />
            </span>
          </a>
        ))}
      </div>
    </section>
  );
}

function ProductSection({ product: p, reverse, loginUrl }: { product: ProductInfo; reverse: boolean; loginUrl: string }) {
  const available = p.status === "available";
  return (
    <section id={`producto-${p.code}`} className={`scroll-mt-20 border-t ${linea}`}>
      <div className={`mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 py-20 md:px-6 lg:grid-cols-2 [&>*]:min-w-0 ${reverse ? "lg:[&>*:first-child]:order-2" : ""}`}>
        <div>
          <div className="flex items-center gap-4">
            <ProductArt code={p.code} icon={p.icon} size="lg" muted={!available} />
            <div>
              <h2 className={`text-3xl font-bold ${titulo}`}>{p.name}</h2>
              <div className="mt-1">
                <StatusChip status={p.status} />
              </div>
            </div>
          </div>
          <p className={`mt-6 text-lg font-medium leading-relaxed ${titulo}`}>{p.tagline}</p>
          <p className={`mt-3 leading-relaxed ${muted}`}>{p.description}</p>
          <div className="mt-8 flex flex-wrap gap-3">
            {available ? (
              <a href={loginUrl} className={buttonClass()}>
                Entrar a {p.name} <ArrowRight className="h-4 w-4" aria-hidden="true" />
              </a>
            ) : (
              <a
                href={`mailto:${CONTACT.email}?subject=${encodeURIComponent(`Quiero saber cuándo llega ${p.name}`)}`}
                className={buttonClass()}
              >
                Quiero saber cuándo llega <Mail className="h-4 w-4" aria-hidden="true" />
              </a>
            )}
            <a href={demoMailto()} className={buttonClass({ variant: "ghost" })}>
              Hablar con un asesor
            </a>
          </div>
        </div>

        <div className={`${card} p-6 md:p-8`}>
          <p className="text-xs font-bold uppercase tracking-[0.08em] text-[color:var(--di-highlight)]">Qué incluye</p>
          <ul className="mt-5 grid gap-4 sm:grid-cols-2">
            {p.features.map((f) => (
              <li key={f} className="flex gap-3 text-sm leading-relaxed">
                <span className="mt-0.5 grid h-5 w-5 shrink-0 place-items-center rounded-full bg-[color:var(--di-success-soft)] text-[color:var(--di-success)]">
                  <Check className="h-3 w-3" aria-hidden="true" />
                </span>
                {f}
              </li>
            ))}
          </ul>
          <div className={`mt-8 border-t pt-6 ${linea}`}>
            <p className="text-xs font-bold uppercase tracking-[0.08em] text-[color:var(--di-text-tertiary)]">Pensado para</p>
            <ul className="mt-3 flex flex-wrap gap-2">
              {p.audiences.map((a) => (
                <li key={a} className="rounded-full bg-[color:var(--di-surface-sunken)] px-3 py-1 text-xs font-medium text-[color:var(--di-text-primary)]">
                  {a}
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </section>
  );
}

function WhyFlit() {
  return (
    <section id="por-que-flit" className={`scroll-mt-20 border-t bg-[color:var(--di-surface-sunken)] ${linea}`}>
      <div className="mx-auto max-w-7xl px-4 py-20 md:px-6">
        <SectionTitle eyebrow="Por qué FLIT" title="Hecha para cómo funciona la movilidad en Colombia" center />
        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {PILLARS.map((pillar) => (
            <div key={pillar.title} className={`${card} p-6`}>
              <span className="grid h-11 w-11 place-items-center rounded-full bg-[color:var(--di-accent-soft)] text-[color:var(--di-action)]">
                {createElement(PILLAR_ICONS[pillar.icon as keyof typeof PILLAR_ICONS], { className: "h-5 w-5", "aria-hidden": true })}
              </span>
              <h3 className={`mt-5 font-semibold ${titulo}`}>{pillar.title}</h3>
              <p className={`mt-2 text-sm leading-relaxed ${muted}`}>{pillar.text}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

function About() {
  return (
    <section id="nosotros" className="mx-auto grid max-w-7xl scroll-mt-20 grid-cols-1 gap-12 px-4 py-20 md:px-6 lg:grid-cols-2 [&>*]:min-w-0">
      <div>
        <SectionTitle eyebrow="Nosotros" title={`Somos ${COMPANY.name}`} text={COMPANY.summary} />
        <p className={`mt-4 leading-relaxed ${muted}`}>{COMPANY.mission}</p>
      </div>
      <ul className="grid gap-4">
        {COMPANY.values.map((v, i) => (
          <li key={v.title} className={`${card} flex gap-5 p-6`}>
            <span className="bg-clip-text text-3xl font-bold text-transparent" style={{ backgroundImage: "var(--di-gradient-action)" }}>
              0{i + 1}
            </span>
            <div>
              <h3 className={`font-semibold ${titulo}`}>{v.title}</h3>
              <p className={`mt-1 text-sm leading-relaxed ${muted}`}>{v.text}</p>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

function HowToStart() {
  return (
    <section id="como-empezar" className={`scroll-mt-20 border-t bg-[color:var(--di-surface-sunken)] ${linea}`}>
      <div className="mx-auto max-w-7xl px-4 py-20 md:px-6">
        <SectionTitle
          eyebrow="Cómo empezar"
          title="De la primera conversación a tu equipo operando"
          text="Cada empresa es distinta: armamos contigo la propuesta según tus productos, usuarios y volumen."
          center
        />
        <ol className="mt-12 grid gap-5 md:grid-cols-5">
          {STEPS.map((step, i) => (
            <li key={step.title} className={`${card} relative p-6`}>
              <span className="grid h-9 w-9 place-items-center rounded-full text-sm font-semibold text-[color:var(--di-on-action)]" style={{ background: "var(--di-gradient-action)" }}>
                {i + 1}
              </span>
              <h3 className={`mt-4 font-semibold ${titulo}`}>{step.title}</h3>
              <p className={`mt-2 text-sm leading-relaxed ${muted}`}>{step.text}</p>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}

function Contact() {
  return (
    <section id="contacto" className="mx-auto max-w-7xl scroll-mt-20 px-4 py-20 md:px-6">
      <div className="relative overflow-hidden rounded-[28px] px-6 py-14 text-center text-[color:var(--di-ingreso-texto)] md:px-16" style={TINTA}>
        <div
          aria-hidden="true"
          className="pointer-events-none absolute -right-20 -top-24 h-72 w-72 rounded-full border-[1.5px]"
          style={{ borderColor: "color-mix(in srgb, var(--di-ingreso-acento-2) 45%, transparent)" }}
        />
        <div
          aria-hidden="true"
          className="pointer-events-none absolute -bottom-24 -left-16 h-72 w-72 rounded-full"
          style={{ background: "color-mix(in srgb, var(--di-ingreso-texto) 8%, transparent)" }}
        />
        <h2 className="relative text-3xl font-extrabold md:text-4xl">¿Hablamos de tu operación?</h2>
        <p className="relative mx-auto mt-4 max-w-2xl text-[color:var(--di-ingreso-texto-2)]">
          Cuéntanos qué necesitas y te mostramos cómo FLIT puede ayudarte. Te respondemos con una demostración a la medida.
        </p>
        <div className="relative mt-8 flex flex-wrap justify-center gap-3">
          <a href={demoMailto()} className={buttonClass({ className: "px-6" })}>
            <Mail className="h-4 w-4" aria-hidden="true" /> {CONTACT.email}
          </a>
          <a href={CONTACT.web} target="_blank" rel="noopener" className={BOTON_TINTA}>
            <Globe className="h-4 w-4" aria-hidden="true" /> {CONTACT.web.replace("https://", "")}
            <ArrowUpRight className="h-4 w-4" aria-hidden="true" />
          </a>
        </div>
      </div>
    </section>
  );
}

function Footer({ loginUrl }: { loginUrl: string }) {
  const col = "text-sm text-[color:var(--di-text-secondary)] transition hover:text-[color:var(--di-action)]";
  const cabeza = `text-sm font-semibold ${titulo}`;
  return (
    <footer className={`border-t ${linea}`}>
      <div className="mx-auto grid max-w-7xl gap-10 px-4 py-14 md:grid-cols-[1.5fr_1fr_1fr_1fr] md:px-6">
        <div>
          <BrandLogo variant="dark" className="h-9 w-auto dark:hidden" />
          <BrandLogo variant="white" className="hidden h-9 w-auto dark:block" />
          <p className={`mt-4 max-w-xs text-sm leading-relaxed ${muted}`}>{COMPANY.headline}.</p>
        </div>
        <nav aria-label="Productos">
          <p className={cabeza}>Productos</p>
          <ul className="mt-4 space-y-2">
            {PRODUCTS.map((p) => (
              <li key={p.code}>
                <a href={`#producto-${p.code}`} className={col}>
                  {p.name}
                </a>
              </li>
            ))}
          </ul>
        </nav>
        <nav aria-label="Empresa">
          <p className={cabeza}>Empresa</p>
          <ul className="mt-4 space-y-2">
            <li><a href="#nosotros" className={col}>Nosotros</a></li>
            <li><a href="#como-empezar" className={col}>Cómo empezar</a></li>
            <li><a href="#contacto" className={col}>Contacto</a></li>
          </ul>
        </nav>
        <nav aria-label="Acceso">
          <p className={cabeza}>Acceso</p>
          <ul className="mt-4 space-y-2">
            <li><a href={loginUrl} className={col}>Iniciar sesión</a></li>
            <li><a href={`mailto:${CONTACT.email}`} className={col}>Soporte</a></li>
          </ul>
        </nav>
      </div>
      <p className={`border-t px-4 py-5 text-center text-[11px] text-[color:var(--di-text-tertiary)] ${linea}`}>
        <a href={FLIT_PRIVACY_POLICY_URL} target="_blank" rel="noopener noreferrer" className="underline-offset-2 hover:underline">
          Políticas de Privacidad y Términos de Uso
        </a>{" "}
        · © {new Date().getFullYear()} FLIT · Todos los derechos reservados · Protegido por cifrado TLS · Auditoría continua
      </p>
    </footer>
  );
}

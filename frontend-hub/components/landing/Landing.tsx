import { createElement } from "react";
import { ArrowRight, ArrowUpRight, Check, Globe, KeyRound, Mail, Palette, Plug, ShieldCheck, Sparkles } from "lucide-react";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { PRODUCTS, type ProductInfo } from "@/lib/products";
import { COMPANY, CONTACT, PILLARS, STEPS, demoMailto } from "@/lib/site";
import { LandingNav } from "./LandingNav";
import { BRAND_GRADIENT as GRADIENT, ProductIcon } from "@/components/ui/ProductIcon";
import { StatusChip } from "@/components/ui/StatusChip";

// Landing de FLIT en la raíz del hub, sin sesión (B-11): qué es FLIT, sus productos, cómo empezar y contacto. Sigue
// la paleta y el lenguaje de Trámites (degradado cian → azul, tarjetas blancas con borde suave, Poppins) y tiene modo
// oscuro. Sin precios por decisión de negocio. El texto vive en lib/products.ts y lib/site.ts.

const PILLAR_ICONS = { "key-round": KeyRound, plug: Plug, "shield-check": ShieldCheck, palette: Palette } as const;

const card =
  "rounded-2xl border border-[var(--color-flit-gray)] bg-white shadow-[var(--nav-sombra-dock)] dark:border-white/10 dark:bg-[#0B0F14]";
const muted = "text-[#59677d] dark:text-white/65";

export function Landing({ loginUrl }: { loginUrl: string }) {
  return (
    <div className="min-h-full bg-[var(--color-flit-bg)] text-flit-primary dark:bg-[#05060A] dark:text-white">
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
      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-flit-brand">{eyebrow}</p>
      <h2 className="mt-3 text-3xl font-semibold leading-tight md:text-4xl">{title}</h2>
      {text && <p className={`mt-4 text-base leading-relaxed ${muted}`}>{text}</p>}
    </div>
  );
}

function Hero({ loginUrl }: { loginUrl: string }) {
  return (
    <section id="inicio" className="relative overflow-hidden">
      {/* El mismo fondo de Trámites: dos halos suaves de marca. */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 opacity-90 dark:opacity-60"
        style={{
          backgroundImage:
            "radial-gradient(at 15% 0%, rgba(85,126,255,0.22), transparent 55%), radial-gradient(at 95% 60%, rgba(0,219,213,0.18), transparent 50%)",
        }}
      />
      <div className="relative mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 pb-20 [&>*]:min-w-0 pt-16 md:px-6 lg:grid-cols-[1.1fr_1fr] lg:pb-28 lg:pt-24">
        <div>
          <p className="inline-flex items-center gap-2 rounded-full border border-flit-brand/20 bg-white/70 px-3 py-1 text-xs font-semibold text-flit-brand dark:border-white/10 dark:bg-white/5">
            <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
            {COMPANY.headline}
          </p>
          <h1 className="mt-6 text-4xl font-semibold leading-[1.1] tracking-tight md:text-5xl lg:text-6xl">
            Trámites, comparendos y vehículos{" "}
            <span className="bg-clip-text text-transparent" style={{ backgroundImage: GRADIENT }}>
              en una sola plataforma
            </span>
          </h1>
          <p className={`mt-6 max-w-xl text-lg leading-relaxed ${muted}`}>{COMPANY.summary}</p>
          <div className="mt-8 flex flex-wrap gap-3">
            <a href={loginUrl} className="inline-flex items-center gap-2 rounded-full bg-flit-brand px-6 py-3 text-sm font-semibold text-white shadow-[var(--nav-sombra-activo)] transition hover:opacity-90">
              Iniciar sesión <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </a>
            <a
              href={demoMailto()}
              className="inline-flex items-center gap-2 rounded-full border border-[var(--color-flit-gray)] bg-white px-6 py-3 text-sm font-semibold text-flit-primary transition hover:border-flit-brand dark:border-white/15 dark:bg-white/5 dark:text-white"
            >
              Solicitar una demostración
            </a>
          </div>
          <ul className={`mt-10 flex flex-wrap gap-x-6 gap-y-2 text-sm ${muted}`}>
            {["Integrado con RUNT y SIMIT", "Una sola cuenta para todo", "Auditoría de cada operación"].map((t) => (
              <li key={t} className="inline-flex items-center gap-2">
                <Check className="h-4 w-4 text-flit-tech" aria-hidden="true" />
                {t}
              </li>
            ))}
          </ul>
        </div>

        {/* La suite de un vistazo: los tres productos sobre el degradado de marca. */}
        <div className="relative">
          <div className="absolute -inset-4 rounded-[2rem] opacity-30 blur-2xl" style={{ background: GRADIENT }} aria-hidden="true" />
          <div className="relative rounded-[2rem] p-6 md:p-8" style={{ background: GRADIENT }}>
            <div className="flex items-center justify-between text-white">
              <BrandLogo variant="white" className="h-8 w-auto" />
              <span className="rounded-full bg-white/20 px-3 py-1 text-xs font-semibold">Suite FLIT</span>
            </div>
            <ul className="mt-8 space-y-3">
              {PRODUCTS.map((p) => (
                <li key={p.code} className="flex items-center gap-4 rounded-2xl bg-white/95 p-4 shadow-lg dark:bg-[#0B0F14]/95">
                  <ProductIcon icon={p.icon} />
                  <div className="min-w-0 flex-1">
                    <p className="flex items-center gap-2 font-semibold text-flit-primary dark:text-white">
                      {p.name}
                      <StatusChip status={p.status} />
                    </p>
                    <p className="truncate text-xs text-[#59677d] dark:text-white/60">{p.tagline}</p>
                  </div>
                </li>
              ))}
            </ul>
            <p className="mt-6 text-center text-xs font-medium text-white/85">Una cuenta · Todos tus productos</p>
          </div>
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
          <a key={p.code} href={`#producto-${p.code}`} className={`${card} group flex flex-col p-6 transition hover:-translate-y-0.5 hover:border-flit-brand`}>
            <div className="flex items-center justify-between">
              <ProductIcon icon={p.icon} />
              <StatusChip status={p.status} />
            </div>
            <h3 className="mt-5 text-lg font-semibold">{p.name}</h3>
            <p className={`mt-2 flex-1 text-sm leading-relaxed ${muted}`}>{p.tagline}</p>
            <span className="mt-5 inline-flex items-center gap-1 text-sm font-semibold text-flit-brand">
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
    <section id={`producto-${p.code}`} className="scroll-mt-20 border-t border-[var(--color-flit-gray)]/70 dark:border-white/5">
      <div className={`mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 py-20 md:px-6 lg:grid-cols-2 [&>*]:min-w-0 ${reverse ? "lg:[&>*:first-child]:order-2" : ""}`}>
        <div>
          <div className="flex items-center gap-3">
            <ProductIcon icon={p.icon} size="lg" />
            <div>
              <h2 className="text-3xl font-semibold">{p.name}</h2>
              <div className="mt-1">
                <StatusChip status={p.status} />
              </div>
            </div>
          </div>
          <p className="mt-6 text-lg font-medium leading-relaxed">{p.tagline}</p>
          <p className={`mt-3 leading-relaxed ${muted}`}>{p.description}</p>
          <div className="mt-8 flex flex-wrap gap-3">
            {available ? (
              <a href={loginUrl} className="inline-flex items-center gap-2 rounded-full bg-flit-brand px-5 py-2.5 text-sm font-semibold text-white transition hover:opacity-90">
                Entrar a {p.name} <ArrowRight className="h-4 w-4" aria-hidden="true" />
              </a>
            ) : (
              <a
                href={`mailto:${CONTACT.email}?subject=${encodeURIComponent(`Quiero saber cuándo llega ${p.name}`)}`}
                className="inline-flex items-center gap-2 rounded-full bg-flit-brand px-5 py-2.5 text-sm font-semibold text-white transition hover:opacity-90"
              >
                Quiero saber cuándo llega <Mail className="h-4 w-4" aria-hidden="true" />
              </a>
            )}
            <a href={demoMailto()} className="inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-flit-brand transition hover:bg-flit-brand/10">
              Hablar con un asesor
            </a>
          </div>
        </div>

        <div className={`${card} p-6 md:p-8`}>
          <p className="text-xs font-semibold uppercase tracking-[0.16em] text-flit-brand">Qué incluye</p>
          <ul className="mt-5 grid gap-4 sm:grid-cols-2">
            {p.features.map((f) => (
              <li key={f} className="flex gap-3 text-sm leading-relaxed">
                <span className="mt-0.5 grid h-5 w-5 shrink-0 place-items-center rounded-full bg-flit-tech/15 text-[#00a8a3] dark:text-flit-tech">
                  <Check className="h-3 w-3" aria-hidden="true" />
                </span>
                {f}
              </li>
            ))}
          </ul>
          <div className="mt-8 border-t border-[var(--color-flit-gray)] pt-6 dark:border-white/10">
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-[#59677d] dark:text-white/50">Pensado para</p>
            <ul className="mt-3 flex flex-wrap gap-2">
              {p.audiences.map((a) => (
                <li key={a} className="rounded-full bg-[var(--color-flit-bg)] px-3 py-1 text-xs font-medium text-flit-primary dark:bg-white/5 dark:text-white/80">
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
    <section id="por-que-flit" className="scroll-mt-20 border-t border-[var(--color-flit-gray)]/70 bg-white/60 dark:border-white/5 dark:bg-white/[0.02]">
      <div className="mx-auto max-w-7xl px-4 py-20 md:px-6">
        <SectionTitle eyebrow="Por qué FLIT" title="Hecha para cómo funciona la movilidad en Colombia" center />
        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {PILLARS.map((pillar) => (
            <div key={pillar.title} className={`${card} p-6`}>
              <span className="grid h-11 w-11 place-items-center rounded-xl bg-flit-brand/10 text-flit-brand">
                {createElement(PILLAR_ICONS[pillar.icon as keyof typeof PILLAR_ICONS], { className: "h-5 w-5", "aria-hidden": true })}
              </span>
              <h3 className="mt-5 font-semibold">{pillar.title}</h3>
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
            <span className="text-3xl font-semibold text-transparent bg-clip-text" style={{ backgroundImage: GRADIENT }}>
              0{i + 1}
            </span>
            <div>
              <h3 className="font-semibold">{v.title}</h3>
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
    <section id="como-empezar" className="scroll-mt-20 border-t border-[var(--color-flit-gray)]/70 bg-white/60 dark:border-white/5 dark:bg-white/[0.02]">
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
              <span className="grid h-9 w-9 place-items-center rounded-full text-sm font-semibold text-white" style={{ background: GRADIENT }}>
                {i + 1}
              </span>
              <h3 className="mt-4 font-semibold">{step.title}</h3>
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
      <div className="relative overflow-hidden rounded-[2rem] px-6 py-14 text-center text-white md:px-16" style={{ background: GRADIENT }}>
        <div aria-hidden="true" className="pointer-events-none absolute -right-20 -top-20 h-72 w-72 rounded-full bg-white/10" />
        <div aria-hidden="true" className="pointer-events-none absolute -bottom-24 -left-16 h-72 w-72 rounded-full bg-white/10" />
        <h2 className="relative text-3xl font-semibold md:text-4xl">¿Hablamos de tu operación?</h2>
        <p className="relative mx-auto mt-4 max-w-2xl text-white/90">
          Cuéntanos qué necesitas y te mostramos cómo FLIT puede ayudarte. Te respondemos con una demostración a la medida.
        </p>
        <div className="relative mt-8 flex flex-wrap justify-center gap-3">
          <a href={demoMailto()} className="inline-flex items-center gap-2 rounded-full bg-white px-6 py-3 text-sm font-semibold text-flit-primary transition hover:opacity-90">
            <Mail className="h-4 w-4" aria-hidden="true" /> {CONTACT.email}
          </a>
          <a
            href={CONTACT.web}
            target="_blank"
            rel="noopener"
            className="inline-flex items-center gap-2 rounded-full border border-white/50 px-6 py-3 text-sm font-semibold text-white transition hover:bg-white/10"
          >
            <Globe className="h-4 w-4" aria-hidden="true" /> {CONTACT.web.replace("https://", "")}
            <ArrowUpRight className="h-4 w-4" aria-hidden="true" />
          </a>
        </div>
      </div>
    </section>
  );
}

function Footer({ loginUrl }: { loginUrl: string }) {
  const col = "text-sm text-[#59677d] transition hover:text-flit-brand dark:text-white/60 dark:hover:text-white";
  return (
    <footer className="border-t border-[var(--color-flit-gray)] dark:border-white/10">
      <div className="mx-auto grid max-w-7xl gap-10 px-4 py-14 md:grid-cols-[1.5fr_1fr_1fr_1fr] md:px-6">
        <div>
          <BrandLogo variant="dark" className="h-9 w-auto dark:hidden" />
          <BrandLogo variant="white" className="hidden h-9 w-auto dark:block" />
          <p className={`mt-4 max-w-xs text-sm leading-relaxed ${muted}`}>{COMPANY.headline}.</p>
        </div>
        <nav aria-label="Productos">
          <p className="text-sm font-semibold">Productos</p>
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
          <p className="text-sm font-semibold">Empresa</p>
          <ul className="mt-4 space-y-2">
            <li><a href="#nosotros" className={col}>Nosotros</a></li>
            <li><a href="#como-empezar" className={col}>Cómo empezar</a></li>
            <li><a href="#contacto" className={col}>Contacto</a></li>
          </ul>
        </nav>
        <nav aria-label="Acceso">
          <p className="text-sm font-semibold">Acceso</p>
          <ul className="mt-4 space-y-2">
            <li><a href={loginUrl} className={col}>Iniciar sesión</a></li>
            <li><a href={`mailto:${CONTACT.email}`} className={col}>Soporte</a></li>
          </ul>
        </nav>
      </div>
      <p className="border-t border-[var(--color-flit-gray)] px-4 py-5 text-center text-[11px] text-[#59677d] dark:border-white/10 dark:text-white/50">
        Políticas de Privacidad y Términos de Uso · © {new Date().getFullYear()} FLIT · Todos los derechos reservados · Protegido por cifrado TLS · Auditoría continua
      </p>
    </footer>
  );
}

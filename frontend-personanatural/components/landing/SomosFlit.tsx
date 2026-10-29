import { SOMOS_FLIT } from "@/lib/landing/contenido";

export function SomosFlit() {
  return (
    <section id="nosotros" className="dashboard-card somos-flit grid items-center gap-6 md:grid-cols-2" aria-label="Somos FLIT">
      <div className="somos-reveal">
        <p className="somos-kicker">NOSOTROS</p>
        <h2 className="mt-2 text-3xl font-bold text-primary">Somos FLIT</h2>
        <p className="mt-3 text-sm leading-relaxed text-foreground/80">
          FLIT te ayuda a consultar y gestionar los trámites de tu vehículo en un solo lugar. Revisa tus pendientes, comparendos y su estado con información conectada a fuentes oficiales, de forma fácil, ágil y segura.
        </p>
      </div>
      <ul className="flex flex-col gap-4">
        {SOMOS_FLIT.map(({ numero, titulo, texto }) => (
          <li key={numero} className="somos-card somos-reveal rounded-2xl bg-card p-6">
            <div className="flex items-center gap-3">
              <span className="somos-card-num text-2xl font-bold">{numero}</span>
              <h3 className="font-bold text-primary">{titulo}</h3>
            </div>
            <p className="mt-2 text-sm text-foreground/70">{texto}</p>
          </li>
        ))}
      </ul>
    </section>
  );
}

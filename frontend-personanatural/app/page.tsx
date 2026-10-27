// Página de humo de la base visual (HU #14052): cabecera, contenido y pie con las clases del diseño aprobado. La landing
// real (organismos, consultar, cotizar e iniciar trámite) llega con las HU E2 a E5.
export default function Inicio() {
  return (
    <div className="dashboard-shell bg-background font-sans text-foreground">
      <header className="dashboard-header flex min-h-16 flex-wrap py-2 shrink-0 items-center justify-between gap-4 border-b border-border">
        <span className="text-lg font-bold text-primary">FLIT 2.0</span>
      </header>
      <main className="dashboard-layout">
        <section aria-label="Bienvenida" className="dashboard-card">
          <h1 className="dashboard-title">Trámites de tránsito 100% en línea</h1>
          <p>Estamos preparando tu portal.</p>
        </section>
      </main>
      <footer className="dashboard-footer">
        <p>© 2026 FLIT · Todos los derechos reservados · Protegido por cifrado TLS</p>
      </footer>
    </div>
  );
}

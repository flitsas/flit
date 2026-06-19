// Contenedor visual común de las pantallas de autenticación.
export function AuthCard({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
}) {
  return (
    <main className="min-h-screen flex items-center justify-center bg-[#eef5ff] px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-xl p-8">
        <h1 className="text-2xl font-bold text-[#162744] mb-1">{title}</h1>
        {subtitle && <p className="text-sm text-slate-500 mb-6">{subtitle}</p>}
        {children}
      </div>
    </main>
  );
}

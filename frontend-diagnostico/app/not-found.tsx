import Link from "next/link";

export default function NotFound() {
  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <h1 className="text-2xl font-semibold text-flit-primary dark:text-white">Esta página no existe</h1>
      <Link
        href="/"
        className="font-semibold text-[var(--flit-brand-ink)] underline underline-offset-4 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--nav-focus)]"
      >
        Volver al inicio
      </Link>
    </main>
  );
}

import { Suspense } from "react";
import { PestanasReportes } from "@/components/reportes/PestanasReportes";

// Reportes 2.0 — un solo menú: Resumen | Gestión | Derechos de petición | Productividad | Consultas | Plataforma.
export default function ReportesLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-4">
      <Suspense fallback={null}>
        <PestanasReportes />
      </Suspense>
      {children}
    </div>
  );
}

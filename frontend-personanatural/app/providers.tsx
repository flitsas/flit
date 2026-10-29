"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { ApiError } from "@/lib/api/errores";

// Caché y estados de petición. No se reintenta ante errores del back: un 429 o un 503 piden esperar, no insistir, y los
// 4xx no mejoran repitiéndolos.
export function Providers({ children }: { children: React.ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: { retry: (intentos, error) => !(error instanceof ApiError) && intentos < 2, refetchOnWindowFocus: false },
          mutations: { retry: false },
        },
      }),
  );
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

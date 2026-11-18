'use client';

import { UiStateBoundary } from '@/components/admin/UiStateBoundary';

export default function CajaError({ reset }: { error: Error; reset: () => void }) {
  return <UiStateBoundary status="error" errorMessage="No pudimos abrir la caja. Intente de nuevo." onRetry={reset} />;
}

import type { AiProviderCode, AiToolCode, AiTransitionReason } from "@/lib/api/admin-plataforma-ai";

export function etiquetaHerramienta(code: AiToolCode): string {
  switch (code) {
    case "ocr":
      return "OCR";
    case "dr_flit":
      return "DR. FLIT";
    default:
      return String(code);
  }
}

export function descripcionHerramienta(code: AiToolCode): string {
  switch (code) {
    case "ocr":
      return "Análisis visual de documentos (licencias, improntas y similares).";
    case "dr_flit":
      return "Asistente conversacional Dr. FLIT (chat de soporte y guía).";
    default:
      return "Herramienta de inteligencia artificial de plataforma.";
  }
}

export function etiquetaProveedor(code: AiProviderCode): string {
  switch (code) {
    case "anthropic":
      return "Anthropic";
    case "openai":
      return "OpenAI";
    case "kimi":
      return "Kimi";
    case "deepseek":
      return "DeepSeek";
    default:
      return String(code);
  }
}

export function etiquetaMotivoTransicion(reason: AiTransitionReason): string {
  switch (reason) {
    case "manual":
      return "Manual";
    case "failover_auto":
      return "Failover automático";
    case "auto_return":
      return "Retorno al primario";
    case "seed":
      return "Inicialización";
    default:
      return String(reason);
  }
}

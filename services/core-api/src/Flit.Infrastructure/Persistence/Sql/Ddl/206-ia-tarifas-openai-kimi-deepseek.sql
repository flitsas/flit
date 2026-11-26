-- ─────────────────────────────────────────────────────────────────────────────
-- Tarifas de OpenAI, Kimi y DeepSeek en admin.ia_tarifa_modelo — contrato ia-consumo v1 (ADR-0090).
-- Migración EF: 20261126130000_IaTarifasOpenAiKimiDeepSeek.
--
-- El DDL 203 sembró solo Anthropic: las llamadas a OpenAI, Kimi y DeepSeek (respaldo de OCR y DR. FLIT) quedaban
-- con costo_usd_estimado NULL. Precios en USD por millón de tokens, de las páginas oficiales del 2026-11-26
-- (precio estándar, sin Batch):
--   openai   developers.openai.com/api/docs/pricing         gpt-4o, gpt-4o-mini
--   kimi     platform.kimi.ai/docs/pricing/chat              kimi-k3 (escritura de caché = la de 5 minutos)
--   deepseek api-docs.deepseek.com/quick_start/pricing       deepseek-flash, tarifa PICO (fuera de pico cobra la
--                                                            mitad: el costo puede quedar sobreestimado, nunca bajo)
-- El modelo es el que DEVUELVE la API, porque la tarifa se busca por modelo_respondido: DeepSeek responde
-- deepseek-flash aunque se pida deepseek-chat; OpenAI responde gpt-4o-2024-08-06 y la calculadora quita la fecha.
-- Misma lista que Flit.Platform.Sdk.IaConsumo.IaTarifasSemilla.OtrosProveedores (una prueba lo compara).
--
-- Después recalcula el costo de las filas de esos proveedores que ya quedaron con NULL, con la misma regla que
-- IaCalculadoraCosto: tarifa vigente más reciente del (proveedor, modelo exacto; si no, sin sufijo de fecha).
-- IDEMPOTENTE: ON CONFLICT DO NOTHING y el UPDATE solo toca filas con costo NULL.
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO admin.ia_tarifa_modelo
  (proveedor, modelo, vigente_desde, precio_entrada_mtok, precio_salida_mtok, precio_cache_lectura_mtok, precio_cache_escritura_mtok)
VALUES
  ('openai',   'gpt-4o',         DATE '2026-11-26', 2.50, 10.00, 1.25,  0.00),
  ('openai',   'gpt-4o-mini',    DATE '2026-11-26', 0.15,  0.60, 0.075, 0.00),
  ('kimi',     'kimi-k3',        DATE '2026-11-26', 3.00, 15.00, 0.30,  3.00),
  ('deepseek', 'deepseek-flash', DATE '2026-11-26', 0.30,  1.20, 0.006, 0.00)
ON CONFLICT (proveedor, modelo, vigente_desde) DO NOTHING;

WITH pendientes AS (
  SELECT l.id,
         (SELECT round((l.tokens_entrada         * t.precio_entrada_mtok
                      + l.tokens_salida          * t.precio_salida_mtok
                      + l.tokens_cache_lectura   * t.precio_cache_lectura_mtok
                      + l.tokens_cache_escritura * t.precio_cache_escritura_mtok) / 1000000.0, 6)
            FROM admin.ia_tarifa_modelo t
           WHERE lower(t.proveedor) = lower(l.proveedor)
             AND t.vigente_desde <= (l.fecha_inicio AT TIME ZONE 'UTC')::date
             AND lower(t.modelo) IN (lower(x.modelo), lower(regexp_replace(x.modelo, '-([0-9]{8}|[0-9]{4}-[0-9]{2}-[0-9]{2})$', '')))
           ORDER BY (lower(t.modelo) = lower(x.modelo)) DESC, t.vigente_desde DESC
           LIMIT 1) AS costo
    FROM admin.ia_consumo_log l
   CROSS JOIN LATERAL (SELECT coalesce(l.modelo_respondido, l.modelo_solicitado) AS modelo) x
   WHERE l.costo_usd_estimado IS NULL
     AND l.proveedor IN ('openai', 'kimi', 'deepseek')
     AND x.modelo IS NOT NULL
)
UPDATE admin.ia_consumo_log l
   SET costo_usd_estimado = p.costo
  FROM pendientes p
 WHERE l.id = p.id
   AND p.costo IS NOT NULL;

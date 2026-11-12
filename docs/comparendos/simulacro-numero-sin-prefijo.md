# Simulacro: número de comparendo sin prefijo (GDC punto 12)

La migración `20261106094303_GDC_NumeroComparendoSoloDigitos` quita los prefijos de letras (`D-`, `A-`) del número
normalizado cuando quedan 10 dígitos o más. Lo que choca con otro número del mismo tenant no se toca y la migración lo
reporta con `RAISE NOTICE`.

Estas cuatro consultas son **de solo lectura** (`SELECT`, sin `UPDATE`). Sirven para:

- **Producción, antes de desplegar:** saber cuántas filas va a limpiar la migración y cuáles quedarán para revisión.
- **QA y DEV, después:** allí se aplicó la versión anterior de la migración, que limpiaba `documento_ocr` sin revisar
  choques. La consulta 4 muestra los documentos que quedaron desfasados de su comparendo.

Las consultas 1 a 3 dan el resultado esperado solo **antes** de aplicar la migración. Se corren en una sesión de solo
lectura:

```sql
BEGIN TRANSACTION READ ONLY;
-- consulta
ROLLBACK;
```

## 1. Comparendos con prefijo, por tenant

Cuántos comparendos tienen prefijo y cuántos de ellos ya tienen un gemelo sin letra en el mismo tenant.

```sql
WITH candidato AS (
    SELECT id, tenant_id, regexp_replace(numero_normalizado, '^[A-Za-z]+', '') AS limpio
    FROM comparendos.comparendo
    WHERE numero_normalizado ~ '^[A-Za-z]+[0-9]{10,}$'
)
SELECT c.tenant_id,
       count(*) AS con_prefijo,
       count(*) FILTER (WHERE EXISTS (
           SELECT 1 FROM comparendos.comparendo x
           WHERE x.tenant_id = c.tenant_id AND x.numero_normalizado = c.limpio)) AS con_gemelo_sin_letra
FROM candidato c
GROUP BY c.tenant_id
ORDER BY con_prefijo DESC;
```

## 2. Comparendos que la migración dejará sin cambiar (choques)

Cada fila es un comparendo con prefijo que choca: ya existe su gemelo sin letra, o hay otro con prefijo distinto que
quedaría con el mismo número. Es la misma lista que la migración reporta con `RAISE NOTICE`.

```sql
WITH candidato AS (
    SELECT id, tenant_id, numero_normalizado AS numero,
           regexp_replace(numero_normalizado, '^[A-Za-z]+', '') AS limpio
    FROM comparendos.comparendo
    WHERE numero_normalizado ~ '^[A-Za-z]+[0-9]{10,}$'
)
SELECT c.tenant_id, c.numero, c.limpio,
       CASE WHEN EXISTS (SELECT 1 FROM comparendos.comparendo x
                         WHERE x.tenant_id = c.tenant_id AND x.numero_normalizado = c.limpio)
            THEN 'ya existe sin letra'
            ELSE 'otro con prefijo quedaría igual' END AS motivo
FROM candidato c
WHERE EXISTS (SELECT 1 FROM comparendos.comparendo x
              WHERE x.tenant_id = c.tenant_id AND x.numero_normalizado = c.limpio)
   OR EXISTS (SELECT 1 FROM candidato o
              WHERE o.tenant_id = c.tenant_id AND o.limpio = c.limpio AND o.id <> c.id)
ORDER BY c.tenant_id, c.limpio, c.numero;
```

## 3. Documentos OCR con prefijo y qué les pasará

`se limpia` o `sin cambiar (choque)`, con la misma regla que la migración: un documento choca si su número sin letra ya
existe como comparendo o si el comparendo que lo comparte choca.

```sql
WITH candidato AS (
    SELECT id, tenant_id, regexp_replace(numero_normalizado, '^[A-Za-z]+', '') AS limpio
    FROM comparendos.comparendo
    WHERE numero_normalizado ~ '^[A-Za-z]+[0-9]{10,}$'
),
choque AS (
    SELECT DISTINCT c.tenant_id, c.limpio
    FROM candidato c
    WHERE EXISTS (SELECT 1 FROM comparendos.comparendo x
                  WHERE x.tenant_id = c.tenant_id AND x.numero_normalizado = c.limpio)
       OR EXISTS (SELECT 1 FROM candidato o
                  WHERE o.tenant_id = c.tenant_id AND o.limpio = c.limpio AND o.id <> c.id)
),
documento AS (
    SELECT id, tenant_id, estado, numero_normalizado AS numero,
           regexp_replace(numero_normalizado, '^[A-Za-z]+', '') AS limpio
    FROM comparendos.documento_ocr
    WHERE numero_normalizado ~ '^[A-Za-z]+[0-9]{10,}$'
)
SELECT d.tenant_id, d.estado, count(*) AS documentos,
       CASE WHEN EXISTS (SELECT 1 FROM choque k WHERE k.tenant_id = d.tenant_id AND k.limpio = d.limpio)
              OR EXISTS (SELECT 1 FROM comparendos.comparendo x
                         WHERE x.tenant_id = d.tenant_id AND x.numero_normalizado = d.limpio)
            THEN 'sin cambiar (choque)' ELSE 'se limpia' END AS resultado
FROM documento d
GROUP BY d.tenant_id, d.estado, resultado
ORDER BY d.tenant_id, d.estado, resultado;
```

## 4. Documentos OCR desfasados de su comparendo

Documentos cuyo número no coincide con el del comparendo al que apuntan (el que registraron o del que son duplicado).
En producción, antes de desplegar, debería dar cero filas. En QA y DEV muestra lo que dejó la versión anterior de la
migración: documento sin prefijo y comparendo con prefijo.

```sql
SELECT d.tenant_id, d.id AS documento_id, d.estado, d.numero_normalizado AS numero_documento,
       c.id AS comparendo_id, c.numero_normalizado AS numero_comparendo,
       CASE WHEN d.comparendo_id = c.id THEN 'registrado' ELSE 'duplicado de' END AS relacion
FROM comparendos.documento_ocr d
JOIN comparendos.comparendo c
  ON c.id = d.comparendo_id OR c.id = d.duplicado_de_comparendo_id
WHERE d.numero_normalizado IS DISTINCT FROM c.numero_normalizado
ORDER BY d.tenant_id, d.numero_normalizado;
```

Los documentos que aún no se registran (sin `comparendo_id`) no aparecen aquí aunque también hayan quedado sin prefijo:
la consulta 3 corrida **antes** de la migración es la que los cuenta.

## Dónde ver el reporte de la migración

Los `RAISE NOTICE` salen en la consola cuando la migración se aplica con `psql` (por ejemplo, el script de
`dotnet ef migrations script`). Cuando el servicio la aplica solo al arrancar, el aviso va al registro de Npgsql, que
por defecto no se muestra: por eso conviene correr las consultas 2 y 3 antes y guardar su resultado.

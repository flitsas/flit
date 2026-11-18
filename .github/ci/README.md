# Tiempos por clase para repartir los shards de core-api

`ci-core-api-pruebas.sh` reparte las clases de `Flit.Admin.Tests` (carril admin partido) y de
`Flit.Integration.Tests` (carril integration) entre los shards **por tiempo**, con estos archivos:

| Archivo | Carril |
|---|---|
| `tiempos-admin.tsv` | admin (`ADMIN_SHARDS` > 1) |
| `tiempos-integration.tsv` | integration (`SHARDS`) |

Formato: `clase<TAB>segundos`. Cada clase va al shard con menos carga, de la más lenta a la más rápida.
Una clase que no está en el archivo (nueva) pesa 10 s (`TIEMPO_NUEVA`). Si el archivo falta, el reparto vuelve a ser por turno.
El último shard sigue tomando «todo lo que no tomaron los demás», así que ninguna prueba se pierde aunque el archivo esté viejo.

## Regenerarlos

Cuando los shards se vean desparejos (más de ~3 min de diferencia en varias corridas), con los `.trx` de las últimas
corridas verdes de `develop` (artefactos `core-api-test-results-<carril>-<shard>`):

```bash
for id in <run_id_1> <run_id_2>; do
  for a in admin-0 admin-1; do gh run download $id -R flitsas/flit -n core-api-test-results-$a -D trx/$id/$a; done
done
cd trx && python3 ../.github/scripts/ci-tiempos-por-clase.py $(find . -name '*.trx' | sed 's|^\./||') > ../.github/ci/tiempos-admin.tsv
```

(Lo mismo con `integration-0..3` para `tiempos-integration.tsv`.) Se toma la mediana por clase entre corridas.
Generados por primera vez el 2026-11-18 con las corridas de `develop` 44380159114 y 44353388355.

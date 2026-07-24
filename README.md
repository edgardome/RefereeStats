# Referee Stats Scraper

Scraper de estadísticas de árbitros de fútbol (fuente: Transfermarkt), en .NET 8,
empaquetado como:

1. **Apify Actor** — contenedor Docker que lee input JSON y escribe al dataset de Apify.
2. **API REST** (`RefereeStats.WebApi`) — mismos datos vía HTTP, pensado para RapidAPI Hub.

Ambos hosts reutilizan `RefereeStats.Core` (scraping, parsing, export, modelos).

## Estructura

```
src/RefereeStats.Core/         Librería compartida: scraping, parsing, export, modelos
src/RefereeStats.ApifyActor/   Host 1 — consola ejecutada dentro del Actor de Apify
src/RefereeStats.WebApi/       Host 2 — API REST para RapidAPI
tests/RefereeStats.Core.Tests/ xUnit — parsing con fixtures HTML locales, tests de export
.actor/                        Manifest, input schema y Dockerfile del Actor
```

## Build local

```bash
dotnet restore RefereeStats.slnx
dotnet build RefereeStats.slnx
dotnet test RefereeStats.slnx
```

## Ejecutar la WebApi localmente

```bash
dotnet run --project src/RefereeStats.WebApi
```

Swagger UI en `https://localhost:<port>/swagger` (solo en entorno Development).

Endpoints:

- `GET /api/referees/{refereeId}/profile?league={league}&season={season}` — Nivel 1
- `GET /api/referees/{refereeId}/matches?since={date}` — Nivel 2
- `GET /api/referees/{refereeId}/analytics` — Nivel 3
- `GET /api/leagues/{league}/referees?season={season}` — listado de árbitros

### Autenticación RapidAPI

RapidAPI inyecta la cabecera `X-RapidAPI-Proxy-Secret` en cada petición reenviada a tu
backend. `RapidApiProxySecretMiddleware` valida esa cabecera contra `RapidApi:ProxySecret`
(appsettings o variable de entorno `RapidApi__ProxySecret`). Si no se configura ningún
secreto, la validación se omite (útil en local/desarrollo).

Rate limiting propio vía `AspNetCoreRateLimit` (ver sección `IpRateLimiting` en
`appsettings.json`) — independiente del rate limiting que gestione RapidAPI.

## Build de Docker (Apify Actor)

```bash
docker build -f .actor/Dockerfile -t referee-stats-actor .
docker run --rm \
  -e APIFY_TOKEN=xxx \
  -e APIFY_DEFAULT_KEY_VALUE_STORE_ID=xxx \
  -e APIFY_DEFAULT_DATASET_ID=xxx \
  referee-stats-actor
```

## Despliegue a Apify

```bash
apify login
apify push
```

`apify push` usa `.actor/actor.json` (manifest) y `.actor/Dockerfile`. El input del Actor
se define en `.actor/input_schema.json` y aparece como formulario en Apify Console.

### Integración con la Apify API (sin SDK oficial de .NET)

`RefereeStats.ApifyActor/ApifyClient.cs` es un wrapper fino sobre HTTP puro:

- **Leer input**: `GET /v2/key-value-stores/{storeId}/records/INPUT?token={APIFY_TOKEN}`
- **Escribir resultados**: `POST /v2/datasets/{datasetId}/items?token={APIFY_TOKEN}`
- **Guardar metadata**: `PUT /v2/key-value-stores/{storeId}/records/{key}?token={APIFY_TOKEN}`

Variables de entorno inyectadas automáticamente en runtime por Apify:
`APIFY_TOKEN`, `APIFY_DEFAULT_KEY_VALUE_STORE_ID`, `APIFY_DEFAULT_DATASET_ID`.

## Exports (CSV / Parquet / JSONL)

Tras cada run, `RefereeStats.Core.Export.ExportService` genera:

- `/app/exports/referees-{timestamp}.csv`
- `/app/exports/referees-{timestamp}.parquet`
- `/app/exports/referees-{timestamp}.jsonl`

Apify empaqueta automáticamente el contenido de `/app/exports` como artefactos
descargables del run.

### Distribución semanal (Kaggle / HuggingFace / GitHub Releases)

`.github/workflows/publish-datasets.yml` corre cada domingo y publica los últimos
exports en 3 canales gratuitos (SEO / lead-gen hacia Apify Store y RapidAPI). Requiere
estos secrets en el repo de GitHub:

| Secret | Uso |
|---|---|
| `APIFY_TOKEN` | Leer metadata de exports desde Apify key-value store |
| `APIFY_KV_STORE_ID` | Store donde se guardó `latest_exports` |
| `KAGGLE_USERNAME`, `KAGGLE_KEY` | Publicar dataset en Kaggle |
| `KAGGLE_DATASET_ID` | `usuario/nombre-dataset` en Kaggle |
| `HUGGINGFACE_TOKEN` | Publicar dataset en HuggingFace |
| `HF_DATASET_REPO` | `usuario/nombre-dataset` en HuggingFace |
| `GITHUB_TOKEN` | Provisto automáticamente por Actions — crea el Release |

## Desplegar la WebApi (Azure App Service)

```bash
az webapp up \
  --name referee-stats-api \
  --resource-group <rg> \
  --runtime "DOTNETCORE:8.0" \
  --sku B1
```

Configura `RapidApi__ProxySecret` como App Setting en Azure con el secreto que
RapidAPI usa para tu API en su panel de configuración de backend.

## Categorización de errores

`RefereeStats.Core.Scraping.ErrorCategory`: `Blocked`, `RateLimited`, `NetworkError`,
`ParseError`, `SilentFailure`. El último cubre respuestas HTTP 200 que en realidad son
páginas de verificación/CAPTCHA — detectado comprobando ausencia de selectores clave
esperados en el HTML (ver `TransfermarktClient.DetectSilentFailure`).

## Scraping incremental

`GetRefereeProfilesAsync(league, season, since)` acepta un parámetro `since` opcional
para limitar el trabajo a partidos nuevos en el modo `matches` (Nivel 2), evitando
recalcular todo el histórico en cada ejecución.

## Ligas soportadas (v0.1)

- LaLiga 2 (España)
- Premier League (Inglaterra)
- Primera División (Argentina)

No añadir más ligas hasta validar estas tres en producción.

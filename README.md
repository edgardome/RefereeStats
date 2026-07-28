# Referee Stats Scraper

Football referee statistics scraper (source: Transfermarkt), .NET 8, packaged as:

1. **Apify Actor** — Docker container that reads JSON input and writes to Apify dataset.
2. **REST API** (`RefereeStats.WebApi`) — same data via HTTP, intended for RapidAPI Hub.

Both hosts reuse `RefereeStats.Core` (scraping, parsing, export, models).

## Structure

```
src/RefereeStats.Core/         Shared library: scraping, parsing, export, models
src/RefereeStats.ApifyActor/   Host 1 — console app executed inside Apify Actor
src/RefereeStats.WebApi/       Host 2 — REST API for RapidAPI
tests/RefereeStats.Core.Tests/ xUnit — parsing with local HTML fixtures, export tests
.actor/                        Actor manifest, input schema, and Dockerfile
```

## Local Build

```bash
dotnet restore RefereeStats.slnx
dotnet build RefereeStats.slnx
dotnet test RefereeStats.slnx
```

## Run WebApi Locally

```bash
dotnet run --project src/RefereeStats.WebApi
```

Swagger UI at `https://localhost:<port>/swagger` (Development environment only).

Endpoints:

- `GET /api/v1/referees/{refereeId}/profile?league={league}&season={season}` — Level 1 (Profiles)
- `GET /api/v1/referees/{refereeId}/matches?league={league}&season={season}&since={date}` — Level 2 (Matches)
- `GET /api/v1/referees/{refereeId}/analytics` — Level 3 (Advanced stats & Win ratios)
- `GET /api/v1/leagues/{league}/referees?season={season}` — Referee List

**Premium Value-Add Endpoints:**
- `GET /api/v1/analytics/leagues/{league}/leaderboard?season={season}&sortBy={strictness|matches}` — Ranks referees in a league by strictness or matches.
- `GET /api/v1/analytics/matchup-preview?refereeId={id}&homeTeam={home}&awayTeam={away}&league={league}&season={season}` — Generates an insight on which team is historically favored by this referee.
- `GET /api/v1/analytics/compare?refereeAId={idA}&refereeBId={idB}&league={league}&season={season}` — Head-to-head referee comparison.

### Authentication & Security

The WebApi uses a dual authentication system configured via ASP.NET Core:

1. **RapidAPI Proxy Secret (B2B):** Verified via the `RapidApiAuthHandler`. It checks for the `X-RapidAPI-Proxy-Secret` header against the `RapidApi:ProxySecret` config variable.
2. **Dashboard JWT (B2C):** Verified via standard JWT Bearer tokens. Used to log in securely to the Dashboard frontend.

All API routes require authentication unless marked with `[AllowAnonymous]` (e.g., `/api/v1/auth/login` and `/health`).

Rate limiting is handled via `AspNetCoreRateLimit` (independent of RapidAPI).

## Docker Build (Apify Actor)

```bash
docker build -f .actor/Dockerfile -t referee-stats-actor .
docker run --rm \
  -e APIFY_TOKEN=xxx \
  -e APIFY_DEFAULT_KEY_VALUE_STORE_ID=xxx \
  -e APIFY_DEFAULT_DATASET_ID=xxx \
  referee-stats-actor
```

## Deploy to Apify

```bash
apify login
apify push
```

`apify push` uses `.actor/actor.json` (manifest) and `.actor/Dockerfile`. Actor input
is defined in `.actor/input_schema.json` and appears as a form in Apify Console.

### Apify API Integration (No Official .NET SDK)

`RefereeStats.ApifyActor/ApifyClient.cs` is a thin HTTP wrapper:

- **Read input**: `GET /v2/key-value-stores/{storeId}/records/INPUT?token={APIFY_TOKEN}`
- **Write results**: `POST /v2/datasets/{datasetId}/items?token={APIFY_TOKEN}`
- **Save metadata**: `PUT /v2/key-value-stores/{storeId}/records/{key}?token={APIFY_TOKEN}`

Environment variables auto-injected by Apify at runtime:
`APIFY_TOKEN`, `APIFY_DEFAULT_KEY_VALUE_STORE_ID`, `APIFY_DEFAULT_DATASET_ID`.

## Exports (CSV / Parquet / JSONL)

After each run, `RefereeStats.Core.Export.ExportService` generates:

- `/app/exports/referees-{timestamp}.csv`
- `/app/exports/referees-{timestamp}.parquet`
- `/app/exports/referees-{timestamp}.jsonl`

Apify automatically packages `/app/exports` contents as downloadable run artifacts.

### Weekly Distribution (Kaggle / HuggingFace / GitHub Releases)

`.github/workflows/publish-datasets.yml` runs every Sunday and publishes latest exports
to 3 free channels (SEO / lead-gen toward Apify Store and RapidAPI). Requires these
secrets in your GitHub repo:

| Secret | Purpose |
|---|---|
| `APIFY_TOKEN` | Read export metadata from Apify key-value store |
| `APIFY_KV_STORE_ID` | Store where `latest_exports` was saved |
| `KAGGLE_USERNAME`, `KAGGLE_KEY` | Publish dataset to Kaggle |
| `KAGGLE_DATASET_ID` | `username/dataset-name` on Kaggle |
| `HUGGINGFACE_TOKEN` | Publish dataset to HuggingFace |
| `HF_DATASET_REPO` | `username/dataset-name` on HuggingFace |
| `GITHUB_TOKEN` | Auto-provided by Actions — creates Release |

## Deploy WebApi (Railway)

The API is packaged with a `Dockerfile` and `railway.toml` for easy deploy on Railway:

1. Connect your repository to Railway.
2. Add a persistent **Volume** and mount it under `/app/data/cache`.
3. Set the environment variables:
   - `ASPNETCORE_ENVIRONMENT`: `Production`
   - `RapidApi:ProxySecret`: Your RapidAPI secret key (for proxy protection)
   - `Jwt:Key`: A strong security key for generating dashboard session tokens.
   - `DefaultAdminPassword`: Set a master password (defaults to `admin123_ChangeMe!`).

## Error Categorization

`RefereeStats.Core.Scraping.ErrorCategory`: `Blocked`, `RateLimited`, `NetworkError`,
`ParseError`, `SilentFailure`. The last covers HTTP 200 responses that are actually
verification/CAPTCHA pages — detected by checking for missing expected selectors in
HTML (see `TransfermarktClient.DetectSilentFailure`).

## Incremental Scraping

`GetRefereeProfilesAsync(league, season, since)` accepts optional `since` parameter
to limit work to new matches in `matches` mode (Level 2), avoiding recalculating
entire history on each run.

## Supported Leagues (v0.1)

- LaLiga 2 (Spain)
- Premier League (England)
- Primera División (Argentina)

Do not add more leagues until validating these three in production.

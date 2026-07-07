# CLAUDE.md

Guidance for AI assistants (and humans) working in this repository.

## What this is

**AiCallAssistent** (product name **VoxFlow**) is an AI phone/WhatsApp receptionist
for Dutch small businesses. An inbound call to a company's Twilio number is answered
by an AI assistant that transcribes the caller in real time, reasons with an LLM, and
speaks back — all while streaming audio bidirectionally. The bot can book/cancel/reschedule
appointments, route to departments, transfer to a human, take callback requests, answer
questions from a company knowledge base, and send WhatsApp/email confirmations. A dashboard
API (also in this solution) lets companies configure the assistant, view call history, manage
appointments, billing, and integrations.

The default UI language and all caller-facing copy is **Dutch (nl)**.

## Tech stack

- **.NET 10** (`net10.0`), C# with nullable reference types and implicit usings enabled.
- **ASP.NET Core Web API** — controllers + a raw WebSocket endpoint for Twilio Media Streams.
- **Entity Framework Core 10** on **PostgreSQL** (Npgsql), hosted on **Supabase**.
- **Supabase Auth** (JWT / JWKS) for dashboard authentication.
- External services: **Twilio** (telephony + WhatsApp), **Deepgram** (speech-to-text),
  **ElevenLabs** (text-to-speech), **Google Vertex AI Gemini** (LLM + function calling),
  **Stripe** (billing), **Microsoft Outlook / Graph** (calendar sync), **Gmail SMTP** (email).
- Deployed to **Azure App Service** ("VoxFlow") via GitHub Actions on push to `Master`.

## Solution layout (Clean Architecture)

The solution file is `AiCallAssistent.slnx` (the newer XML `.slnx` format). Four projects,
dependencies point inward:

```
AiCallAssistent.Domain          ← EF entity models only, no dependencies
AiCallAssistent.Application     ← interfaces, DTOs, config POCOs, constants, helpers
AiCallAssistent.Infrastructure  ← service implementations, EF DbContext, external API clients
AiCallAssistent.WebAPI          ← controllers, WebSocket endpoint, Program.cs, DI wiring
```

- **Domain/Models/** — plain EF entities (`Company`, `CallSession`, `Appointment`,
  `AppointmentType`, `Employee`, `CompanyPackage`, `AssistantProfile`, `CallbackRequest`,
  `CompanyOpeningHour`, `EmailJob`, `AuditLog`, etc.). No behavior.
- **Application/** — the contract layer:
  - `Services/I*.cs` — service interfaces (implemented in Infrastructure).
  - `DTOs/` — request/response records. `CallPipelineDtos.cs` holds the core call records
    (`CompanyFeatures`, `CallDispatchContext`, `CompanyCallConfig`).
  - `Configuration/` — strongly-typed settings POCOs bound from `appsettings.json` sections
    (`GeminiSettings`, `DeepgramSettings`, `ElevenLabsSettings`, `TwilioSettings`,
    `SupabaseSettings`, `StripeSettings`, `GmailSettings`, `OutlookSettings`, `AdminSettings`,
    `AssistantSettings`).
  - `Constants/` — string enums as `static class` constants (`CallMode`, `AfterHoursMode`,
    `CallbackStatus`, `CallerClassification`, `CompanyBranch`). Prefer these over magic strings.
  - `Helpers/` — `NlTimeZone` (Europe/Amsterdam handling), `ConversationSeeder`.
- **Infrastructure/** — everything that touches the outside world:
  - `Data/AppDbContext.cs` — the EF `DbContext`, all `DbSet`s, indexes, and constraints.
  - `Services/` — implementations. Split roughly into the **call pipeline** (streaming),
    the **dashboard services** (appointments, departments, opening hours, packages, callbacks),
    and **background/hosted services**.
  - `Services/Email/` — SMTP sending, templating, and the background email job queue.
  - `DependencyInjection.cs` — extension methods that register everything (see below).
- **WebAPI/** — HTTP surface:
  - `Controllers/` — ~40 controllers, one per resource/feature area.
  - `WebSockets/TwilioStreamEndpoint.cs` — the raw WebSocket handler for `/ws/twilio`.
  - `Filters/ValidateTwilioRequestAttribute.cs` — verifies Twilio webhook signatures.
  - `Program.cs` — the composition root.

## Dependency injection

DI is organized as extension methods in `Infrastructure/DependencyInjection.cs`, called from
`Program.cs`. Understand which bucket a service belongs to before registering it:

- `AddSharedInfrastructure` — DbContext (via `AddDbContextFactory`, also registers scoped
  `AppDbContext`), and services used by both the call API and dashboard (packages, opening
  hours, departments, appointments, callbacks, WhatsApp, Outlook, the named `"Twilio"`
  HttpClient with Basic auth).
- `AddCallPipelineInfrastructure` — call-only services: Gemini, ElevenLabs, Deepgram, the
  function dispatcher, call setup, and the streaming interfaces
  (`ISttStreamingService`, `ITtsStreamingService`, `ILlmStreamingService`). Note the
  **STT provider is chosen at startup** from `Deepgram:SttProvider` (`"flux"` →
  `DeepgramStreamingService`, `"nova3"` → `Nova3StreamingService`, `"scribe"` →
  `ElevenLabsScribeStreamingService`, which reuses the `ElevenLabs` API key).
- `AddBackgroundServices` — hosted services: `AppointmentNotificationService` (reminders),
  `DataRetentionService` (GDPR purge).
- `AddEmailInfrastructure` — Gmail SMTP sender, template service, and the
  `EmailJobBackgroundService` queue worker.

`InMemoryConversationStore` and `InMemoryAudioStore` are **singletons** — per-call state lives
in process memory, so the app is **not horizontally scalable without changes** (see Gotchas).

## The call pipeline (the heart of the app)

This is the most intricate part of the codebase. Trace it in this order:

1. **`TwilioController.Answer()`** (`POST /api/twilio/answer`) — Twilio's voice webhook.
   Loads company config via `ICallSetupService.LoadAsync(calledNumber, callerNumber)`,
   enforces subscription lockout / blacklist, seeds the conversation, writes a `CallSession`
   row, and returns TwiML. Depending on config it either dials a human first (backup mode /
   after-hours "try human") or returns `<Connect><Stream>` TwiML pointing at the WebSocket.
2. **`TwilioStreamEndpoint.HandleAsync`** (`/ws/twilio`) — accepts the WebSocket, reads
   Twilio's `start` frame (carries `callSid` + custom parameters), **verifies the `callSid`
   was registered by `Answer()`** (security check against the DB), reloads setup, then
   constructs and runs a **`CallStreamHandler`**.
3. **`CallStreamHandler`** — the per-call orchestrator (instantiated manually, **not** in DI).
   Runs three concurrent loops over `System.Threading.Channels`:
   - **Receive** Twilio media frames → decode base64 mulaw → push to an audio channel.
   - **Forward** audio to the STT service; monitor Deepgram "speech started" events for
     **barge-in** (caller interrupts the bot).
   - **Process** finalized transcripts → `ILlmStreamingService.RunConversationStreamingAsync`
     (Gemini SSE) → split into sentences → `ITtsStreamingService` (ElevenLabs) → stream
     mulaw back to Twilio in 160-byte (20 ms) sub-chunks.
   Barge-in, playback-timing math (8 kHz mulaw = 8 bytes/ms), and turn state are managed with
   `volatile` flags and linked `CancellationTokenSource`s. Read the extensive inline comments
   before touching this file — the ordering of `_isBotSpeaking` / `_suppressBargeIn` /
   `_playbackCts` is load-bearing.
4. **Transfers** — when the LLM decides to transfer, the handler stores the target in
   `IMemoryCache` keyed `transfer_{callSid}` and closes the WebSocket. Twilio then calls the
   `<Connect action=...>` URL → `TwilioController.Transfer()` reads the cache and returns
   `<Dial>` TwiML.
5. **`TwilioController.Status()`** (`POST /api/twilio/status`) — Twilio's call-status
   callback. On `completed`, it summarizes and classifies the call via Gemini, builds the
   transcript, determines the `CallType`, persists everything to `CallSession`, and fires
   post-call side effects (knowledge suggestions, owner notification emails) on background
   `Task.Run`s using freshly-scoped DbContexts.

**LLM function calling** lives in `GeminiFunctionDispatcher`. It maps Gemini function-call
names (`create_appointment`, `check_availability`, `transfer_to_department`, `schedule_callback`,
`get_opening_hours`, …) to handlers. Branch-specific tools are added via the `BranchToolAdders`
dictionary (e.g. `makelaar` / real-estate gets `get_property_info`) — extend that dictionary to
add a branch tool without touching dispatch logic.

WhatsApp reuses the same LLM + dispatcher path via `TwilioController.WhatsAppIncoming()` and
`RunConversationAsync` (non-streaming), keyed by `whatsapp:{number}:{companyId}`.

## Configuration & secrets

- Config sections live in `appsettings.json`; secrets (connection string, API keys) are
  **empty in the repo** and injected via environment / Azure App Service settings / user
  secrets (`UserSecretsId` is set in `WebAPI.csproj`). **Never commit real secrets.**
- The Postgres connection string is appended with `;No Reset On Close=true;Max Auto Prepare=0`
  in code — **required** for Supabase's Supavisor transaction pooler. Don't remove it.
- Key sections: `Gemini` (model, Vertex location, project), `ElevenLabs` (voice, model,
  language), `Deepgram` (model, `SttProvider`), `Twilio` (`BaseUrl` must be the public HTTPS
  host — it's rewritten to `wss://` for the stream and used to build webhook callback URLs),
  `Supabase` (`Authority`/`ProjectUrl` for JWT validation), `Cors:AllowedOrigins`, `Stripe`,
  `Gmail`, `Outlook`, `Admin`, `Assistant` (`DefaultCompanyId`, welcome message template).
- **`Deepgram:SttProvider`** selects the streaming STT implementation at startup:
  `"flux"` (default) or `"nova3"` (both Deepgram, EU endpoint) or `"scribe"` (ElevenLabs
  Scribe v2 Realtime — reuses `ElevenLabs:ApiKey`). Scribe's own knobs (`ScribeBaseUrl`,
  `ScribeModel`, `ScribeLanguage`) live on the `ElevenLabs` section; `ScribeLanguage` empty =
  multilingual auto-detect. Scribe forwards Twilio μ-law 8 kHz frames untouched (no transcode).
- **Azure App Service application settings override `appsettings.json` at runtime** (env-var
  form `Section__Key`, e.g. `Deepgram__SttProvider`). If a key is *not* set in Azure, the
  `appsettings.json` value applies. Editing `appsettings.json` only takes effect once
  **deployed** (push to `Master`); an Azure app setting changes behaviour on restart without a
  redeploy and never modifies the committed file.

## Database & migrations

- **There are no EF Core migrations.** The schema is managed **manually via SQL** run in the
  Supabase SQL editor. See `migrations/AddProfilesAndCallFeatures.sql` for the pattern
  (`ALTER TABLE ... ADD COLUMN IF NOT EXISTS`). When you add or change an entity in
  `Domain/Models` and register it in `AppDbContext`, you must also **write the corresponding
  idempotent SQL migration** in `migrations/` — do not assume `dotnet ef` will apply anything.
- Table/column names are `snake_case` in Postgres; entities use PascalCase mapped by
  conventions/attributes. Indexes and unique constraints are declared in
  `AppDbContext.OnModelCreating` (e.g. unique `(EmployeeId, StartTime)` prevents double-booking;
  partial unique index for one active `AssistantProfile` per company).

## Build, run, test

```bash
# Restore & build the whole solution
dotnet build AiCallAssistent.slnx

# Run the API locally (Swagger UI at /swagger in Development)
dotnet run --project AiCallAssistent.WebAPI

# Publish (what CI does)
dotnet publish AiCallAssistent.WebAPI/AiCallAssistent.WebAPI.csproj -c Release -o out
```

- **There is no test project** in this solution — do not claim tests pass; there are none to run.
  Verify changes by building and, where possible, exercising the affected endpoint/flow.
- `AiCallAssistent.WebAPI.http` contains sample requests for manual testing.
- In Development, HTTPS redirect is disabled and CORS allows any localhost origin.

## Branches, hosting & deployment

- **`Master`** is the production deploy branch. CI (`.github/workflows/master_voxflow.yml`)
  builds and deploys to the **Azure App Service "VoxFlow"** (West Europe, Linux) on every
  push. Don't push feature work directly to it.
- **`Develop`** is kept identical to `Master` (it previously carried an *unrelated* history and
  was reset to match Master). There is **no separate dev deployment wired up** — only the
  Master workflow exists. If someone has an old local `Develop`, they must
  `git fetch && git reset --hard origin/Develop` (the history was rewritten).
- Prod runs on a **single Basic B1 instance**. Two consequences: a deploy restarts the app and
  **drops any in-progress call** (prefer deploying off-hours), and there's no autoscale/second
  instance. Because the app isn't horizontally scalable (in-memory stores, see Gotchas), growth
  is **vertical** — a larger instance, or Standard for autoscale + deployment slots. Call volume
  is light: packages cap usage at 500 / 1000 call-minutes per company per month.
- **Adding a dev/staging environment (cheapest):** create a second Web App assigned to the
  *existing* B1 App Service plan — multiple apps share one plan at no extra compute cost. It
  needs its own Azure settings: a separate `Twilio:BaseUrl` (its own host), a separate Twilio
  number pointed at its `/api/twilio/answer`, and ideally a **separate Supabase project** so
  test calls don't write to prod data. Move dev onto its own plan once prod serves real
  customers (so a test call can't starve prod's shared vCPU). Deployment slots need Standard+.

## Conventions & patterns

- **Clean Architecture boundaries are real.** Domain depends on nothing; Application defines
  interfaces; Infrastructure implements them; WebAPI wires them. Put an interface in
  Application and its implementation in Infrastructure — controllers depend on the interface.
- **Records for DTOs**, positional or `with`-expression style. Config POCOs are plain classes
  bound via `IOptions<T>`.
- **`static class` constants instead of enums** for values persisted to the DB (see
  `Constants/`). Reference them rather than hardcoding strings like `"first_line"` or `"A"`.
- **Resilience in the call path**: external calls and DB writes on the hot path are wrapped in
  try/catch and log-and-continue rather than throw — a failed side effect must not drop a live
  call. Follow this pattern in call-flow code.
- **Twilio webhooks return HTTP 200 even on internal failure** (Twilio retries non-2xx). TwiML
  is returned as `application/xml`. All caller-supplied text going into TwiML is XML-escaped.
- **STT provider comparison logging**: every streaming STT service funnels finalized transcripts
  through a `PublishTranscript` helper that emits a uniform, greppable
  `[STT] provider=… confidence=… chars=… transcript="…"` line, and `TwilioStreamEndpoint` logs a
  per-call `[STT] call summary provider=… avgConfidence=…`. Filter logs on `[STT]` to A/B
  providers on real calls; keep any new STT provider on this convention.
- **Background side effects** use `IServiceScopeFactory.CreateScope()` + a fresh
  `AppDbContext` (the request-scoped one is disposed). Never capture the scoped DbContext in a
  `Task.Run`.
- **New `[ApiController]`s** go under `Controllers/`, route `api/<resource>`, `[Authorize]` for
  dashboard endpoints (Supabase JWT). Twilio endpoints use `[ValidateTwilioRequest]` and
  `[Consumes("application/x-www-form-urlencoded")]` instead of auth.
- Comments in this codebase are explanatory and dense in the tricky areas (call handler,
  after-hours flows) — match that density there, stay terse elsewhere.

## Gotchas

- **Not horizontally scalable as-is.** `InMemoryConversationStore`, `InMemoryAudioStore`, and
  the `transfer_{callSid}` `IMemoryCache` entries are per-process. Two instances behind a load
  balancer will lose call state. The `Status()` handler already hedges by falling back to DB
  evidence when in-memory outcome is missing — keep that in mind when changing call state.
- **`Twilio:BaseUrl` is authoritative** for every callback and the WebSocket URL. If it's wrong
  or not the public host, calls silently break. It's also rewritten `https→wss`.
- **STT provider selection is startup-time**, from `Deepgram:SttProvider`. Switching providers
  requires a restart, not just config reload.
- **The deploy branch is `Master`** (capitalized) — CI deploys to Azure on push; `Develop`
  mirrors it but has no deploy wired, and the single B1 instance means a deploy drops
  in-progress calls. See "Branches, hosting & deployment". Do not push feature work to `Master`.
- Schema changes need a hand-written SQL migration (see Database section) — EF won't generate one.

## Related docs

- `EMAIL_TEMPLATES.md` — catalogue of transactional email templates and their triggers.

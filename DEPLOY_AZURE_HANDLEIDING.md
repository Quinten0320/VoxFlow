# VoxFlow — Live zetten op Azure (stap-voor-stap handleiding)

Deze handleiding is geschreven voor iemand **zonder technische kennis**. Volg de stappen
van boven naar beneden. Je hoeft niets te programmeren — je maakt alleen accounts aan,
kopieert sleutels ("keys") en plakt ze op de juiste plek in Azure.

> **Wat gaan we doen?**
> 1. Bij elke dienst (Twilio, ElevenLabs, enz.) de juiste sleutel ophalen.
> 2. De backend (deze code) op Azure zetten.
> 3. Alle sleutels in Azure invullen als *environment variables*.
> 4. De server in **dev-modus** zetten.
> 5. De frontend (gemaakt in Lovable) laten praten met de backend.

Reken op **1 tot 2 uur** de eerste keer.

---

## Belangrijk om te weten vooraf

- **De backend draait op Azure App Service** onder de naam **VoxFlow**.
- **Deployen (uitrollen) gebeurt automatisch**: elke keer dat er nieuwe code in de
  `Master`-branch op GitHub komt, bouwt en publiceert GitHub het vanzelf naar Azure.
  Jij hoeft dus geen code te uploaden.
- **Sleutels staan NIET in de code.** Ze worden apart ingevuld in Azure. Dat is veiliger.
  Jij vult ze straks in bij *Environment variables* (stap 3).
- Een sleutel wijzigen in Azure = na een **herstart** actief. Geen nieuwe deployment nodig.

---

# DEEL 1 — Sleutels ophalen bij elke dienst

Voor elke dienst hieronder staat: **waar je moet zijn**, **wat je moet aanvinken/kiezen**,
en **welke waarde je moet kopiëren**. Bewaar alles alvast in een kladblok/Word-document —
in Deel 3 plak je ze in Azure.

> Tip: maak een leeg document met kopjes en plak elke sleutel eronder zodra je hem hebt.

---

## 1.1 Supabase — database + inloggen (auth)

Supabase is de database én zorgt voor het inloggen van klanten in het dashboard.

**Wat doen:**
1. Ga naar <https://supabase.com> → log in → open zijn project.
2. Klik linksonder op **Project Settings** (tandwiel).
3. Ga naar **Data API** (of **API**). Kopieer de **Project URL**
   (ziet eruit als `https://xxxx.supabase.co`).
   → Bewaar als **`Supabase__ProjectUrl`**.
4. Ga naar **Database** → **Connection string** → kies het tabblad **"Transaction pooler"**
   (heel belangrijk, NIET "Direct connection").
   - Vink/klik het formaat **`.NET`** aan als dat er staat, anders **URI**.
   - Kopieer de string. Vervang `[YOUR-PASSWORD]` door het database-wachtwoord.
     (Weet hij het wachtwoord niet meer? Op dezelfde pagina staat **Reset database password**.)
   → Bewaar als **`ConnectionStrings__DefaultConnection`**.

> Let op: de code voegt zelf automatisch de juiste extra instellingen toe voor Supabase.
> Jij hoeft de connection string alleen maar te plakken zoals Supabase hem geeft.

**Database-tabellen aanmaken:** de tabellen worden met de hand aangemaakt via SQL.
In de map `migrations/` in deze code staan `.sql`-bestanden. Die moet hij één keer
uitvoeren in Supabase onder **SQL Editor** → nieuw script → plakken → **Run**.
(Als de database al bestond en al gevuld is, kan dit overgeslagen worden.)

---

## 1.2 Google Cloud (Gemini AI) — het "brein" van de assistent

De assistent denkt na met Google Gemini via Vertex AI. Dit is de lastigste stap.

**Wat doen:**
1. Ga naar <https://console.cloud.google.com> → log in → kies (of maak) een **project**.
   - Bovenin staat de projectnaam; klik erop → kopieer de **Project ID**
     (kleine letters, bijv. `voxflow-123456`).
     → Bewaar als **`Gemini__ProjectId`**.
2. Zoek bovenin naar **"Vertex AI API"** → open → klik **Enable** (inschakelen).
3. Maak een sleutelbestand ("service account"):
   - Ga naar **IAM & Admin** → **Service Accounts** → **Create service account**.
   - Naam: bijv. `voxflow-vertex`. Klik **Create and continue**.
   - Bij **rol (role)** kies: **Vertex AI User**. Klik **Continue** → **Done**.
   - Klik op de zojuist gemaakte service account → tabblad **Keys** →
     **Add key** → **Create new key** → kies **JSON** → **Create**.
   - Er wordt een **`.json`-bestand** gedownload. Open het met Kladblok.
   - Kopieer **de volledige inhoud** (alles, van `{` tot `}`).
     → Bewaar als **`Gemini__ServiceAccountKeyJson`**.

> Deze JSON is één lange tekst met veel regels. Plak hem straks in Azure precies zoals hij is.

---

## 1.3 Deepgram — spraak omzetten naar tekst (STT)

**Wat doen:**
1. Ga naar <https://console.deepgram.com> → log in.
2. Klik links op **API Keys** → **Create a New API Key**.
   - Naam: `voxflow`. Rechten (permissions): laat op **Member** / standaard staan.
   - Klik **Create Key** → kopieer de sleutel **meteen** (hij wordt maar één keer getoond).
     → Bewaar als **`Deepgram__ApiKey`**.

> De rest (model, EU-server) staat al goed in de code. Niets aanvinken nodig.

---

## 1.4 ElevenLabs — tekst omzetten naar spraak (de stem)

**Wat doen:**
1. Ga naar <https://elevenlabs.io> → log in.
2. Klik rechtsboven op zijn **profielicoon** → **API Keys** (of **Profile + API key**).
3. Klik **Create API Key** → naam `voxflow` → bij rechten mag alles standaard blijven
   (of vink minimaal **Text to Speech** aan) → **Create**.
   - Kopieer de sleutel. → Bewaar als **`ElevenLabs__ApiKey`**.
4. **Stem kiezen (optioneel):** de standaardstem staat al ingesteld in de code.
   Wil hij een andere stem? Ga naar **Voices**, kies een stem, klik de **"..."** →
   **Copy Voice ID**. → Bewaar als **`ElevenLabs__VoiceId`** (anders overslaan).

---

## 1.5 Twilio — telefonie + WhatsApp

Twilio zorgt voor de telefoonnummers en het bellen. Dit heeft meerdere sleutels.

**Wat doen:**
1. Ga naar <https://console.twilio.com> → log in.
2. Op het **hoofd-dashboard** (Account Info) staan meteen:
   - **Account SID** → Bewaar als **`Twilio__AccountSid`**.
   - **Auth Token** (klik **Show/Reveal**) → Bewaar als **`Twilio__AuthToken`**.
3. **API Key aanmaken** (aparte, veiligere sleutel):
   - Ga naar **Account** (rechtsboven) → **API keys & tokens** → **Create API key**.
   - Naam: `voxflow`. Type: kies **Standard**. Klik **Create**.
   - Kopieer nu direct **SID** en **Secret** (Secret zie je maar één keer!).
     → Bewaar SID als **`Twilio__ApiKeySid`** en Secret als **`Twilio__ApiKeySecret`**.
4. **Telefoonnummer koppelen** (heel belangrijk, doe dit ná Deel 2 als je de Azure-URL kent):
   - Ga naar **Phone Numbers** → **Manage** → **Active numbers** → klik zijn nummer.
   - Bij **Voice Configuration** → **A call comes in** → kies **Webhook** →
     vul in: `https://<JOUW-AZURE-ADRES>/api/twilio/answer` → methode **HTTP POST**.
   - Bij **Call status changes** (Status callback URL):
     `https://<JOUW-AZURE-ADRES>/api/twilio/status` → **HTTP POST**.
   - Klik **Save**.
   > `<JOUW-AZURE-ADRES>` is het webadres van de Azure-app uit Deel 2 (bijv.
   > `voxflow-xxxx.westeurope-01.azurewebsites.net`).
5. **WhatsApp (optioneel):** heeft hij een WhatsApp-nummer bij Twilio? Ga naar
   **Messaging** → **Senders** → **WhatsApp senders** en kopieer het nummer in de vorm
   `whatsapp:+31...`. → Bewaar als **`Twilio__WhatsAppFrom`** (anders leeg laten).
6. **SMS-verificatie (optioneel):** gebruikt hij Twilio Verify voor het bevestigen van
   telefoonnummers bij aanmelden? Ga naar **Verify** → **Services**, kopieer de **Service SID**
   (begint met `VS...`). → Bewaar als **`Twilio__VerifyServiceSid`** (anders leeg laten).

---

## 1.6 Stripe — betalingen/abonnementen

Alleen nodig als klanten via de site betalen. Anders kun je dit overslaan (leeg laten).

**Wat doen:**
1. Ga naar <https://dashboard.stripe.com> → log in.
2. **Belangrijk:** zet linksboven **"Test mode"** aan als hij eerst wil testen,
   of laat uit voor echt geld.
3. **Developers** → **API keys**:
   - Kopieer de **Secret key** (`sk_live_...` of `sk_test_...`).
     → Bewaar als **`Stripe__SecretKey`**.
4. **Webhook** (voor betaalmeldingen):
   - **Developers** → **Webhooks** → **Add endpoint**.
   - URL: `https://<JOUW-AZURE-ADRES>/api/stripe/webhook`
   - Kies events, of vink **"Select all events"** aan voor het gemak → **Add endpoint**.
   - Klik op de webhook → **Reveal** bij **Signing secret** (`whsec_...`).
     → Bewaar als **`Stripe__WebhookSecret`**.
5. **Prijs-ID's** (per abonnement): **Product catalog** → open elk product/abonnement →
   kopieer de **Price ID** (`price_...`) van de maand- en jaarvariant.
   → Bewaar als:
   - `Stripe__StartMonthlyPriceId`, `Stripe__StartYearlyPriceId`
   - `Stripe__BasisMonthlyPriceId`, `Stripe__BasisYearlyPriceId`
   - `Stripe__GroeiMonthlyPriceId`, `Stripe__GroeiYearlyPriceId`

---

## 1.7 Gmail — bevestigingsmails versturen

De assistent stuurt bevestigingen via een Gmail-account.

**Wat doen:**
1. Log in op het Google-account dat de mails verstuurt.
2. Ga naar <https://myaccount.google.com/security> → zet **2-stapsverificatie AAN**
   (verplicht om de volgende stap te kunnen doen).
3. Ga naar <https://myaccount.google.com/apppasswords>.
   - App-naam: `VoxFlow` → **Create**.
   - Google toont een wachtwoord van **16 letters** (met spaties). Kopieer die,
     **zonder de spaties**. → Bewaar als **`Gmail__AppPassword`**.
4. Het e-mailadres zelf → Bewaar als **`Gmail__SenderEmail`** (bijv. `info@voxflow.nl`).

---

## 1.8 Microsoft / Outlook agenda-koppeling (optioneel)

Alleen nodig als klanten hun Outlook-agenda willen koppelen. Anders overslaan.

**Wat doen:**
1. Ga naar <https://portal.azure.com> → zoek **App registrations** → **New registration**.
2. Naam: `VoxFlow Outlook`. Bij **Redirect URI** kies **Web** en vul in:
   `https://<JOUW-AZURE-ADRES>/api/integrations/outlook/callback` → **Register**.
3. Op de overzichtspagina: kopieer **Application (client) ID**.
   → Bewaar als **`Outlook__ClientId`**.
4. Ga naar **Certificates & secrets** → **New client secret** → **Add**.
   Kopieer de **Value** meteen (verdwijnt later!). → Bewaar als **`Outlook__ClientSecret`**.
5. Ga naar **API permissions** → **Add a permission** → **Microsoft Graph** →
   **Delegated permissions** → vink minimaal aan: **Calendars.ReadWrite**, **offline_access**,
   **User.Read** → **Add permissions**.

---

## 1.9 Admin-inlog (zelf kiezen)

De app heeft een aparte admin-login. Deze verzin je zelf.

- Kies een gebruikersnaam → **`Admin__Username`**.
- Kies een sterk wachtwoord → **`Admin__Password`**.

---

# DEEL 2 — Backend op Azure zetten

De app "VoxFlow" bestaat waarschijnlijk al in Azure (uit eerdere hosting). We controleren
hem, of maken hem opnieuw aan.

## 2.1 Bestaat de app al?

1. Ga naar <https://portal.azure.com> → zoek bovenin naar **App Services**.
2. Zie je **VoxFlow** in de lijst staan? → Ga door naar **Deel 3**.
3. Zie je hem NIET? → maak hem opnieuw aan met 2.2.

## 2.2 Nieuwe Web App aanmaken (alleen als hij niet bestaat)

1. **App Services** → **Create** → **Web App**.
2. Vul in:
   - **Subscription / Resource group**: kies bestaande of maak nieuwe (`voxflow-rg`).
   - **Name**: `voxflow` (het adres wordt dan `voxflow-....azurewebsites.net`).
   - **Publish**: **Code**.
   - **Runtime stack**: **.NET 10** (of de nieuwste .NET die er staat).
   - **Operating System**: **Linux**.
   - **Region**: **West Europe**.
   - **Pricing plan**: **Basic B1** (voldoende om te starten).
3. Klik **Review + create** → **Create**. Wacht tot het klaar is.

## 2.3 Automatisch deployen vanuit GitHub instellen

Zo wordt de code automatisch naar Azure gezet bij elke wijziging:

1. Open de Web App → menu links → **Deployment Center**.
2. **Source**: kies **GitHub** → log in bij GitHub → autoriseer.
3. Kies de **Organization**, **Repository** (`aicallassistent`) en **Branch: `Master`**.
4. Build provider: **GitHub Actions**. Klik **Save**.
   → Azure maakt nu vanzelf een deploy-bestand aan en de eerste build start.
   Elke nieuwe push naar `Master` wordt hierna automatisch uitgerold.

> Draait er al een GitHub Actions-deployment (zie je een groen vinkje bij de repo onder
> **Actions**)? Dan hoef je dit niet opnieuw te doen.

## 2.4 WebSockets AAN zetten (VERPLICHT!)

De telefoongesprekken werken via "web sockets". Staat dit uit, dan **verbreekt elk gesprek
meteen**.

1. Open de Web App → **Settings** → **Configuration** → tabblad **General settings**
   (bij nieuwere portal: **Settings** → **Configuration** → **Platform settings**).
2. Zoek **Web sockets** → zet op **On**.
3. Zet ook **Always on** → **On** (zodat de app niet in slaap valt en herinneringsmails
   blijven werken).
4. Klik **Save** bovenaan.

---

# DEEL 3 — Environment variables invullen in Azure

Dit is de belangrijkste lijst. Hier plak je alle sleutels uit Deel 1.

## 3.1 Waar vul ik ze in?

1. Open de Web App **VoxFlow** → menu links → **Settings** → **Environment variables**
   (heette vroeger *Configuration → Application settings*).
2. Onder **App settings** klik je telkens op **+ Add**.
3. Vul bij **Name** de naam in (bijv. `Deepgram__ApiKey`) en bij **Value** de sleutel.
   > Let op: het zijn **twee liggende streepjes** `__` (underscore), geen dubbele punt.
4. Herhaal voor elke regel hieronder.
5. **Helemaal onderaan klik je op `Apply` / `Save`** en bevestig de herstart.
   Sleutels werken pas na deze herstart.

## 3.2 De volledige lijst (kopieer de Name, vul jouw sleutel in bij Value)

### Server-modus (dev-modus aanzetten — zoals gevraagd)

| Name | Value |
|------|-------|
| `ASPNETCORE_ENVIRONMENT` | `Development` |

> Dit zet de server in **dev-modus**: Swagger-testpagina staat aan op `/swagger`,
> en de strenge HTTPS-omleiding staat uit. Wil je later "productie" (strenger/veiliger)?
> Verander deze waarde in `Production` en herstart.

### Database (Supabase)

| Name | Value |
|------|-------|
| `ConnectionStrings__DefaultConnection` | `YOUR_SUPABASE_CONNECTION_STRING` |

### Inloggen dashboard (Supabase Auth)

| Name | Value |
|------|-------|
| `Supabase__ProjectUrl` | `https://xxxx.supabase.co` |
| `Supabase__WebhookSecret` | `YOUR_WEBHOOK_SECRET` *(optioneel)* |

### AI-brein (Google Gemini / Vertex AI)

| Name | Value |
|------|-------|
| `Gemini__ProjectId` | `YOUR_GOOGLE_PROJECT_ID` |
| `Gemini__Location` | `europe-west4` |
| `Gemini__Model` | `gemini-2.5-flash` |
| `Gemini__ServiceAccountKeyJson` | `PLAK_HIER_DE_HELE_JSON` |

### Spraak → tekst (Deepgram)

| Name | Value |
|------|-------|
| `Deepgram__ApiKey` | `YOUR_DEEPGRAM_KEY` |
| `Deepgram__SttProvider` | `flux` |
| `Deepgram__Model` | `nova-3` |
| `Deepgram__BaseUrl` | `wss://api.eu.deepgram.com` |

### Tekst → spraak / de stem (ElevenLabs)

| Name | Value |
|------|-------|
| `ElevenLabs__ApiKey` | `YOUR_ELEVENLABS_KEY` |
| `ElevenLabs__VoiceId` | `yBtEjlHaWNu9xrYohjbA` |
| `ElevenLabs__Model` | `eleven_turbo_v2_5` |
| `ElevenLabs__Language` | `nl` |

### Telefonie (Twilio)

| Name | Value |
|------|-------|
| `Twilio__AccountSid` | `YOUR_TWILIO_ACCOUNT_SID` |
| `Twilio__AuthToken` | `YOUR_TWILIO_AUTH_TOKEN` |
| `Twilio__ApiKeySid` | `YOUR_TWILIO_API_KEY_SID` |
| `Twilio__ApiKeySecret` | `YOUR_TWILIO_API_KEY_SECRET` |
| `Twilio__BaseUrl` | `https://<JOUW-AZURE-ADRES>` |
| `Twilio__WhatsAppFrom` | `whatsapp:+31...` *(optioneel)* |
| `Twilio__VerifyServiceSid` | `VS...` *(optioneel)* |

> **`Twilio__BaseUrl` is cruciaal**: dit MOET exact het openbare Azure-adres zijn
> (met `https://`, zonder schuine streep aan het eind). Staat hier iets fout, dan
> werken de telefoontjes stilletjes niet.

### Assistent-instellingen

| Name | Value |
|------|-------|
| `Assistant__DefaultCompanyId` | `2` |
| `Assistant__WelcomeMessage` | `Bedankt voor uw oproep. U spreekt met de AI-assistent van {company}. Hoe kan ik u vandaag helpen?` *(optioneel)* |

### E-mail (Gmail)

| Name | Value |
|------|-------|
| `Gmail__SenderEmail` | `info@voxflow.nl` |
| `Gmail__SenderName` | `VoxFlow` |
| `Gmail__AppPassword` | `YOUR_16_LETTER_APP_PASSWORD` |

### Admin-login (zelf gekozen)

| Name | Value |
|------|-------|
| `Admin__Username` | `YOUR_ADMIN_USERNAME` |
| `Admin__Password` | `YOUR_STRONG_PASSWORD` |

### Betalingen (Stripe) — optioneel

| Name | Value |
|------|-------|
| `Stripe__SecretKey` | `sk_live_...` |
| `Stripe__WebhookSecret` | `whsec_...` |
| `Stripe__StartMonthlyPriceId` | `price_...` |
| `Stripe__StartYearlyPriceId` | `price_...` |
| `Stripe__BasisMonthlyPriceId` | `price_...` |
| `Stripe__BasisYearlyPriceId` | `price_...` |
| `Stripe__GroeiMonthlyPriceId` | `price_...` |
| `Stripe__GroeiYearlyPriceId` | `price_...` |

### Outlook-agenda — optioneel

| Name | Value |
|------|-------|
| `Outlook__ClientId` | `YOUR_OUTLOOK_CLIENT_ID` |
| `Outlook__ClientSecret` | `YOUR_OUTLOOK_CLIENT_SECRET` |
| `Outlook__RedirectUri` | `https://<JOUW-AZURE-ADRES>/api/integrations/outlook/callback` |
| `Outlook__DashboardUrl` | `https://<FRONTEND-ADRES>/dashboard/integraties` |

### Frontend toegang geven (CORS) — zie ook Deel 4

| Name | Value |
|------|-------|
| `Cors__AllowedOrigins__0` | `https://<FRONTEND-ADRES>` |
| `Cors__AllowedOrigins__1` | `https://www.<FRONTEND-ADRES>` |

> `__0`, `__1` enz. zijn genummerde regels — zo maak je een lijstje. Vul minstens het
> hoofd-adres van de Lovable-site in.

---

# DEEL 4 — Frontend (Lovable) koppelen aan de backend

De frontend is een aparte website (gemaakt in Lovable) die de backend aanroept.
Twee dingen moeten kloppen: (A) de frontend moet weten wáár de backend staat, en
(B) de backend moet de frontend toestaan (CORS).

## 4.1 In Lovable: het backend-adres instellen

1. Open het project in **Lovable**.
2. De frontend gebruikt een "API base URL" — het adres van de backend. Zoek in Lovable
   naar de instelling/omgeving variabele die hiervoor gebruikt wordt (vaak iets als
   `VITE_API_URL`, `VITE_API_BASE_URL` of `NEXT_PUBLIC_API_URL`).
   > Weet je de exacte naam niet? Vraag het in Lovable via de chat: *"Welke environment
   > variable gebruikt de app voor de backend-URL?"* — Lovable weet dat van zijn eigen project.
3. Zet de waarde op het **Azure-adres van de backend**, bijvoorbeeld:
   `https://voxflow-xxxx.westeurope-01.azurewebsites.net`
   (dus **niet** localhost).
4. Ook de **Supabase**-instellingen in de frontend moeten kloppen (Project URL + de
   **anon public key** uit Supabase → Project Settings → API). Die gebruikt de frontend
   om klanten te laten inloggen. Vul die in Lovable's Supabase-instelling in.
5. **Publiceer** de frontend in Lovable en noteer het openbare adres
   (bijv. `https://voxflow.nl` of een `*.lovable.app`-adres).

## 4.2 In Azure: de frontend toestaan (CORS)

De backend blokkeert standaard onbekende websites. Zet daarom het frontend-adres in de
CORS-lijst (deed je al bij Deel 3):

- `Cors__AllowedOrigins__0` = het exacte frontend-adres, mét `https://`, **zonder** schuine
  streep op het eind (bijv. `https://voxflow.nl`).
- Heeft de site ook een `www`-variant of een apart `*.lovable.app`-adres? Voeg die toe als
  `Cors__AllowedOrigins__1`, `Cors__AllowedOrigins__2`, enz.

> Belangrijk: het adres moet **exact** overeenkomen (hoofd/kleine letters, `https`,
> geen `/` aan het eind). Klopt het niet, dan geeft de site "CORS"-fouten en laden de
> gegevens niet.

Klik **Apply/Save** in Azure → app herstart → koppeling is actief.

---

# DEEL 5 — Controleren of alles werkt

Doe deze controles ná het invullen en herstarten:

1. **Backend leeft?** Open in de browser:
   `https://<JOUW-AZURE-ADRES>/swagger`
   → Zie je een testpagina met een lijst endpoints? Dan draait de backend (dev-modus werkt).
   *(Zie je hem niet, controleer dan `ASPNETCORE_ENVIRONMENT = Development` en herstart.)*
2. **Database verbonden?** Probeer in het dashboard (frontend) in te loggen of een lijst te
   openen. Geen fout = database + Supabase-auth werken.
3. **Frontend praat met backend?** Doe een actie in het dashboard die gegevens ophaalt.
   Werkt het niet? Open in de browser met **F12** het tabblad **Console** — staat er "CORS"?
   → frontend-adres nog toevoegen bij `Cors__AllowedOrigins`. Staat er "401"? → inlog/token.
4. **Bel-test:** bel het Twilio-nummer. De assistent hoort op te nemen en te praten.
   - Neemt hij niet op / valt meteen weg? → controleer:
     - **Web sockets = On** (Deel 2.4),
     - **`Twilio__BaseUrl`** exact het Azure-adres,
     - de Twilio-webhook `.../api/twilio/answer` (Deel 1.5 stap 4).

---

# Snelle checklist (afvinken)

**Sleutels ophalen:**
- [ ] Supabase: Project URL + connection string (Transaction pooler)
- [ ] Google Cloud: Project ID + Vertex AI ingeschakeld + service-account JSON
- [ ] Deepgram: API Key
- [ ] ElevenLabs: API Key (+ evt. Voice ID)
- [ ] Twilio: Account SID, Auth Token, API Key SID + Secret (+ evt. WhatsApp, Verify)
- [ ] Stripe: Secret Key, Webhook Secret, prijs-ID's *(optioneel)*
- [ ] Gmail: adres + app-wachtwoord (2FA aan)
- [ ] Outlook: Client ID + Secret + permissions *(optioneel)*
- [ ] Admin: zelf een gebruikersnaam + wachtwoord kiezen

**Azure:**
- [ ] Web App "VoxFlow" bestaat / aangemaakt (Linux, West Europe, .NET, B1)
- [ ] Deployment Center gekoppeld aan GitHub `Master`
- [ ] **Web sockets = On** en **Always on = On**
- [ ] Alle environment variables ingevuld (Deel 3)
- [ ] `ASPNETCORE_ENVIRONMENT = Development` (dev-modus)
- [ ] Op **Apply/Save** geklikt → app herstart

**Koppelingen:**
- [ ] Twilio-nummer wijst naar `.../api/twilio/answer` en `.../api/twilio/status`
- [ ] Stripe-webhook wijst naar `.../api/stripe/webhook` *(optioneel)*
- [ ] Frontend (Lovable) API-URL = Azure-adres
- [ ] Frontend-adres staat in `Cors__AllowedOrigins`

**Testen:**
- [ ] `/swagger` opent
- [ ] Dashboard-login werkt
- [ ] Bel-test: assistent neemt op en praat

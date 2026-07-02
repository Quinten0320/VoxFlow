namespace AiCallAssistent.Infrastructure.Services.Email;

public class EmailTemplateService
{
    // ── Shared layout ─────────────────────────────────────────────────────────

    private static string Layout(string preheader, string bodyHtml) => $$"""
        <!DOCTYPE html>
        <html lang="nl">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>VoxFlow</title>
          <style>
            body { margin:0; padding:0; background:#f8f7ff; font-family:'Helvetica Neue',Arial,sans-serif; }
            .wrap { max-width:600px; margin:32px auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 2px 12px rgba(0,0,0,0.06); }
            .header { background:#6B46C1; padding:28px 40px; }
            .header h1 { margin:0; color:#fff; font-size:22px; letter-spacing:-0.3px; }
            .body { padding:36px 40px; color:#1a1a2e; line-height:1.65; font-size:15px; }
            .body p { margin:0 0 14px; }
            .cta { display:inline-block; margin:8px 8px 8px 0; padding:13px 28px; background:#6B46C1; color:#fff !important; text-decoration:none; border-radius:8px; font-weight:600; font-size:15px; }
            .cta-sec { display:inline-block; margin:8px 8px 8px 0; padding:13px 28px; background:#f3f0ff; color:#6B46C1 !important; text-decoration:none; border-radius:8px; font-weight:600; font-size:15px; }
            .stats { background:#f8f7ff; border-radius:8px; padding:20px 24px; margin:16px 0; }
            .stats table { width:100%; border-collapse:collapse; }
            .stats td { padding:6px 0; font-size:14px; color:#444; }
            .stats td:last-child { text-align:right; font-weight:600; color:#1a1a2e; }
            .footer { padding:20px 40px; font-size:12px; color:#888; border-top:1px solid #f0eeff; }
            .footer a { color:#6B46C1; text-decoration:none; }
          </style>
        </head>
        <body>
          <!--[if mso]><div style="display:none"><!--<![endif]-->
          <span style="display:none;font-size:1px;color:#fff;max-height:0">{{preheader}}</span>
          <!--[if mso]></div><![endif]-->
          <div class="wrap">
            <div class="header"><h1>VoxFlow</h1></div>
            <div class="body">{{bodyHtml}}</div>
            <div class="footer">
              Je ontvangt deze e-mail omdat je een VoxFlow-account hebt. &nbsp;|&nbsp;
              <a href="https://voxflow.nl">voxflow.nl</a>
            </div>
          </div>
        </body>
        </html>
        """;

    private static string Cta(string label, string url) =>
        $"""<a href="{url}" class="cta">{label}</a>""";

    private static string CtaSec(string label, string url) =>
        $"""<a href="{url}" class="cta-sec">{label}</a>""";

    private static string DashboardUrl => "https://voxflow.nl/dashboard";

    // ── #1 · Welkomstmail ─────────────────────────────────────────────────────

    public (string Subject, string Html) Welcome(string naam) =>
        ($"Welkom bij VoxFlow, {naam}!", Layout(
            "Je account is aangemaakt. Start je onboarding.",
            $"""
            <p>Hoi {naam}, welkom!</p>
            <p>Je account is aangemaakt. Je bent één setup verwijderd van een assistent die nooit meer een gesprek laat liggen.</p>
            <p>Het duurt minder dan 10 minuten om live te gaan. Als je liever hulp hebt bij het onboarden, kun je een gratis adviesgesprek met ons inplannen.</p>
            {Cta("Start mijn onboarding", DashboardUrl + "/onboarding")}
            """));

    // ── #2 · Onboarding herinnering ───────────────────────────────────────────

    public (string Subject, string Html) OnboardingReminder(string naam) =>
        ("Je setup staat nog open", Layout(
            "Inkomende oproepen worden nog niet afgehandeld.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je bent gisteren gestart met de setup maar hebt hem nog niet afgerond. Zolang die open staat, worden inkomende oproepen nog niet door VoxFlow afgehandeld. Wil je het vandaag afronden?</p>
            {Cta("Rond mijn setup af", DashboardUrl + "/onboarding")}
            """));

    // ── #8 · Setup voltooid ───────────────────────────────────────────────────

    public (string Subject, string Html) SetupComplete(string naam) =>
        ("Je bent live, VoxFlow staat aan", Layout(
            "Elk inkomend gesprek wordt vanaf nu afgehandeld.",
            $"""
            <p>Hoi {naam}, je bent live!</p>
            <p>Vanaf nu neemt VoxFlow elk inkomend gesprek voor je op. Afspraken worden direct ingepland, vragen beantwoord en urgente zaken doorverbonden naar jou. Je hoeft er niets meer voor te doen.</p>
            <p>In je dashboard zie je alles wat er is afgehandeld, inclusief samenvattingen per gesprek.</p>
            {Cta("Ga naar mijn dashboard", DashboardUrl)}
            """));

    // ── #9 · Setup foutmelding ────────────────────────────────────────────────

    public (string Subject, string Html) SetupError(string naam, string foutOmschrijving) =>
        ("Er ging iets mis bij je setup", Layout(
            "Je assistent is nog niet actief.",
            $"""
            <p>Hoi {naam},</p>
            <p>Er is iets misgegaan tijdens het activeren van je assistent. Je bent er nog niet live mee, maar dat lossen we snel op.</p>
            <p>Foutmelding: {foutOmschrijving}</p>
            <p>Probeer het opnieuw via de knop hieronder. Lukt het dan nog steeds niet, neem dan contact met ons op, dan we helpen je direct verder.</p>
            {Cta("Probeer opnieuw", DashboardUrl + "/onboarding")}
            {CtaSec("Neem contact op", "mailto:support@voxflow.nl")}
            """));

    // ── #13b · Telefoonnummer nog niet gekoppeld ──────────────────────────────

    public (string Subject, string Html) PhoneNumberNotLinked(string naam) =>
        ("Nog één stap — koppel je telefoonnummer", Layout(
            "Bellers kunnen je assistent nog niet bereiken.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je assistent is bijna klaar, maar er is nog geen telefoonnummer aan gekoppeld. Zolang dat niet is ingesteld, kunnen bellers je assistent niet bereiken.</p>
            <p>Het koppelen duurt minder dan 2 minuten. Daarna staat alles klaar.</p>
            {Cta("Koppel mijn nummer", DashboardUrl + "/onboarding")}
            """));

    // ── #14 · Eerste testgesprek gelukt ──────────────────────────────────────

    public (string Subject, string Html) FirstCallSucceeded(string naam) =>
        ("Je eerste gesprek is afgehandeld", Layout(
            "Je assistent werkt — bekijk de samenvatting.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je assistent heeft zojuist zijn eerste gesprek afgehandeld. Het werkt.</p>
            <p>In je dashboard zie je een samenvatting van wat er is gezegd en wat er is afgesproken. Zo ziet het er elke keer uit; automatisch, zonder dat jij er iets voor hoeft te doen.</p>
            {Cta("Bekijk mijn eerste gesprek", DashboardUrl + "/gesprekken")}
            """));

    // ── #15 · Assistent instellingen niet compleet ────────────────────────────

    public (string Subject, string Html) AssistantSettingsIncomplete(string naam) =>
        ("Je assistent mist nog een paar instellingen", Layout(
            "Vul de ontbrekende instellingen in voor optimale werking.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je assistent staat aan, maar een aantal instellingen zijn nog niet ingevuld. Daardoor werkt hij nog niet optimaal. Denk aan je openingszin, je doorschakelregels of je spoedcriteria.</p>
            <p>Het invullen duurt een paar minuten en maakt een groot verschil in hoe je assistent overkomt bij bellers.</p>
            {Cta("Vul mijn instellingen aan", DashboardUrl + "/instellingen")}
            """));

    // ── #T1 · Proefperiode gestart ────────────────────────────────────────────

    public (string Subject, string Html) TrialStarted(string naam) =>
        ("Je proefperiode is gestart! De 14 dagen gratis start nu", Layout(
            "14 dagen gratis VoxFlow, geen verplichtingen.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je proefperiode is actief. De komende 14 dagen gebruik je VoxFlow gratis en zonder verplichtingen.</p>
            <p>Onze tip: ga zo snel mogelijk live en laat je assistent een echt gesprek afhandelen. Dat is het moment waarop je voelt wat het doet.</p>
            <p>Heb je vragen of wil je dat we even meekijken? Stuur gewoon een bericht, want we helpen je graag.</p>
            {Cta("Ga naar mijn dashboard", DashboardUrl)}
            """));

    // ── #T2 · Proefperiode loopt af over 3 dagen ─────────────────────────────

    public (string Subject, string Html) TrialExpiringSoon(string naam, string eindDatum) =>
        ("Je proefperiode loopt over 3 dagen af", Layout(
            "Na je proefperiode gaat je abonnement automatisch door.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je proefperiode loopt over 3 dagen af. Daarna gaat je abonnement automatisch door. Je hoeft niets te doen om VoxFlow te blijven gebruiken.</p>
            <p>Wil je toch stoppen? Dat kan via je accountinstellingen voor {eindDatum}. Heb je vragen? Stuur ons gerust een bericht.</p>
            {Cta("Beheer mijn abonnement", DashboardUrl + "/instellingen")}
            """));

    // ── #10 · Abonnement gestart ──────────────────────────────────────────────

    public (string Subject, string Html) SubscriptionStarted(string naam, string abonnement, string startdatum, string volgendeFactuur) =>
        ("Je abonnement is actief", Layout(
            $"VoxFlow {abonnement} is actief.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je abonnement is actief. Plan: <strong>{abonnement}</strong> | Startdatum: {startdatum} | Volgende factuur: {volgendeFactuur}.</p>
            <p>Vanaf nu staat VoxFlow elke dag voor je klaar. Mocht er ooit iets zijn, weet je ons te vinden.</p>
            {Cta("Ga naar mijn dashboard", DashboardUrl)}
            """));

    // ── #11 · Abonnement geüpgraded ───────────────────────────────────────────

    public (string Subject, string Html) SubscriptionUpgraded(string naam, string nieuwPlan, IEnumerable<string> nieuweFeatures) =>
        ($"Je gebruikt nu pakket {nieuwPlan}", Layout(
            "Upgrade geslaagd — nieuwe functies staan klaar.",
            $"""
            <p>Hoi {naam}, je upgrade is geslaagd.</p>
            <p>Je gebruikt nu het <strong>{nieuwPlan}</strong>-pakket. Je hebt nu toegang tot {string.Join(" en ", nieuweFeatures)}. Die staan direct klaar in je dashboard.</p>
            {Cta("Ontdek de nieuwe functies", DashboardUrl)}
            """));

    // ── #12 · Abonnement gedowngraded ─────────────────────────────────────────

    public (string Subject, string Html) SubscriptionDowngraded(string naam, string nieuwPlan, string ingangsDatum, IEnumerable<string> vervaldeFeatures) =>
        ($"Je plan is gewijzigd naar {nieuwPlan}", Layout(
            $"Pakketwijziging per {ingangsDatum}.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je abonnement is gewijzigd naar <strong>{nieuwPlan}</strong> per {ingangsDatum}. Vanaf dan zijn {string.Join(" en ", vervaldeFeatures)} niet meer beschikbaar.</p>
            <p>Wil je later toch weer upgraden? Dat kan altijd via je accountinstellingen.</p>
            {Cta("Beheer mijn abonnement", DashboardUrl + "/instellingen")}
            """));

    // ── #13 · Abonnement opgezegd ─────────────────────────────────────────────

    public (string Subject, string Html) SubscriptionCancelled(string naam, string eindDatum) =>
        ("Je abonnement is opgezegd", Layout(
            $"Toegang tot {eindDatum}.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je abonnement is opgezegd. Je hebt nog toegang tot {eindDatum}. Tot die tijd handelt VoxFlow gewoon je gesprekken af.</p>
            <p>Mocht je van gedachten veranderen of wil je iets bespreken, horen wij het graag.</p>
            {Cta("Abonnement heractiveren", DashboardUrl + "/instellingen")}
            """));

    // ── #F1 · Factuur beschikbaar ─────────────────────────────────────────────

    public (string Subject, string Html) InvoiceAvailable(string naam, string maand, string bedrag, string periodeVan, string periodeTot, string factuurUrl) =>
        ($"Je factuur van {maand} staat klaar", Layout(
            $"Factuur {maand} — €{bedrag}.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je factuur van {maand} is beschikbaar. Bedrag: €{bedrag} | Periode: {periodeVan} t/m {periodeTot}.</p>
            <p>De betaling wordt automatisch verwerkt via je betaalmethode. Je hoeft niets te doen.</p>
            {Cta("Bekijk mijn factuur", factuurUrl)}
            """));

    // ── #B1 · Betaling mislukt ────────────────────────────────────────────────

    public (string Subject, string Html) PaymentFailed(string naam, string bedrag) =>
        ("Je betaling is niet gelukt", Layout(
            "Controleer je betaalgegevens.",
            $"""
            <p>Hoi {naam},</p>
            <p>De automatische betaling van €{bedrag} is niet gelukt. Dat kan gebeuren, je account staat gewoon nog aan en je mist niets.</p>
            <p>Controleer even je betaalgegevens zodat de volgende poging wel lukt. Mocht er iets zijn, neem dan gerust contact op.</p>
            {Cta("Controleer mijn betaalgegevens", DashboardUrl + "/instellingen")}
            """));

    // ── #18 · Account tijdelijk beperkt ───────────────────────────────────────

    public (string Subject, string Html) AccountRestricted(string naam, string bedrag) =>
        ("Je account is tijdelijk beperkt", Layout(
            "Openstaande betaling — herstel je account.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je account is tijdelijk beperkt omdat een betaling van €{bedrag} nog openstaat. VoxFlow handelt op dit moment geen gesprekken af.</p>
            <p>Rond de betaling af om je account direct te herstellen.</p>
            {Cta("Betaal nu en herstel mijn account", DashboardUrl + "/instellingen")}
            """));

    // ── #W1 · Eerste week samenvatting ────────────────────────────────────────

    public (string Subject, string Html) FirstWeekSummary(string naam, int gesprekken, int afspraken, int doorverbonden) =>
        ("Je eerste week met VoxFlow", Layout(
            $"{gesprekken} gesprekken afgehandeld in je eerste week.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je bent een week live. Dit heeft VoxFlow voor je gedaan: <strong>{gesprekken}</strong> gesprekken afgehandeld, <strong>{afspraken}</strong> afspraken ingepland en <strong>{doorverbonden}</strong> keer doorverbonden naar jou.</p>
            <p>In je dashboard zie je een volledig overzicht per gesprek. Alles wat je hebt gemist zonder het te weten, VoxFlow heeft het opgevangen.</p>
            {Cta("Bekijk mijn weekoverzicht", DashboardUrl + "/gesprekken")}
            """));

    // ── #22 · Maandrapport ────────────────────────────────────────────────────

    public (string Subject, string Html) MonthlyReport(string naam, string maand, int totaalGesprekken, string besteDag, string groeiPercentage, int doorverbonden) =>
        ($"Jouw maand — {maand}", Layout(
            $"{totaalGesprekken} gesprekken in {maand}.",
            $"""
            <p>Hoi {naam}, hier is je maandoverzicht.</p>
            <p>Totaal gesprekken: <strong>{totaalGesprekken}</strong> | Drukste dag: <strong>{besteDag}</strong> | Groei t.o.v. vorige maand: <strong>{groeiPercentage}</strong> | Doorverbonden: <strong>{doorverbonden}</strong>.</p>
            <p>Een volledig overzicht staat klaar in je dashboard.</p>
            {Cta("Bekijk volledig rapport", DashboardUrl + "/gesprekken")}
            """));

    // ── #G1 · Gesprekslimiet bijna bereikt ────────────────────────────────────

    public (string Subject, string Html) CallLimitAlmostReached(string naam, int gebruikt, int limiet) =>
        ($"Je gebruikt {gebruikt * 100 / limiet}% van je gesprekslimiet", Layout(
            $"{gebruikt} van {limiet} gesprekken gebruikt.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je hebt deze maand al <strong>{gebruikt}</strong> van je <strong>{limiet}</strong> gesprekken gebruikt. Je zit op 80% van je limiet.</p>
            <p>Wat er gebeurt als je limiet bereikt is, hangt af van jouw instelling. Heb je gekozen voor automatisch doorgaan? Dan worden extra gesprekken buiten je bundel afgehandeld en gefactureerd. Heb je gekozen voor stoppen? Dan pauzeert VoxFlow totdat je volgende factuurperiode begint.</p>
            <p>Wil je je instelling aanpassen of upgraden naar een hoger plan? Dat kan via je accountinstellingen.</p>
            {Cta("Bekijk mijn instelling", DashboardUrl + "/instellingen")}
            {CtaSec("Upgrade mijn abonnement", DashboardUrl + "/instellingen?tab=abo")}
            """));

    // ── #G2 · Gesprekslimiet bereikt ─────────────────────────────────────────

    public (string Subject, string Html) CallLimitReached(string naam, int limiet, string volgendeFactuurDatum) =>
        ("Je gesprekslimiet is bereikt", Layout(
            $"Maandlimiet van {limiet} gesprekken bereikt.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je maandlimiet van <strong>{limiet}</strong> gesprekken is bereikt.</p>
            <p>Wat er nu gebeurt hangt af van jouw instelling. Heb je gekozen voor automatisch doorgaan? Dan handelt VoxFlow extra gesprekken buiten je bundel af — deze worden gefactureerd bij je volgende factuur. Heb je gekozen voor stoppen? Dan pauzeert VoxFlow totdat je volgende factuurperiode begint op {volgendeFactuurDatum}.</p>
            <p>Wil je je instelling aanpassen of upgraden? Dat kan direct via je accountinstellingen.</p>
            {Cta("Bekijk mijn instelling", DashboardUrl + "/instellingen")}
            {CtaSec("Upgrade mijn abonnement", DashboardUrl + "/instellingen?tab=abo")}
            """));

    // ── #23 · Inactief 7 dagen ────────────────────────────────────────────────

    public (string Subject, string Html) Inactive7Days(string naam) =>
        ("Er staan gesprekken op je te wachten", Layout(
            "7 dagen niet ingelogd — er wachten gesprekken op je.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je hebt de afgelopen 7 dagen niet ingelogd. VoxFlow heeft ondertussen gewoon doorgewerkt en er staan waarschijnlijk samenvattingen en gesprekken op je te wachten.</p>
            {Cta("Log in en bekijk mijn gesprekken", DashboardUrl + "/gesprekken")}
            """));

    // ── #25 · Inactief 30 dagen ───────────────────────────────────────────────

    public (string Subject, string Html) Inactive30Days(string naam, int aantalGesprekken) =>
        ("Nog steeds iets voor je?", Layout(
            "Al een maand niet ingelogd.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je hebt al een maand niet ingelogd. VoxFlow heeft in die tijd <strong>{aantalGesprekken}</strong> gesprekken voor je afgehandeld, maar we vragen ons af of alles nog goed gaat.</p>
            <p>Is er iets wat beter kan? Iets wat je mist? We horen het graag.</p>
            {Cta("Log opnieuw in", DashboardUrl)}
            {CtaSec("Neem contact op", "mailto:support@voxflow.nl")}
            """));

    // ── #43 · Win-back dag 1 ──────────────────────────────────────────────────

    public (string Subject, string Html) WinBack1(string naam, int aantalGesprekken) =>
        ("Je hebt opgezegd — we begrijpen het", Layout(
            "Altijd welkom terug.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je hebt je abonnement opgezegd. Dat respecteren we. In de periode dat je VoxFlow gebruikte, werden er <strong>{aantalGesprekken}</strong> gesprekken voor je afgehandeld. Gesprekken die je anders zelf had moeten opvangen of had gemist.</p>
            <p>Als er een reden is waarom het niet paste, horen we dat graag. Niet om je terug te winnen, maar omdat we er beter van willen worden.</p>
            {Cta("Deel mijn feedback", "mailto:support@voxflow.nl")}
            {CtaSec("Herstart mijn abonnement", DashboardUrl + "/instellingen")}
            """));

    // ── #4 · Wachtwoord gewijzigd ─────────────────────────────────────────────

    public (string Subject, string Html) PasswordResetConfirmed(string naam, string tijdstip) =>
        ("Je wachtwoord is gewijzigd", Layout(
            "Wachtwoord succesvol gewijzigd.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je wachtwoord is succesvol gewijzigd op {tijdstip}. Als jij dit was, hoef je niets te doen.</p>
            <p>Was jij dit niet? Beveilig dan direct je account via de knop hieronder.</p>
            {Cta("Beveilig mijn account", DashboardUrl + "/instellingen")}
            """));

    // ── #34 · Nieuwe inlog gedetecteerd ──────────────────────────────────────

    public (string Subject, string Html) NewLoginDetected(string naam, string apparaat, string locatie, string tijdstip) =>
        ("Nieuwe inlog op je account", Layout(
            "Nieuwe inlog gedetecteerd — was jij dit?",
            $"""
            <p>Hoi {naam},</p>
            <p>Er is ingelogd op je VoxFlow-account vanaf een nieuw apparaat of locatie. Apparaat: <strong>{apparaat}</strong> | Locatie: <strong>{locatie}</strong> | Tijdstip: <strong>{tijdstip}</strong>.</p>
            <p>Was jij dit? Dan hoef je niets te doen. Was jij dit niet, beveilig dan direct je account.</p>
            {Cta("Mijn account beveiligen", DashboardUrl + "/instellingen")}
            """));

    // ── #36 · Account vergrendeld ─────────────────────────────────────────────

    public (string Subject, string Html) AccountLocked(string naam, string tijdstip) =>
        ("Je account is tijdelijk vergrendeld", Layout(
            "Te veel mislukte inlogpogingen.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je account is tijdelijk vergrendeld na te veel mislukte inlogpogingen. Probeer over 15 minuten opnieuw, of reset direct je wachtwoord.</p>
            {Cta("Wachtwoord resetten", "https://voxflow.nl/login")}
            """));

    // ── #VJ · Verjaardag account ──────────────────────────────────────────────

    public (string Subject, string Html) AccountBirthday(string naam, int aantalGesprekken) =>
        ("Eén jaar VoxFlow! dank je wel", Layout(
            "Een jaar geleden gestart — bedankt.",
            $"""
            <p>Hoi {naam},</p>
            <p>Precies een jaar geleden ben je gestart met VoxFlow. In die tijd heeft je assistent <strong>{aantalGesprekken}</strong> gesprekken afgehandeld — gesprekken die jij anders zelf had moeten beantwoorden of had gemist.</p>
            <p>Bedankt dat je ons het vertrouwen hebt gegeven. Als kleine blijk van waardering: wist je dat je met een jaarabonnement twee maanden gratis krijgt ten opzichte van maandelijks betalen? Als je toch al blij bent met VoxFlow, is dit het moment om over te stappen.</p>
            {Cta("Stap over naar jaarabonnement", DashboardUrl + "/instellingen?tab=abo")}
            {CtaSec("Bekijk mijn jaarsamenvatting", DashboardUrl + "/gesprekken")}
            """));

    // ── #50 · Integratie gekoppeld ────────────────────────────────────────────

    public (string Subject, string Html) IntegrationConnected(string naam, string integratieNaam) =>
        ($"{integratieNaam} is gekoppeld", Layout(
            "Synchronisatie actief.",
            $"""
            <p>Hoi {naam},</p>
            <p>{integratieNaam} is succesvol gekoppeld aan VoxFlow. Synchronisatie is actief en afspraken die worden ingepland via je assistent verschijnen voortaan direct in {integratieNaam}.</p>
            {Cta("Bekijk mijn integratie-instellingen", DashboardUrl + "/instellingen")}
            """));

    // ── #51 · Integratie fout ─────────────────────────────────────────────────

    public (string Subject, string Html) IntegrationError(string naam, string integratieNaam, string foutCode) =>
        ($"Er is een fout in je {integratieNaam}-koppeling", Layout(
            "Synchronisatie mislukt.",
            $"""
            <p>Hoi {naam},</p>
            <p>De synchronisatie met <strong>{integratieNaam}</strong> is mislukt (foutcode: {foutCode}). Daardoor worden afspraken mogelijk niet correct doorgezet.</p>
            <p>Controleer je instellingen of neem contact op als je er niet uitkomt.</p>
            {Cta("Bekijk mijn integratie", DashboardUrl + "/instellingen")}
            {CtaSec("Neem contact op", "mailto:support@voxflow.nl")}
            """));

    // ── #52 · Integratie verbroken ────────────────────────────────────────────

    public (string Subject, string Html) IntegrationExpired(string naam, string integratieNaam) =>
        ($"Verbinding met {integratieNaam} verbroken", Layout(
            "Toegang verlopen — herstel de koppeling.",
            $"""
            <p>Hoi {naam},</p>
            <p>De verbinding met <strong>{integratieNaam}</strong> is verbroken, waarschijnlijk omdat de toegang is verlopen. Herstel de koppeling zodat alles weer synchroon loopt.</p>
            {Cta("Herstel koppeling", DashboardUrl + "/instellingen")}
            """));

    // ── #58 · Wachtlijst bevestiging ─────────────────────────────────────────

    public (string Subject, string Html) IntegrationNotifyConfirm(string naam, string integratieNaam) =>
        ($"Je staat op de lijst voor {integratieNaam}", Layout(
            $"Je hoort het zodra {integratieNaam} beschikbaar is.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je staat op de wachtlijst voor <strong>{integratieNaam}</strong>. Zodra de integratie beschikbaar is, laten we het je direct weten.</p>
            {Cta("Bekijk mijn integraties", DashboardUrl + "/integraties")}
            """));

    // ── #59 · Integratie nu live ──────────────────────────────────────────────

    public (string Subject, string Html) IntegrationNowLive(string naam, string integratieNaam) =>
        ($"{integratieNaam} is nu beschikbaar", Layout(
            $"{integratieNaam} is nu live in VoxFlow.",
            $"""
            <p>Hoi {naam},</p>
            <p>Goed nieuws! <strong>{integratieNaam}</strong> is nu beschikbaar in VoxFlow. Je kunt hem direct activeren via je integratie-instellingen.</p>
            {Cta($"Activeer {integratieNaam}", DashboardUrl + "/integraties")}
            """));

    // ── #56 · WhatsApp geactiveerd ────────────────────────────────────────────

    public (string Subject, string Html) WhatsAppActivated(string naam, string phoneNumber) =>
        ("WhatsApp Business is actief", Layout(
            "Bellers kunnen nu berichten ontvangen via WhatsApp.",
            $"""
            <p>Hoi {naam},</p>
            <p>WhatsApp Business is actief voor jouw account. Bellers kunnen voortaan berichten ontvangen via <strong>{phoneNumber}</strong>. Alles loopt automatisch via VoxFlow.</p>
            {Cta("Bekijk mijn WhatsApp-instellingen", DashboardUrl + "/integraties")}
            """));

    // ── #54 · Referral succesvol ──────────────────────────────────────────────

    public (string Subject, string Html) ReferralSuccess(string naam, string naamVriend) =>
        ($"{naamVriend} heeft zich aangemeld via jouw link", Layout(
            "Je beloning wordt verrekend met je volgende factuur.",
            $"""
            <p>Hoi {naam},</p>
            <p><strong>{naamVriend}</strong> heeft zich aangemeld en betaald via jouw referrallink. Je beloning, één maand gratis, wordt verrekend met je volgende factuur.</p>
            {Cta("Bekijk mijn referrals", DashboardUrl + "/instellingen")}
            """));

    // ── #55 · Referral beloning toegekend ────────────────────────────────────

    public (string Subject, string Html) ReferralRewarded(string naam, string beloningBedrag) =>
        ("Je referral-beloning is toegevoegd", Layout(
            $"€{beloningBedrag} tegoed op je account.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je referral-beloning van <strong>€{beloningBedrag}</strong> is toegevoegd aan je account. Die wordt automatisch verrekend met je volgende factuur.</p>
            {Cta("Bekijk mijn voordelen", DashboardUrl + "/instellingen")}
            {CtaSec("Deel mijn referrallink", DashboardUrl + "/instellingen")}
            """));

    // ── #57 · WhatsApp aanvraag (intern → admin) ──────────────────────────────

    public (string Subject, string Html) WhatsAppRequestedAdmin(string companyName, short companyId, string phoneNumber) =>
        ($"WhatsApp-aanvraag: {companyName}", Layout(
            $"{companyName} vraagt WhatsApp Business aan.",
            $"""
            <p>Bedrijf: <strong>{companyName}</strong> (ID: {companyId})</p>
            <p>Telefoonnummer: <strong>{phoneNumber}</strong></p>
            {Cta("Ga naar admin panel", "https://voxflow-a9b2ghh9anb6gnfa.westeurope-01.azurewebsites.net/admin")}
            """));

    // ── #60 · Wachtlijst aanvraag integratie (intern → admin) ────────────────

    public (string Subject, string Html) IntegrationNotifyAdmin(string companyName, short companyId, string integratieNaam) =>
        ($"Wachtlijst: {integratieNaam} — {companyName}", Layout(
            $"{companyName} wil {integratieNaam}.",
            $"""
            <p>Bedrijf: <strong>{companyName}</strong> (ID: {companyId})</p>
            <p>Integratie: <strong>{integratieNaam}</strong></p>
            {Cta("Ga naar admin panel", "https://voxflow-a9b2ghh9anb6gnfa.westeurope-01.azurewebsites.net/admin")}
            """));

    // ── Owner event notifications ─────────────────────────────────────────────

    public (string Subject, string Html) TeamMemberRemoved(string naam, string bedrijfsNaam) =>
        ($"Je bent verwijderd uit {bedrijfsNaam}", Layout(
            "Je toegang is ingetrokken.",
            $"""
            <p>Hoi {naam},</p>
            <p>Je bent verwijderd uit het VoxFlow-team van <strong>{bedrijfsNaam}</strong>. Je hebt geen toegang meer tot de gedeelde omgeving. Als je denkt dat dit een vergissing is, neem dan contact op met de beheerder.</p>
            """));

    public (string Subject, string Html) OwnerAppointmentCreated(
        string companyName, string? customerName, string type, DateTimeOffset startTime, string? callerPhone) =>
        ($"Nieuwe afspraak: {customerName ?? "klant"} – {type}", Layout(
            $"Afspraak ingepland op {startTime:dd-MM-yyyy HH:mm}.",
            $"""
            <p>Via uw VoxFlow-assistent is zojuist een afspraak ingepland:</p>
            <div class="stats"><table>
              <tr><td>Klant</td><td>{customerName ?? "Onbekend"}</td></tr>
              <tr><td>Type</td><td>{type}</td></tr>
              <tr><td>Datum &amp; tijd</td><td>{startTime:dd-MM-yyyy HH:mm}</td></tr>
              {(callerPhone is { Length: > 0 } ? $"<tr><td>Telefoonnummer</td><td>{callerPhone}</td></tr>" : "")}
            </table></div>
            {Cta("Bekijk afspraken", DashboardUrl + "/afspraken")}
            """));

    public (string Subject, string Html) OwnerCallbackCreated(
        string companyName, string callerName, string reason,
        DateTimeOffset scheduledFrom, DateTimeOffset scheduledUntil, string? callerPhone) =>
        ($"Terugbelverzoek: {callerName}", Layout(
            $"{callerName} wil teruggebeld worden.",
            $"""
            <p>Via uw VoxFlow-assistent is een terugbelverzoek aangemaakt:</p>
            <div class="stats"><table>
              <tr><td>Naam</td><td>{callerName}</td></tr>
              <tr><td>Reden</td><td>{reason}</td></tr>
              <tr><td>Bel terug tussen</td><td>{scheduledFrom:HH:mm} – {scheduledUntil:HH:mm} op {scheduledFrom:dd-MM-yyyy}</td></tr>
              {(callerPhone is { Length: > 0 } ? $"<tr><td>Telefoonnummer</td><td>{callerPhone}</td></tr>" : "")}
            </table></div>
            {Cta("Bekijk terugbelverzoeken", DashboardUrl + "/inbox")}
            """));

    public (string Subject, string Html) OwnerTransfer(
        string companyName, string? callerPhone, string? departmentName) =>
        ("Gesprek doorgestuurd naar medewerker", Layout(
            "Een beller is doorgestuurd door uw assistent.",
            $"""
            <p>Uw VoxFlow-assistent heeft een gesprek doorgestuurd{(departmentName is { Length: > 0 } ? $" naar de afdeling <strong>{departmentName}</strong>" : " naar een medewerker")}.</p>
            <div class="stats"><table>
              {(callerPhone is { Length: > 0 } ? $"<tr><td>Beller</td><td>{callerPhone}</td></tr>" : "")}
              {(departmentName is { Length: > 0 } ? $"<tr><td>Afdeling</td><td>{departmentName}</td></tr>" : "")}
            </table></div>
            {Cta("Bekijk gesprekken", DashboardUrl + "/gesprekken")}
            """));

    public (string Subject, string Html) OwnerUrgent(string companyName, string? callerPhone) =>
        ("Spoedmelding ontvangen van beller", Layout(
            "Urgente oproep via uw VoxFlow-assistent.",
            $"""
            <p>Uw VoxFlow-assistent heeft een urgente oproep ontvangen en de beller doorgestuurd naar een medewerker.</p>
            <div class="stats"><table>
              {(callerPhone is { Length: > 0 } ? $"<tr><td>Beller</td><td>{callerPhone}</td></tr>" : "")}
            </table></div>
            {Cta("Bekijk gesprekken", DashboardUrl + "/gesprekken")}
            """));

    // ── AVG / data lifecycle ──────────────────────────────────────────────────

    public (string Subject, string Html) SubscriptionExpiredWarning(string naam, int daysLeft) =>
        ("Laatste waarschuwing: uw gegevens worden binnenkort verwijderd", Layout(
            $"Uw VoxFlow-gegevens worden over {daysLeft} dag{(daysLeft == 1 ? "" : "en")} verwijderd.",
            $"""
            <p>Beste {naam},</p>
            <p>Uw VoxFlow-abonnement is verlopen en de assistent is niet meer actief. U heeft nog toegang tot uw dashboard.</p>
            <p><strong>Over {daysLeft} dag{(daysLeft == 1 ? "" : "en")} worden al uw gegevens definitief verwijderd</strong> in het kader van de AVG-wetgeving. Dit omvat alle gesprekken, afspraken, medewerkers en bedrijfsinstellingen.</p>
            <p>Wilt u uw gegevens behouden? Heractiveer uw abonnement vóór de verwijderdatum.</p>
            {Cta("Abonnement heractiveren", DashboardUrl + "/instellingen?tab=abo")}
            <p style="color:#6b7280;font-size:13px;">Heeft u vragen? Neem contact op via info@voxflow.nl.</p>
            """));

    public (string Subject, string Html) DataDeleted(string naam) =>
        ("Uw VoxFlow-gegevens zijn verwijderd", Layout(
            "Al uw gegevens zijn verwijderd conform de AVG.",
            $"""
            <p>Beste {naam},</p>
            <p>Conform de AVG-wetgeving zijn alle gegevens van uw VoxFlow-account definitief verwijderd.</p>
            <p>Als u in de toekomst opnieuw gebruik wilt maken van VoxFlow, kunt u een nieuw account aanmaken.</p>
            {Cta("Nieuw account aanmaken", "https://voxflow.nl/signup")}
            <p style="color:#6b7280;font-size:13px;">Dit is een automatisch bericht. U hoeft niets te doen.</p>
            """));
}

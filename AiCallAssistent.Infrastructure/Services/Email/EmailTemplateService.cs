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
            .body h2 { font-size:20px; margin:0 0 16px; color:#1a1a2e; }
            .body p { margin:0 0 14px; }
            .cta { display:inline-block; margin:20px 0 8px; padding:13px 28px; background:#6B46C1; color:#fff !important; text-decoration:none; border-radius:8px; font-weight:600; font-size:15px; }
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

    private static string DashboardUrl => "https://voxflow.nl/dashboard";

    // ── #2 · Welkomst ─────────────────────────────────────────────────────────

    public (string Subject, string Html) Welcome(string naam) =>
        ("Welkom bij VoxFlow 🎉", Layout(
            $"Welkom, {naam}! Je account is klaar.",
            $"""
            <h2>Welkom, {naam}!</h2>
            <p>Je account is aangemaakt en klaar om te starten. In een paar minuten stel je jouw AI-telefonist in en mis je nooit meer een gesprek.</p>
            {Cta("Start mijn onboarding", DashboardUrl + "/onboarding")}
            """));

    // ── #4 · Wachtwoord reset bevestiging ─────────────────────────────────────

    public (string Subject, string Html) PasswordResetConfirmed(string naam, string tijdstip) =>
        ("Je wachtwoord is gewijzigd", Layout(
            "Wachtwoord succesvol gewijzigd.",
            $"""
            <h2>Wachtwoord gewijzigd</h2>
            <p>Hallo {naam},</p>
            <p>Je wachtwoord is succesvol gewijzigd op <strong>{tijdstip}</strong>. Als jij dit niet was, beveilig dan direct je account.</p>
            {Cta("Beveilig mijn account", DashboardUrl + "/instellingen")}
            """));

    // ── #6 · Onboarding start ─────────────────────────────────────────────────

    public (string Subject, string Html) OnboardingStart(string naam) =>
        ("Je assistent wacht op je — 3 stappen", Layout(
            "Nog 5 minuten en je bent live.",
            $"""
            <h2>Hoi {naam}, je assistent is bijna klaar</h2>
            <p>Je hoeft maar 3 dingen te doen:</p>
            <ol style="padding-left:20px;margin:0 0 16px">
              <li>Kies een telefoonnummer</li>
              <li>Stel de begroeting in</li>
              <li>Test een gesprek</li>
            </ol>
            <p>Duurt minder dan 5 minuten.</p>
            {Cta("Start setup", DashboardUrl + "/onboarding")}
            """));

    // ── #7 · Onboarding herinnering ───────────────────────────────────────────

    public (string Subject, string Html) OnboardingReminder(string naam) =>
        ("Je setup is nog niet klaar ⚠️", Layout(
            "Oproepen worden nog niet afgehandeld.",
            $"""
            <h2>Hoi {naam}</h2>
            <p>Je setup is nog niet afgerond. Dat betekent dat oproepen op dit moment nog <strong>niet</strong> worden afgehandeld door VoxFlow.</p>
            <p>Rond de setup in 5 minuten af zodat je niets meer mist.</p>
            {Cta("Rond setup af", DashboardUrl + "/onboarding")}
            """));

    // ── #8 · Setup voltooid ───────────────────────────────────────────────────

    public (string Subject, string Html) SetupComplete(string naam) =>
        ("Je bent live! 🚀", Layout(
            "VoxFlow staat aan — elk gesprek wordt nu opgevangen.",
            $"""
            <h2>{naam}, je bent live!</h2>
            <p>VoxFlow staat aan. Elk gesprek dat je mist wordt nu automatisch door jouw AI-telefonist afgehandeld.</p>
            <p>Bekijk je dashboard om gesprekken, afspraken en callbacks te volgen.</p>
            {Cta("Ga naar mijn dashboard", DashboardUrl)}
            """));

    // ── #9 · Setup foutmelding ────────────────────────────────────────────────

    public (string Subject, string Html) SetupError(string naam, string foutOmschrijving) =>
        ("Er is een fout opgetreden tijdens je setup", Layout(
            "We konden je assistent niet activeren.",
            $"""
            <h2>Hoi {naam}, er is iets misgegaan</h2>
            <p>We konden je assistent niet volledig activeren. Foutmelding:</p>
            <p style="background:#fff0f0;border-left:4px solid #e53e3e;padding:12px 16px;border-radius:4px;font-family:monospace;font-size:13px">{foutOmschrijving}</p>
            <p>Probeer het opnieuw of neem contact op met ons support-team.</p>
            {Cta("Probeer opnieuw", DashboardUrl + "/onboarding")}
            """));

    // ── #10 · Abonnement gestart ──────────────────────────────────────────────

    public (string Subject, string Html) SubscriptionStarted(string naam, string abonnement, string startdatum, string volgendeFactuur) =>
        ("Je abonnement is actief", Layout(
            $"Welkom bij VoxFlow {abonnement}.",
            $"""
            <h2>Abonnement actief, {naam}!</h2>
            <p>Bedankt. Je <strong>{abonnement}</strong>-abonnement is actief.</p>
            <div class="stats"><table>
              <tr><td>Plan</td><td>{abonnement}</td></tr>
              <tr><td>Startdatum</td><td>{startdatum}</td></tr>
              <tr><td>Volgende factuur</td><td>{volgendeFactuur}</td></tr>
            </table></div>
            {Cta("Ga naar mijn account", DashboardUrl)}
            """));

    // ── #11 · Abonnement geüpgraded ───────────────────────────────────────────

    public (string Subject, string Html) SubscriptionUpgraded(string naam, string nieuwPlan, IEnumerable<string> nieuweFeatures) =>
        ($"Je bent geüpgraded naar {nieuwPlan} 🎉", Layout(
            $"Nieuwe functies beschikbaar.",
            $"""
            <h2>Upgrade geslaagd, {naam}!</h2>
            <p>Je gebruikt nu het <strong>{nieuwPlan}</strong>-pakket. Je hebt nu toegang tot:</p>
            <ul style="padding-left:20px;margin:0 0 16px">
              {string.Join("", nieuweFeatures.Select(f => $"<li>{f}</li>"))}
            </ul>
            {Cta("Ontdek de nieuwe functies", DashboardUrl)}
            """));

    // ── #12 · Abonnement gedowngrade ──────────────────────────────────────────

    public (string Subject, string Html) SubscriptionDowngraded(string naam, string nieuwPlan, string ingangsDatum, IEnumerable<string> vervaldeFeatures) =>
        ($"Je plan is gewijzigd naar {nieuwPlan}", Layout(
            $"Pakketwijziging per {ingangsDatum}.",
            $"""
            <h2>Pakket gewijzigd, {naam}</h2>
            <p>Je abonnement is gewijzigd naar <strong>{nieuwPlan}</strong> per <strong>{ingangsDatum}</strong>.</p>
            <p>De volgende functies zijn niet meer beschikbaar:</p>
            <ul style="padding-left:20px;margin:0 0 16px">
              {string.Join("", vervaldeFeatures.Select(f => $"<li>{f}</li>"))}
            </ul>
            {Cta("Beheer mijn abonnement", DashboardUrl + "/instellingen")}
            """));

    // ── #13 · Abonnement opgezegd ─────────────────────────────────────────────

    public (string Subject, string Html) SubscriptionCancelled(string naam, string eindDatum) =>
        ("Je abonnement is opgezegd", Layout(
            $"Toegang tot {eindDatum}.",
            $"""
            <h2>Tot ziens, {naam}</h2>
            <p>Je abonnement is opgezegd. Je hebt toegang tot <strong>{eindDatum}</strong>.</p>
            <p>Wil je VoxFlow toch nog een kans geven? Je kunt altijd opnieuw starten.</p>
            {Cta("Abonnement heractiveren", DashboardUrl + "/instellingen")}
            """));

    // ── #18 · Account tijdelijk beperkt ───────────────────────────────────────

    public (string Subject, string Html) AccountRestricted(string naam, string bedrag) =>
        ("Je account is tijdelijk beperkt", Layout(
            "Onbetaalde factuur — betaal nu om door te gaan.",
            $"""
            <h2>Actie vereist, {naam}</h2>
            <p>Je account is tijdelijk beperkt wegens een openstaande factuur van <strong>€{bedrag}</strong>.</p>
            <p>Betaal nu om je account direct te herstellen en geen gesprekken te missen.</p>
            {Cta("Betaal nu en herstel account", DashboardUrl + "/instellingen")}
            """));

    // ── #21 · Wekelijks rapport ───────────────────────────────────────────────

    public (string Subject, string Html) WeeklyReport(string naam, int gesprekken, int doorverbonden, int voicemails, string gemiddeldeDuur, int weekNummer) =>
        ($"Jouw weekoverzicht — week {weekNummer}", Layout(
            $"{gesprekken} gesprekken afgehandeld.",
            $"""
            <h2>Week {weekNummer} — jouw overzicht</h2>
            <p>Hallo {naam}, dit is wat VoxFlow deze week voor je deed:</p>
            <div class="stats"><table>
              <tr><td>Gesprekken afgehandeld</td><td>{gesprekken}</td></tr>
              <tr><td>Doorverbonden</td><td>{doorverbonden}</td></tr>
              <tr><td>Voicemails</td><td>{voicemails}</td></tr>
              <tr><td>Gemiddelde gespreksduur</td><td>{gemiddeldeDuur}</td></tr>
            </table></div>
            {Cta("Bekijk volledig rapport", DashboardUrl + "/gesprekken")}
            """));

    // ── #22 · Maandelijks rapport ─────────────────────────────────────────────

    public (string Subject, string Html) MonthlyReport(string naam, string maand, int totaalGesprekken, string besteDag, string groeiPercentage, string klanttevredenheidScore) =>
        ($"Jouw maandoverzicht — {maand}", Layout(
            $"{totaalGesprekken} gesprekken in {maand}.",
            $"""
            <h2>{maand} — jouw maandoverzicht</h2>
            <p>Hallo {naam}, hier is wat VoxFlow in {maand} voor je deed:</p>
            <div class="stats"><table>
              <tr><td>Totaal gesprekken</td><td>{totaalGesprekken}</td></tr>
              <tr><td>Drukste dag</td><td>{besteDag}</td></tr>
              <tr><td>Groei t.o.v. vorige maand</td><td>{groeiPercentage}</td></tr>
              <tr><td>Klanttevredenheid</td><td>{klanttevredenheidScore}</td></tr>
            </table></div>
            {Cta("Bekijk volledig rapport", DashboardUrl + "/gesprekken")}
            """));

    // ── #23 · Inactief 7 dagen ────────────────────────────────────────────────

    public (string Subject, string Html) Inactive7Days(string naam) =>
        ("7 dagen niet ingelogd — mogelijk gemiste gesprekken", Layout(
            "Er kunnen gesprekken op je wachten.",
            $"""
            <h2>Hoi {naam}</h2>
            <p>Je hebt de afgelopen 7 dagen niet ingelogd. Ondertussen zijn er mogelijk gesprekken afgehandeld die jou interesseren.</p>
            {Cta("Log in en bekijk mijn gesprekken", DashboardUrl + "/gesprekken")}
            """));

    // ── #24 · Inactief 14 dagen ───────────────────────────────────────────────

    public (string Subject, string Html) Inactive14Days(string naam, int aantalGesprekken) =>
        ("14 dagen inactief — je hebt gesprekken gemist", Layout(
            $"We hebben {aantalGesprekken} gesprekken voor je afgehandeld.",
            $"""
            <h2>Hoi {naam}</h2>
            <p>14 dagen niet ingelogd. In die tijd heeft VoxFlow <strong>{aantalGesprekken} gesprekken</strong> voor je afgehandeld. Weet je wat daarin zat?</p>
            {Cta("Bekijk mijn gemiste gesprekken", DashboardUrl + "/gesprekken")}
            """));

    // ── #25 · Inactief 30 dagen ───────────────────────────────────────────────

    public (string Subject, string Html) Inactive30Days(string naam) =>
        ("Is VoxFlow nog iets voor je?", Layout(
            "Al een maand niet ingelogd.",
            $"""
            <h2>Hoi {naam}</h2>
            <p>Je hebt al een maand niet ingelogd. Is VoxFlow nog iets voor je? We horen graag van je.</p>
            {Cta("Neem contact op", "mailto:support@voxflow.nl")}
            <br>
            {Cta("Log opnieuw in", DashboardUrl)}
            """));

    // ── #26 · Re-activatie ────────────────────────────────────────────────────

    public (string Subject, string Html) ReActivated(string naam) =>
        ("Welkom terug bij VoxFlow! 👋", Layout(
            "Je account staat klaar.",
            $"""
            <h2>Welkom terug, {naam}!</h2>
            <p>Fijn dat je er weer bent. Je account is actief en alles staat klaar — je assistent vangt elk gesprek op dat je mist.</p>
            {Cta("Ga naar mijn dashboard", DashboardUrl)}
            """));

    // ── #33 · Feedback verzoek ────────────────────────────────────────────────

    public (string Subject, string Html) FeedbackRequest(string naam, string ticketId) =>
        ("Hoe was je ervaring met VoxFlow-support?", Layout(
            "Jouw mening telt.",
            $"""
            <h2>Hoi {naam}</h2>
            <p>Je supportvraag (#{ticketId}) is opgelost. Hoe was je ervaring?</p>
            <p>Jouw feedback helpt ons om VoxFlow beter te maken.</p>
            {Cta("Geef feedback", "https://voxflow.nl/feedback?ticket=" + ticketId)}
            """));

    // ── #34 · Nieuwe login gedetecteerd ──────────────────────────────────────

    public (string Subject, string Html) NewLoginDetected(string naam, string apparaat, string locatie, string tijdstip) =>
        ("Nieuwe inlog op je VoxFlow-account", Layout(
            "Nieuwe inlog gedetecteerd — was jij dit?",
            $"""
            <h2>Nieuwe inlog gedetecteerd</h2>
            <p>Hallo {naam},</p>
            <p>Er is ingelogd op je account vanaf een nieuw apparaat of locatie:</p>
            <div class="stats"><table>
              <tr><td>Apparaat</td><td>{apparaat}</td></tr>
              <tr><td>Locatie</td><td>{locatie}</td></tr>
              <tr><td>Tijdstip</td><td>{tijdstip}</td></tr>
            </table></div>
            <p>Was jij dit? Dan hoef je niets te doen. Was jij dit niet, beveilig dan direct je account.</p>
            {Cta("Mijn account beveiligen", DashboardUrl + "/instellingen")}
            """));

    // ── #36 · Account vergrendeld ─────────────────────────────────────────────

    public (string Subject, string Html) AccountLocked(string naam, string tijdstip) =>
        ("Je account is tijdelijk vergrendeld", Layout(
            "Te veel mislukte inlogpogingen.",
            $"""
            <h2>Account vergrendeld</h2>
            <p>Hallo {naam},</p>
            <p>Je account is tijdelijk vergrendeld wegens te veel mislukte inlogpogingen op <strong>{tijdstip}</strong>.</p>
            <p>Probeer het over 15 minuten opnieuw of reset je wachtwoord.</p>
            {Cta("Wachtwoord resetten", "https://voxflow.nl/login")}
            """));

    // ── #43 · Win-back dag 1 ──────────────────────────────────────────────────

    public (string Subject, string Html) WinBack1(string naam, int aantalGesprekken) =>
        ("Je hebt VoxFlow opgezegd — altijd welkom terug", Layout(
            "We missen je al.",
            $"""
            <h2>Tot ziens, {naam}</h2>
            <p>Je hebt je abonnement opgezegd. In de periode dat je VoxFlow gebruikte, werden er <strong>{aantalGesprekken} gesprekken</strong> voor je afgehandeld.</p>
            <p>Mocht je van gedachten veranderen, ben je altijd welkom terug.</p>
            {Cta("Herstart mijn abonnement", DashboardUrl + "/instellingen")}
            """));

    // ── #44 · Win-back dag 7 ──────────────────────────────────────────────────

    public (string Subject, string Html) WinBack7(string naam, int kortingsPercentage, string aanbiedingGeldigTot) =>
        ($"Kom terug met {kortingsPercentage}% korting", Layout(
            $"Speciale aanbieding — geldig tot {aanbiedingGeldigTot}.",
            $"""
            <h2>Hoi {naam}, we hebben een aanbod voor je</h2>
            <p>Kom terug naar VoxFlow en krijg <strong>{kortingsPercentage}% korting</strong> op je eerste maand.</p>
            <p><em>Aanbieding geldig tot {aanbiedingGeldigTot}.</em></p>
            {Cta("Kom terug met korting", DashboardUrl + "/instellingen")}
            """));

    // ── #45 · Win-back dag 30 ─────────────────────────────────────────────────

    public (string Subject, string Html) WinBack30(string naam) =>
        ("Nog op zoek naar een telefonische bereikbaarheidsoplossing?", Layout(
            "Kijk wat er nieuw is bij VoxFlow.",
            $"""
            <h2>Hoi {naam}</h2>
            <p>Ben je nog op zoek naar een manier om elk telefoontje op te vangen, ook als je er niet bent?</p>
            <p>VoxFlow heeft de afgelopen maanden nieuwe functies gekregen. Misschien is het nu wél precies wat je nodig hebt.</p>
            {Cta("Bekijk wat er nieuw is bij VoxFlow", "https://voxflow.nl")}
            """));

    // ── #48 · Team lid verwijderd ─────────────────────────────────────────────

    public (string Subject, string Html) TeamMemberRemoved(string naam, string bedrijfsNaam) =>
        ($"Je bent verwijderd uit {bedrijfsNaam}", Layout(
            "Je toegang is ingetrokken.",
            $"""
            <h2>Hoi {naam}</h2>
            <p>Je bent verwijderd uit het VoxFlow-team van <strong>{bedrijfsNaam}</strong>.</p>
            <p>Je hebt geen toegang meer tot de gedeelde omgeving. Als je denkt dat dit een vergissing is, neem dan contact op met de beheerder.</p>
            """));

    // ── #50 · Integratie gekoppeld ────────────────────────────────────────────

    public (string Subject, string Html) IntegrationConnected(string naam, string integratieNaam) =>
        ($"{integratieNaam} succesvol gekoppeld", Layout(
            "Synchronisatie actief.",
            $"""
            <h2>{integratieNaam} is gekoppeld, {naam}!</h2>
            <p>De koppeling met <strong>{integratieNaam}</strong> is succesvol. Gesprekken en afspraken worden nu automatisch gesynchroniseerd.</p>
            {Cta("Bekijk integratie-instellingen", DashboardUrl + "/instellingen")}
            """));

    // ── #51 · Integratie foutmelding ──────────────────────────────────────────

    public (string Subject, string Html) IntegrationError(string naam, string integratieNaam, string foutCode) =>
        ($"Fout in je {integratieNaam}-koppeling", Layout(
            "Synchronisatie mislukt.",
            $"""
            <h2>Fout in {integratieNaam}-koppeling</h2>
            <p>Hallo {naam},</p>
            <p>Er is een fout opgetreden in je <strong>{integratieNaam}</strong>-koppeling (code: <code>{foutCode}</code>).</p>
            <p>Controleer je instellingen of herstel de koppeling om synchronisatie te hervatten.</p>
            {Cta("Bekijk integratie", DashboardUrl + "/instellingen")}
            """));

    // ── #52 · Integratie verbroken ────────────────────────────────────────────

    public (string Subject, string Html) IntegrationExpired(string naam, string integratieNaam) =>
        ($"Verbinding met {integratieNaam} verbroken", Layout(
            "Toegangstoken verlopen — herstel de koppeling.",
            $"""
            <h2>Koppeling verbroken</h2>
            <p>Hallo {naam},</p>
            <p>De verbinding met <strong>{integratieNaam}</strong> is verbroken, waarschijnlijk omdat het toegangstoken is verlopen.</p>
            <p>Herstel de koppeling om synchronisatie te hervatten.</p>
            {Cta("Herstel koppeling", DashboardUrl + "/instellingen")}
            """));

    // ── #53 · Referral uitgenodigd ────────────────────────────────────────────

    public (string Subject, string Html) ReferralSent(string naam, string uitgenodigdEmail) =>
        ("Je uitnodiging is verstuurd", Layout(
            "Beloning zodra ze aanmelden en betalen.",
            $"""
            <h2>Uitnodiging verstuurd!</h2>
            <p>Hallo {naam},</p>
            <p>Je uitnodiging is verstuurd naar <strong>{uitgenodigdEmail}</strong>. Zodra zij aanmelden en na hun proefperiode betalen, ontvang jij een maand gratis.</p>
            {Cta("Deel mijn referrallink", DashboardUrl + "/instellingen")}
            """));

    // ── #54 · Referral succes ─────────────────────────────────────────────────

    public (string Subject, string Html) ReferralSuccess(string naam, string naamVriend) =>
        ($"{naamVriend} heeft betaald via jouw link 🎉", Layout(
            "Je beloning wordt verwerkt.",
            $"""
            <h2>Goed nieuws, {naam}!</h2>
            <p><strong>{naamVriend}</strong> heeft zich aangemeld en betaald via jouw referrallink.</p>
            <p>Je beloning (1 maand gratis) wordt binnenkort verwerkt en automatisch verrekend met je volgende factuur.</p>
            {Cta("Bekijk mijn referrals", DashboardUrl + "/instellingen")}
            """));

    // ── #55 · Referral beloning ───────────────────────────────────────────────

    public (string Subject, string Html) ReferralRewarded(string naam, string beloningBedrag) =>
        ("Je referral-beloning is toegevoegd ✨", Layout(
            $"€{beloningBedrag} tegoed op je account.",
            $"""
            <h2>Beloning toegevoegd, {naam}!</h2>
            <p>Je referral-beloning van <strong>€{beloningBedrag}</strong> is toegevoegd aan je account. Het wordt automatisch verrekend met je volgende factuur.</p>
            {Cta("Bekijk mijn voordelen", DashboardUrl + "/instellingen")}
            <br>
            {Cta("Deel mijn referrallink", DashboardUrl + "/instellingen")}
            """));

    // ── #56 · WhatsApp geactiveerd (naar bedrijf) ─────────────────────────────

    public (string Subject, string Html) WhatsAppActivated(string naam, string phoneNumber) =>
        ("WhatsApp Business is nu actief!", Layout(
            "Uw klanten kunnen nu bereikt worden via WhatsApp.",
            $"""
            <h2>WhatsApp Business is actief, {naam}!</h2>
            <p>Goed nieuws! WhatsApp Business is nu ingeschakeld voor uw VoxFlow-assistent.</p>
            <p>Uw klanten kunnen voortaan via WhatsApp berichten ontvangen vanaf <strong>{phoneNumber}</strong>.</p>
            <p>Afspraakbevestigingen, herinneringen en terugbelbevestigingen worden nu automatisch verstuurd.</p>
            {Cta("Bekijk mijn integraties", DashboardUrl + "/instellingen")}
            """));

    // ── #57 · WhatsApp aanvraag (intern naar admin) ───────────────────────────

    public (string Subject, string Html) WhatsAppRequestedAdmin(string companyName, short companyId, string phoneNumber) =>
        ($"WhatsApp-aanvraag: {companyName}", Layout(
            $"{companyName} wil WhatsApp activeren.",
            $"""
            <h2>Nieuwe WhatsApp-aanvraag</h2>
            <p><strong>Bedrijf:</strong> {companyName} (ID: {companyId})</p>
            <p><strong>Telefoonnummer:</strong> {phoneNumber}</p>
            <p>Registreer dit nummer als WhatsApp-sender in de Meta Business Manager en klik daarna op Activeer in het admin panel.</p>
            {Cta("Ga naar admin panel", "https://voxflow-a9b2ghh9anb6gnfa.westeurope-01.azurewebsites.net/admin")}
            """));

    // ── #58 · Integratie wachtlijst bevestiging (naar klant) ─────────────────

    public (string Subject, string Html) IntegrationNotifyConfirm(string naam, string integratieNaam) =>
        ($"Je staat op de wachtlijst voor {integratieNaam}", Layout(
            $"Je hoort het als {integratieNaam} live gaat!",
            $"""
            <h2>Je staat op de lijst, {naam}!</h2>
            <p>We hebben je aanvraag ontvangen. Zodra <strong>{integratieNaam}</strong> beschikbaar is in VoxFlow, sturen we je een bericht.</p>
            <p>We werken hier actief aan — je bent er vroeg bij.</p>
            {Cta("Bekijk mijn integraties", DashboardUrl + "/integraties")}
            """));

    // ── #59 · Integratie is nu live (naar klant) ──────────────────────────────

    public (string Subject, string Html) IntegrationNowLive(string naam, string integratieNaam) =>
        ($"{integratieNaam} is nu beschikbaar!", Layout(
            $"Goed nieuws: {integratieNaam} is live.",
            $"""
            <h2>Goed nieuws, {naam}!</h2>
            <p><strong>{integratieNaam}</strong> is nu beschikbaar in VoxFlow. Je kunt het direct activeren via je integratiepagina.</p>
            {Cta("Integratie activeren", DashboardUrl + "/integraties")}
            """));

    // ── #60 · Integratie wachtlijst aanvraag (intern naar admin) ─────────────

    public (string Subject, string Html) IntegrationNotifyAdmin(string companyName, short companyId, string integratieNaam) =>
        ($"Wachtlijst: {integratieNaam} — {companyName}", Layout(
            $"{companyName} wil {integratieNaam}.",
            $"""
            <h2>Nieuwe wachtlijst-aanvraag</h2>
            <p><strong>Bedrijf:</strong> {companyName} (ID: {companyId})</p>
            <p><strong>Integratie:</strong> {integratieNaam}</p>
            <p>Klik op "Live zetten" in het admin panel zodra de integratie beschikbaar is.</p>
            {Cta("Ga naar admin panel", "https://voxflow-a9b2ghh9anb6gnfa.westeurope-01.azurewebsites.net/admin")}
            """));

    // ── #61 · Abonnement verlopen — waarschuwing 5 dagen voor verwijdering ──

    public (string Subject, string Html) SubscriptionExpiredWarning(string naam, int daysLeft) =>
        ("Laatste waarschuwing: uw gegevens worden binnenkort verwijderd", Layout(
            $"Uw VoxFlow-gegevens worden over {daysLeft} dag{(daysLeft == 1 ? "" : "en")} verwijderd.",
            $"""
            <h2>Uw abonnement is verlopen</h2>
            <p>Beste {naam},</p>
            <p>Uw VoxFlow-abonnement is verlopen en de assistent is niet meer actief. U heeft nog toegang tot uw dashboard.</p>
            <p><strong>Over {daysLeft} dag{(daysLeft == 1 ? "" : "en")} worden al uw gegevens definitief verwijderd</strong> in het kader van de AVG-wetgeving. Dit omvat:</p>
            <ul>
              <li>Alle gesprekken en belhistorie</li>
              <li>Afspraken en afspraaktypen</li>
              <li>Medewerkers en bedrijfsinstellingen</li>
              <li>Alle overige bedrijfsgegevens</li>
            </ul>
            <p>Wilt u uw gegevens behouden? Heractiveer uw abonnement vóór de verwijderdatum.</p>
            {Cta("Abonnement heractiveren", "https://voxflow.nl/dashboard/instellingen?tab=abo")}
            <p style="color:#6b7280;font-size:13px;">Heeft u vragen? Neem contact op via info@voxflow.nl.</p>
            """));

    // ── #62 · Bevestiging gegevensverwijdering (AVG) ─────────────────────────

    public (string Subject, string Html) DataDeleted(string naam) =>
        ("Uw VoxFlow-gegevens zijn verwijderd", Layout(
            "Al uw gegevens zijn verwijderd conform de AVG.",
            $"""
            <h2>Gegevens verwijderd</h2>
            <p>Beste {naam},</p>
            <p>Conform de AVG-wetgeving zijn alle gegevens van uw VoxFlow-account definitief verwijderd.</p>
            <p>Als u in de toekomst opnieuw gebruik wilt maken van VoxFlow, kunt u een nieuw account aanmaken.</p>
            {Cta("Nieuw account aanmaken", "https://voxflow.nl/signup")}
            <p style="color:#6b7280;font-size:13px;">Dit is een automatisch bericht. U hoeft niets te doen.</p>
            """));
}

# VoxFlow — Email Templates Overzicht

Alle e-mails worden verstuurd via Gmail SMTP. Afzender: VoxFlow.
Hieronder staan alle templates, gegroepeerd per categorie.

---

## Account & Onboarding

### #2 · Welkomstmail
**Wanneer:** Direct na registratie  
**Onderwerp:** `Welkom bij VoxFlow 🎉`

> Welkom, [naam]!
> Je account is aangemaakt en klaar om te starten. In een paar minuten stel je jouw AI-telefonist in en mis je nooit meer een gesprek.
>
> → [Start mijn onboarding]

---

### #6 · Onboarding gestart
**Wanneer:** Na eerste inlog / start setup  
**Onderwerp:** `Je assistent wacht op je — 3 stappen`

> Hoi [naam], je assistent is bijna klaar
>
> Je hoeft maar 3 dingen te doen:
> 1. Kies een telefoonnummer
> 2. Stel de begroeting in
> 3. Test een gesprek
>
> Duurt minder dan 5 minuten.
>
> → [Start setup]

---

### #7 · Onboarding herinnering
**Wanneer:** Als onboarding niet afgerond is (bijv. na 24u)  
**Onderwerp:** `Je setup is nog niet klaar ⚠️`

> Hoi [naam]
>
> Je setup is nog niet afgerond. Dat betekent dat oproepen op dit moment nog **niet** worden afgehandeld door VoxFlow.
>
> Rond de setup in 5 minuten af zodat je niets meer mist.
>
> → [Rond setup af]

---

### #8 · Setup voltooid
**Wanneer:** Na succesvolle activatie  
**Onderwerp:** `Je bent live! 🚀`

> [naam], je bent live!
>
> VoxFlow staat aan. Elk gesprek dat je mist wordt nu automatisch door jouw AI-telefonist afgehandeld.
>
> → [Ga naar mijn dashboard]

---

### #9 · Setup foutmelding
**Wanneer:** Als activatie technisch mislukt  
**Onderwerp:** `Er is een fout opgetreden tijdens je setup`

> Hoi [naam], er is iets misgegaan
>
> We konden je assistent niet volledig activeren. Foutmelding:
> `[foutmelding]`
>
> → [Probeer opnieuw]

---

## Abonnement & Facturatie

### #10 · Abonnement gestart
**Wanneer:** Na succesvolle Stripe checkout  
**Onderwerp:** `Je abonnement is actief`

> Abonnement actief, [naam]!
>
> | Plan         | [abonnement]   |
> | Startdatum   | [datum]        |
> | Volgende factuur | [datum]    |
>
> → [Ga naar mijn account]

---

### #11 · Abonnement geüpgraded
**Wanneer:** Na upgrade naar hoger plan  
**Onderwerp:** `Je bent geüpgraded naar [plan] 🎉`

> Upgrade geslaagd, [naam]!
>
> Je gebruikt nu het **[plan]**-pakket. Je hebt nu toegang tot:
> - [nieuwe feature 1]
> - [nieuwe feature 2]
>
> → [Ontdek de nieuwe functies]

---

### #12 · Abonnement gedowngraded
**Wanneer:** Na downgrade naar lager plan (op volgende factuurmoment)  
**Onderwerp:** `Je plan is gewijzigd naar [plan]`

> Pakket gewijzigd, [naam]
>
> Je abonnement is gewijzigd naar **[plan]** per **[datum]**.
>
> De volgende functies zijn niet meer beschikbaar:
> - [vervallen feature 1]
>
> → [Beheer mijn abonnement]

---

### #13 · Abonnement opgezegd
**Wanneer:** Na opzegging via Stripe portal  
**Onderwerp:** `Je abonnement is opgezegd`

> Tot ziens, [naam]
>
> Je abonnement is opgezegd. Je hebt toegang tot **[einddatum]**.
>
> → [Abonnement heractiveren]

---

### #18 · Account tijdelijk beperkt
**Wanneer:** Bij openstaande factuur / betaling mislukt  
**Onderwerp:** `Je account is tijdelijk beperkt`

> Actie vereist, [naam]
>
> Je account is tijdelijk beperkt wegens een openstaande factuur van **€[bedrag]**.
>
> → [Betaal nu en herstel account]

---

## Rapportages

### #21 · Weekrapport
**Wanneer:** Wekelijks automatisch (niet actief geïmplementeerd)  
**Onderwerp:** `Jouw weekoverzicht — week [nr]`

> Week [nr] — jouw overzicht
>
> | Gesprekken afgehandeld | [aantal] |
> | Doorverbonden          | [aantal] |
> | Voicemails             | [aantal] |
> | Gem. gespreksduur      | [duur]   |
>
> → [Bekijk volledig rapport]

---

### #22 · Maandrapport
**Wanneer:** Maandelijks automatisch (niet actief geïmplementeerd)  
**Onderwerp:** `Jouw maandoverzicht — [maand]`

> [maand] — jouw maandoverzicht
>
> | Totaal gesprekken    | [aantal]       |
> | Drukste dag          | [dag]          |
> | Groei t.o.v. vorig   | [percentage]   |
> | Klanttevredenheid    | [score]        |
>
> → [Bekijk volledig rapport]

---

## Inactiviteit & Win-back

### #23 · Inactief 7 dagen
**Onderwerp:** `7 dagen niet ingelogd — mogelijk gemiste gesprekken`

> Er kunnen gesprekken op je wachten, [naam].
>
> → [Log in en bekijk mijn gesprekken]

---

### #24 · Inactief 14 dagen
**Onderwerp:** `14 dagen inactief — je hebt gesprekken gemist`

> 14 dagen niet ingelogd. In die tijd heeft VoxFlow **[aantal] gesprekken** voor je afgehandeld.
>
> → [Bekijk mijn gemiste gesprekken]

---

### #25 · Inactief 30 dagen
**Onderwerp:** `Is VoxFlow nog iets voor je?`

> Je hebt al een maand niet ingelogd. Is VoxFlow nog iets voor je?
>
> → [Neem contact op] / [Log opnieuw in]

---

### #26 · Re-activatie
**Wanneer:** Na eerste login na lange afwezigheid  
**Onderwerp:** `Welkom terug bij VoxFlow! 👋`

> Welkom terug, [naam]! Je account staat klaar.
>
> → [Ga naar mijn dashboard]

---

### #43 · Win-back dag 1
**Wanneer:** Dag na opzegging  
**Onderwerp:** `Je hebt VoxFlow opgezegd — altijd welkom terug`

> We missen je al, [naam]. In de periode dat je VoxFlow gebruikte, werden er **[aantal] gesprekken** voor je afgehandeld.
>
> → [Herstart mijn abonnement]

---

### #44 · Win-back dag 7
**Wanneer:** 7 dagen na opzegging  
**Onderwerp:** `Kom terug met [x]% korting`

> Kom terug naar VoxFlow en krijg **[x]% korting** op je eerste maand.
> Geldig tot [datum].
>
> → [Kom terug met korting]

---

### #45 · Win-back dag 30
**Wanneer:** 30 dagen na opzegging  
**Onderwerp:** `Nog op zoek naar een telefonische bereikbaarheidsoplossing?`

> VoxFlow heeft de afgelopen maanden nieuwe functies gekregen. Misschien is het nu wél precies wat je nodig hebt.
>
> → [Bekijk wat er nieuw is bij VoxFlow]

---

## Beveiliging

### #4 · Wachtwoord gewijzigd
**Wanneer:** Na succesvolle wachtwoordreset  
**Onderwerp:** `Je wachtwoord is gewijzigd`

> Hallo [naam], je wachtwoord is succesvol gewijzigd op [tijdstip].
> Als jij dit niet was, beveilig dan direct je account.
>
> → [Beveilig mijn account]

---

### #34 · Nieuwe inlog gedetecteerd
**Wanneer:** Login vanaf onbekend apparaat/locatie (niet actief geïmplementeerd)  
**Onderwerp:** `Nieuwe inlog op je VoxFlow-account`

> | Apparaat  | [apparaat] |
> | Locatie   | [locatie]  |
> | Tijdstip  | [tijdstip] |
>
> Was jij dit niet, beveilig dan direct je account.
>
> → [Mijn account beveiligen]

---

### #36 · Account vergrendeld
**Wanneer:** Na te veel mislukte inlogpogingen (niet actief geïmplementeerd)  
**Onderwerp:** `Je account is tijdelijk vergrendeld`

> Je account is vergrendeld wegens te veel mislukte inlogpogingen. Probeer over 15 minuten opnieuw.
>
> → [Wachtwoord resetten]

---

## Team

### #48 · Team lid verwijderd
**Wanneer:** Als een medewerker verwijderd wordt uit het dashboard  
**Onderwerp:** `Je bent verwijderd uit [bedrijfsnaam]`

> Hoi [naam], je bent verwijderd uit het VoxFlow-team van **[bedrijfsnaam]**.
> Neem contact op met de beheerder als dit een vergissing is.

---

## Support

### #33 · Feedbackverzoek
**Wanneer:** Na afhandeling van supportticket (niet actief geïmplementeerd)  
**Onderwerp:** `Hoe was je ervaring met VoxFlow-support?`

> Je supportvraag (#[ticketId]) is opgelost. Hoe was je ervaring?
>
> → [Geef feedback]

---

## Referrals

### #53 · Referral uitnodiging verstuurd
**Onderwerp:** `Je uitnodiging is verstuurd`

> Je uitnodiging is verstuurd naar **[email]**. Zodra zij aanmelden en betalen, ontvang jij een maand gratis.
>
> → [Deel mijn referrallink]

---

### #54 · Referral succesvol
**Onderwerp:** `[naam vriend] heeft betaald via jouw link 🎉`

> **[naam]** heeft zich aangemeld en betaald via jouw referrallink.
> Je beloning (1 maand gratis) wordt verrekend met je volgende factuur.
>
> → [Bekijk mijn referrals]

---

### #55 · Referral beloning toegekend
**Onderwerp:** `Je referral-beloning is toegevoegd ✨`

> Je referral-beloning van **€[bedrag]** is toegevoegd aan je account.
>
> → [Bekijk mijn voordelen] / [Deel mijn referrallink]

---

## Integraties

### #50 · Integratie gekoppeld
**Wanneer:** Na succesvol koppelen van Outlook of andere integratie  
**Onderwerp:** `[integratienaam] succesvol gekoppeld`

> **[integratienaam]** is gekoppeld, [naam]! Synchronisatie is nu actief.
>
> → [Bekijk integratie-instellingen]

---

### #51 · Integratie fout
**Wanneer:** Als een integratie-sync mislukt (niet actief geïmplementeerd)  
**Onderwerp:** `Fout in je [integratienaam]-koppeling`

> Er is een fout opgetreden (code: `[foutcode]`). Controleer je instellingen.
>
> → [Bekijk integratie]

---

### #52 · Integratie verbroken
**Wanneer:** Als token verloopt (niet actief geïmplementeerd)  
**Onderwerp:** `Verbinding met [integratienaam] verbroken`

> De verbinding is verbroken, waarschijnlijk omdat het toegangstoken is verlopen.
>
> → [Herstel koppeling]

---

### #58 · Wachtlijst bevestiging (naar klant)
**Wanneer:** Als klant op "Houd me op de hoogte" klikt bij een komende integratie  
**Onderwerp:** `Je staat op de wachtlijst voor [integratienaam]`

> Je staat op de lijst, [naam]! Zodra **[integratienaam]** beschikbaar is, sturen we je een bericht.
>
> → [Bekijk mijn integraties]

---

### #59 · Integratie nu live (naar klant)
**Wanneer:** Als admin in het admin panel op "Live zetten" klikt  
**Onderwerp:** `[integratienaam] is nu beschikbaar!`

> Goed nieuws, [naam]! **[integratienaam]** is nu beschikbaar in VoxFlow.
>
> → [Integratie activeren]

---

## WhatsApp

### #56 · WhatsApp geactiveerd (naar bedrijf)
**Wanneer:** Als admin WhatsApp activeert voor een bedrijf  
**Onderwerp:** `WhatsApp Business is nu actief!`

> WhatsApp Business is actief, [naam]! Klanten kunnen voortaan berichten ontvangen vanaf **[telefoonnummer]**.
>
> → [Bekijk mijn integraties]

---

## Admin (intern, alleen naar quintenwit41@gmail.com)

### #57 · WhatsApp aanvraag
**Wanneer:** Als een bedrijf WhatsApp Business aanvraagt  
**Onderwerp:** `WhatsApp-aanvraag: [bedrijfsnaam]`

> **Bedrijf:** [naam] (ID: [id])
> **Telefoonnummer:** [nummer]
>
> → [Ga naar admin panel]

---

### #60 · Wachtlijst aanvraag integratie
**Wanneer:** Als een bedrijf zich aanmeldt voor een komende integratie  
**Onderwerp:** `Wachtlijst: [integratienaam] — [bedrijfsnaam]`

> **Bedrijf:** [naam] (ID: [id])
> **Integratie:** [naam]
>
> → [Ga naar admin panel]

---

## Samenvatting

| # | Template | Actief? |
|---|---|---|
| 2 | Welkomstmail | ✅ |
| 4 | Wachtwoord gewijzigd | ✅ |
| 6 | Onboarding gestart | ✅ |
| 7 | Onboarding herinnering | ✅ |
| 8 | Setup voltooid | ✅ |
| 9 | Setup foutmelding | ✅ |
| 10 | Abonnement gestart | ✅ |
| 11 | Abonnement geüpgraded | ✅ |
| 12 | Abonnement gedowngraded | ✅ |
| 13 | Abonnement opgezegd | ✅ |
| 18 | Account beperkt (openstaande factuur) | ✅ |
| 21 | Weekrapport | ⏳ Nog niet getriggerd |
| 22 | Maandrapport | ⏳ Nog niet getriggerd |
| 23 | Inactief 7 dagen | ⏳ Nog niet getriggerd |
| 24 | Inactief 14 dagen | ⏳ Nog niet getriggerd |
| 25 | Inactief 30 dagen | ⏳ Nog niet getriggerd |
| 26 | Re-activatie | ⏳ Nog niet getriggerd |
| 33 | Feedbackverzoek | ⏳ Nog niet getriggerd |
| 34 | Nieuwe inlog gedetecteerd | ⏳ Nog niet getriggerd |
| 36 | Account vergrendeld | ⏳ Nog niet getriggerd |
| 43 | Win-back dag 1 | ⏳ Nog niet getriggerd |
| 44 | Win-back dag 7 | ⏳ Nog niet getriggerd |
| 45 | Win-back dag 30 | ⏳ Nog niet getriggerd |
| 48 | Team lid verwijderd | ✅ |
| 50 | Integratie gekoppeld | ✅ |
| 51 | Integratie fout | ⏳ Nog niet getriggerd |
| 52 | Integratie verbroken | ⏳ Nog niet getriggerd |
| 53 | Referral uitnodiging verstuurd | ✅ |
| 54 | Referral succesvol | ✅ |
| 55 | Referral beloning toegekend | ✅ |
| 56 | WhatsApp geactiveerd | ✅ |
| 57 | WhatsApp aanvraag (admin) | ✅ |
| 58 | Wachtlijst bevestiging (klant) | ✅ |
| 59 | Integratie nu live (klant) | ✅ |
| 60 | Wachtlijst aanvraag (admin) | ✅ |

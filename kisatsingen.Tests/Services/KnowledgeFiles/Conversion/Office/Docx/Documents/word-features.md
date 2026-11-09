# Word → Markdown: testdokument med rariteter

*Dette dokumentet samler formateringer og elementer fra Word som har en rimelig motpart i Markdown (CommonMark/GFM/Pandoc). Hver seksjon viser elementet og en kort merknad om forventet Markdown.*

# Innholdsfortegnelse

**Merknad:** Feltkode (TOC). Oppdateres i Word med F9. Konvertere hopper ofte over den eller lager en lenkeliste.

# 1. Overskrifter på alle nivåer

## Overskrift 2

### Overskrift 3

#### Overskrift 4

##### Overskrift 5

###### Overskrift 6

**Merknad:** Forventet: # til ######. Tittel-stilen øverst blir ofte # eller metadata (title).

# 2. Tegnformatering

**Fet**, *kursiv*, ***fet og kursiv***, understreket, ~~gjennomstreket~~, ~~dobbel gjennomstreking~~.

Hevet skrift: x<sup>2</sup> og 1<sup>st</sup>. Senket skrift: H<sub>2</sub>O og CO<sub>2</sub>.

Kapitéler, STORE BOKSTAVER VIA FORMAT, uthevet med markeringsfarge, rød tekst, annen skrifttype (Georgia), stor tekst.

Delvis **fet**midt*i*ordet og **fet med mellomrom rundt** (vanskelig for konvertere).

Kode i linje: `console.log("hei")` og `` git commit -m `melding` `` (inneholder backticks).

**Merknad:** Forventet: \*\*fet\*\*, \*kursiv\*, \~\~gjennomstreket\~\~, ^hevet^, \~senket\~, \[tekst\]{.underline}/\[tekst\]{.smallcaps}/\[tekst\]{.mark} i Pandoc. Farge, font og størrelse går normalt tapt.

# 3. Avsnitt, linjeskift og justering

Linje én i samme avsnitt  
linje to etter myk linjeskift (Shift+Enter)  
linje tre etter enda et linjeskift

Sentrert avsnitt.

Høyrejustert avsnitt.

Blokkjustert avsnitt som er langt nok til å gå over flere linjer slik at du faktisk ser at ordmellomrommene strekkes ut for å fylle hele linjebredden fra marg til marg.

Innrykket avsnitt (venstre innrykk 1,27 cm) uten å være sitat.

Avsnitt med hengende innrykk: første linje stikker ut til venstre, resten er rykket inn. Dette brukes ofte i litteraturlister.

Tabulator: etter tab og en til

↑ Tomt avsnitt over denne linjen.

**Merknad:** Forventet: myke linjeskift blir backslash eller to mellomrom før linjeskiftet. Justering og innrykk går tapt (eller blir sitatblokk).

# 4. Lister

## Punktliste med nivåer

- Første punkt
- Andre punkt
    - Underpunkt nivå 2
        - Under-underpunkt nivå 3
            - Nivå 4
    - Tilbake på nivå 2
- Punkt med **fet** og `kode`

## Nummerert liste

1. Første
2. Andre
    1. Underpunkt a
    2. Underpunkt b
        1. Romertall i
        2. Romertall ii
3. Tredje

## Nummerering som starter på 5

5. Punkt fem
6. Punkt seks

## Blandet liste (tall med kulepunkter under)

1. Steg én
    - Detalj
    - Detalj
2. Steg to

## Liste med flere avsnitt i ett punkt

1. Punkt med fortsettelse:

   Dette avsnittet hører til punktet over (innrykket, uten nummer).

2. Neste punkt

## Avkrysningsliste

☐ Ikke gjort

☒ Gjort

☑ Også gjort

**Merknad:** Forventet: «-»/«1.» med innrykk. «5.» som start. Avkrysning blir - \[ \] / - \[x\] kun om konverteren tolker tegnene.

# 5. Lenker, bokmerker og kryssreferanser

Ekstern lenke: [Anthropic](https://www.anthropic.com). Lenke der teksten er URL-en: [https://example.com/sti?a=1&b=2](https://example.com/sti?a=1&b=2).

E-postlenke: [post@example.no](mailto:post@example.no). Fet lenke: [**viktig lenke**](https://example.org).

Intern lenke til bokmerke: hopp til seksjon 10.

**Merknad:** Forventet: \[tekst\](url), \<url>, \[tekst\](#anker).

# 6. Fotnoter og sluttnoter

Her er en påstand med fotnote[^1] og en til[^2]. Og her en sluttnote[^e1].

**Merknad:** Forventet: \[^1\] med definisjon nederst. Sluttnoter blir som regel også fotnoter.

# 7. Sitater og kodeblokker

> *Dette er et sitat med stilen «Quote». Det kan inneholde **fet** tekst.*

> ***Sterkt sitat (Intense Quote).***

Kodeblokk (avsnittsstil «Source Code», monospace, grå bakgrunn):

```
function hilsen(navn) {
  // kommentar med *stjerner* og _understrek_
  return `Hei, ${navn}!`;
}
```

**Merknad:** Forventet: «> » for sitat og \`\`\` for kode. Word har ingen ekte kodeblokk; Pandoc gjenkjenner stilene «Source Code» og «Verbatim Char», andre konvertere ser bare monospace-tekst.

# 8. Tabeller

## Enkel tabell med overskriftsrad og justering

| **Venstre** | **Midt** | **Høyre (tall)** | **Formatert** |
| --- | --- | --- | --- |
| Epler | rød | 12,50 | **fet** + *kursiv* |
| Pærer | grønn | 1 234,00 | [lenke](https://example.com) |
| Tegn \| rør | kode | -5 | Linje én<br>Linje to i samme celle |

**Merknad:** Forventet: GFM-pipetabell med :---: for justering. Celle med to avsnitt eller «|» tvinger ofte grid table/HTML.

## Tabell med flettede celler

| **Flettet over to kolonner** |  | C1 |
| --- | --- | --- |
| Flettet over to rader | B2 | C2 |
|  | B3 | C3 |

**Merknad:** Forventet: kan ikke uttrykkes i ren Markdown – blir HTML-tabell eller mister flettingen.

## Tabell uten overskriftsrad og med nøstet liste

| Celle med liste: | a<br>b |
| --- | --- |

# 9. Bilder

![Gul sirkel og hvit boks på blå bakgrunn]()

*Figur 1: Bildetekst med stilen «Caption».*

Bilde i linje med tekst: ![Lite ikon]() fortsetter her.

**Merknad:** Forventet: !\[alt\](media/image1.png). Alt-tekst hentes fra «description».

# 10. Skillelinjer og sideskift (bokmerket mål)

Under kommer en horisontal linje laget med avsnittskantlinje:

Og en laget av tegn (\*\*\*), slik Word gjør med Autoformat:

\*\*\*

**Merknad:** Forventet: --- for skillelinje. Sideskift (rett under) forsvinner vanligvis eller blir \newpage.

# 11. Ligninger (OMML)

I linje: $E=mc^{2}$.

$x=\frac{-b±\sqrt{b^{2}-4ac}}{2a}$

$\sum_{i=1}^{n} x_{i}$

**Merknad:** Forventet: \$E=mc^{2}\$ og \$\$\frac{...}{...}\$\$ (Pandoc gjør OMML om til LaTeX).

# 12. Spesialtegn og tegn som må escapes

Markdown-tegn i vanlig tekst: \* stjerne, \_understrek\_, # hash, \`backtick\`, \[klammer\](ikke-lenke), \<vinkel>, \ backslash, | rør, \~tilde\~, 1. ikke en liste, + pluss.

\# Denne linjen starter med hash, men er ikke en overskrift.

\- Denne starter med bindestrek, men er ikke en liste.

Typografi: «sitattegn», “engelske”, ‘enkle’, tankestrek – og lang tankestrek —, ellipse …, grader 20 °C, ©, ®, ™, €, ½, ≠, ≤, →.

Hardt mellomrom:10 000 kr. Myk bindestrek: super­kali­fragilistisk. Hard bindestrek: e‑post. Nullbredde: a​b.

Symbolfont (Wingdings): og emoji: 😀 🇳🇴 ✅

HTML-lignende tekst: \<b>ikke fet\</b> og \&amp;.

**Merknad:** Forventet: \\\*, \\\_, \\# osv. escapes. Hardt mellomrom blir ofte «\&nbsp;» eller vanlig mellomrom. Symbolfont-tegn tapes ofte.

# 13. Kommentarer og sporede endringer

Denne teksten har en kommentar knyttet til seg.

Sporet endring: ny tekst i samme setning.

**Merknad:** Forventet: Pandoc --track-changes=accept|reject|all. Kommentarer blir \[tekst\]{.comment-start} kun med «all».

# 14. Diverse

## Overskrift med \*\*fet\*\* og \`kode\` i seg

## Overskrift med kursiv formatering

**Definisjonsliste (stilene «Definition Term» og «Definition»):**

**Markdown**

Et lettvekts markeringsspråk laget av John Gruber i 2004.

**Pandoc**

Et verktøy for å konvertere mellom dokumentformater.

Falsk overskrift: **Fet tekst i stor skrift som ser ut som en overskrift**

Tekst med skjult tekst-formatering (usynlig i Word).

Sidetall i bunntekst, og dokumentet har topptekst. Disse har ingen Markdown-motpart.

**Siste avsnitt. Slutt.**

[^1]: Første fotnote, helt enkel.

[^2]: Andre fotnote med **fet** tekst og [en lenke](https://example.com).

[^e1]: Dette er en sluttnote (vises bakerst i dokumentet).

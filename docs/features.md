# Feature backlog

Planned features for Vacation Calculator, in the order they should be built. Each one depends on the ones before it.

Status: **Done** = implemented. **Planned** = agreed, ready to implement. **Needs decisions** = open questions below must be answered first.

---

## 1. Half days

**Status:** Done

Register a single day as a half day (0.5) instead of a full day.

- A booked day counts as either 1 or 0.5 days against the balance.
- Balances, used/booked/remaining figures and the status banner show half days (e.g. "12.5 days").
- A booking that spans several days uses full days. Half days are set per day (in practice through the calendar, features 5 and 6).

**Data model change (prerequisite for features 2, 3, 5 and 6):** today an entry is a date range. Clicking single days in a calendar, mixing full and half days, and mixing vacation and Feriefri all work per day, so storage moves to one record per booked day: `Date`, `DayType` (Vacation / Feriefri), `Amount` (1 or 0.5) and an optional description. The "Add vacation" form still accepts a range and creates one record per working day in it.

- No migration of the old range format: no data file exists yet, so nothing needs converting.
- The vacation list groups consecutive days of the same type back into ranges for display.

**Agents:** `domain-logic` (model, calculations), then `wpf-ui` (show decimals, list grouping).

---

## 1b. Layout and theming inspired by Claude Desktop, with light and dark themes

**Status:** Done

Restyle the app with Claude Desktop as the inspiration for layout and look, and support both a light and a dark theme. Inspiration only: no Anthropic or Claude names, logos or icons.

Layout:

- A **left sidebar** with the app name, year navigation, settings (allowance, carry-over) and public holidays. A theme switch sits at the bottom of the sidebar.
- A **main content area** with the summary figures, the status banner and the vacation list. The full-year calendar (feature 5) will later become the main content here.
- Generous whitespace, rounded corners, thin subtle borders instead of heavy boxes, and calm typography with clear size steps for headings, labels and numbers.

Theming:

- Warm neutral backgrounds (off-white/cream in light, warm charcoal in dark) instead of pure white/black, with a single warm terracotta accent colour for primary buttons, the selected state and key numbers.
- Status colours (all planned, days to plan, days will be lost, over-booked, expired) have light and dark variants with enough contrast in both.
- All colours are defined once as theme resources; the existing hard-coded colours in `MainWindow.xaml` move into them.
- Every control is themed in both modes, including DataGrid, DatePicker (and its calendar popup), CheckBox, TextBox, ListBox, buttons, scrollbars and tooltips. The window title bar follows the theme.
- **Theme choice: Light / Dark / System**, default System (follows the Windows app theme and updates if Windows switches while the app is running). The choice is remembered between sessions in a small preferences file next to the data file, separate from the vacation data.

**Agents:** `wpf-ui`.

---

## 2. Vacation follows the Danish Holiday Act (ferieloven)

**Status:** Done

Replace the current model (a fixed allowance per calendar year plus manual carry-over) with the rules of ferieloven (samtidighedsferie, in force since 1 September 2020).

Rules the app implements:

- **Accrual:** 2.08 days per month of employment, 25 days for a full holiday year. To make a full year exactly 25, the app accrues 25/12 per month.
- **Holiday year (ferieår):** 1 September to 31 August. Days are earned during the holiday year.
- **When days can be used:** days earned in a month can be used from the first day of the following month.
- **Deadline:** each holiday year's days must be used during its holiday-taking period (ferieafholdelsesperiode), from 1 September at the start of the holiday year until **31 December the following year** (16 months). Days earned 1 Sep 2025 to 31 Aug 2026 must be used by 31 Dec 2026.
- **Two pots at once:** from September to December, two holiday years are open at the same time (the old one about to expire and the new one being earned).
- **What happens to unused days at the deadline:**
  - Days beyond 4 weeks (the 5th week, up to 5 days) are paid out automatically, unless they are transferred to the next holiday year by agreement made no later than 31 December.
  - Unused days within the first 4 weeks (20 days) are lost, unless there was a holiday obstacle (feriehindring, e.g. illness or parental leave).
- **Advance holiday (ferie på forskud):** by agreement, days can be taken before they are earned, but never more than can be earned by 31 August of that holiday year.

Settings:

- **"Employed since"** (date, optional). Accrual starts on this date; a partial first month accrues in proportion to the days employed. Empty means employed since before the holiday years being tracked.
- **Transfer and payout of unused days:** replaced by feature 2b (calculated, not registered).

What the app shows:

- For each open holiday year: earned so far, used, booked ahead, remaining, and its deadline.
- The status banner warns about the holiday year whose deadline is 31 December of the selected calendar year. It splits unused days into **days that will be lost** (within the first 20) and **days that will be paid out** (the 5th week, unless transferred).
- **Booking beyond earned days is allowed.** The app never blocks a vacation booking because too few days have been earned. Instead it shows clearly that the number has been exceeded: the remaining figure goes negative and is highlighted, and the banner states by how many days the earned balance is exceeded. It also says so when bookings exceed what can be earned by 31 August of the holiday year. (Feriefri is different: it can't be booked beyond its allowance, see feature 3.)

Decisions:

- **Order of use:** when two holiday years are open (September to December), booked days use the oldest holiday year first. This always applies: booking more days in September–December uses the older year's days first, which reduces what is left to transfer from it.
- **Holiday obstacle:** transferring days from the first 4 weeks because of a holiday obstacle (illness, parental leave) is out of scope for now.
- **Existing settings:** the per-calendar-year "Days per year" and "Carried over from last year" fields are removed. No migration is needed.

The app is a planning aid, not legal advice. Collective agreements (overenskomster) can change some of these rules, e.g. by forbidding transfers or limiting them to 10 days.

When implemented, the "Domain rules" section of `CLAUDE.md` must be updated to match.

**Agents:** `domain-logic` (holiday-year model, accrual, deadline and payout calculations), then `wpf-ui` (settings, per-holiday-year summary, banner text).

---

## 2b. Calculated transfer and payout of unused days

**Status:** Done

Changes feature 2: 5th-week transfers are no longer registered per holiday year. The app only knows how many days can be transferred (per holiday year; 0 = no transfer), and calculates what happens to each holiday year's unused days at its deadline: transferred, paid out or lost.

Settings (in the sidebar):

- ~~"Unused days can be transferred to the next holiday year" (on/off)~~ — removed (user decision): a transfer limit of 0 means no transfer, so the switch isn't needed.
- Per holiday year (configurable year to year, for the holiday year shown):
  - **"Days that can be transferred"** (number, default **5**, as in the Holiday Act; **0 = no transfer**). It can be set higher when the employee has a personal agreement in their contract. Always shown. With the default 5, unused 5th-week days are transferred unless the year is set to 0.
  - **"Unused days paid out instead of lost"**: **None** (default), **Some** (a number of days) or **All**. This only applies to unused days beyond the 5th week (see below); it reflects the employee's contract.
  - A holiday year without its own values uses the previous holiday year's values; if there are none, the defaults (5 days, none paid out).

**Opening balance:** transfers are only calculated from a starting point the user sets, never assumed for earlier years:

- **"First holiday year tracked"** and **"Days transferred into it"** (the opening balance, default 0), in the Employment settings. The default first year is the oldest holiday year open when the data is first saved (e.g. 2025/26 on 24 Oct 2026); it is stored then, so it doesn't move as time passes. The user can change it.
- The first tracked holiday year's transferred-in days are the opening balance. Each later year's transferred-in days are calculated from the year before.
- Holiday years before the first tracked year transfer nothing and get no transfer calculation; bookings in them still count as used vacation for those years.

Days transferred into a holiday year are used first within that year (they are the oldest days). They must be used in that holiday year: they can never be transferred again (vacation days are never transferred more than one year; days from 2025 can never reach 2027), and if unused at its deadline they are lost.

Calculation at each holiday year's deadline (31 December of the following year), from the days still unused after all bookings (used and booked ahead):

1. **Transferred-in days still unused:** lost.
2. **Transferred:** the year's own unused days up to the transfer limit: 5th-week days first (the year's own earnings beyond 20, at most 5), then other own unused days only through the part of the limit above 5 (a personal agreement). With the default limit 5 only 5th-week days move, as in the Holiday Act; limit 8 can also move up to 3 days from the first 20. They are added to the next holiday year's total.
3. **5th-week days not transferred:** always paid out, as the Holiday Act says.
4. **Other unused days** (beyond the 5th week): paid out up to the payout setting (none, some N, or all); the rest are lost.

The banner and the holiday-years card show the projected split for the holiday year whose deadline is in the selected calendar year (e.g. "If they are not booked, 5 days will be transferred to 2026/27 and 3 days will be lost"), and after the deadline what happened.

Removed: the per-holiday-year "Days transferred to next holiday year" input and the stored `TransferredToNext` values (old values in the data file are ignored). Unused 5th-week days that aren't transferred are always paid out (Holiday Act); the payout setting only covers unused days beyond the 5th week, whose treatment depends on the employee's contract.

**Agents:** `domain-logic` (settings model, transfer/payout/lost calculation), then `wpf-ui` (sidebar settings, banner and card text).

---

## 2c. Remove the transfer switch

**Status:** Done

Remove the employment-wide "Unused days can be transferred" switch (`VacationData.TransferAllowed`). The per-holiday-year "Days that can be transferred" (default 5, inherited) alone decides: 0 = no transfer. Old data files with the switch still load (the value is ignored). See section 2b.

**Agents:** `domain-logic`, then `wpf-ui`.

---

## 3. Feriefri days

**Status:** Done

A second type of day off (feriefridage) with its own allowance, separate from regular vacation. The rules match the previous web version of the app (`vacationcalculator.html`).

Rules:

- **Period:** Feriefri follows the same holiday year as vacation (1 September to 31 August). A booked Feriefri day counts against the holiday year its date falls in.
- **Allowance:** the full allowance is available from the start of the holiday year; it is not earned month by month. Default 5 days, configurable per holiday year (0 to 20).
- **Partial years:** no automatic pro-rating. If employment starts or ends mid-year, the user sets that holiday year's allowance by hand.
- **Using the previous holiday year's days:** unused Feriefri days from the previous holiday year can still be used until 31 December, like vacation. After 31 December they are gone.
- **Payout on 31 December:** the unused Feriefri days still open on 31 December (previous holiday year's leftover plus the current holiday year's remaining) may not exceed 5. Anything above 5 must be used before 31 December or is paid out in January. The banner warns about these days.
- **Half days:** a Feriefri day can be a full or a half day.
- **One type per day:** a day is either vacation or Feriefri. Booking one type on a day removes the other.
- **Can't exceed the allowance:** booking a full day that would go over the holiday year's allowance books a half day instead if that fits (with a message); otherwise it is refused ("All 5 Feriefri days are used").
- **No order of use:** the app doesn't prefer Feriefri over vacation or the reverse.
- **Free working days:** Feriefri is not part of the check for whether there are enough free working days left; that check covers vacation only.
- Has its own balance per holiday year: allowance / used / booked / remaining, shown separately from vacation in the summary, the list and the calendar.

Details decided for the implementation:

- **Allowance per holiday year** inherits like the other per-year settings: a holiday year without its own value uses the nearest earlier year's, otherwise the default 5.
- **Order of use:** like vacation, oldest first. In September–December a Feriefri booking uses the previous holiday year's leftover first, then the current year's allowance.
- **31 December of calendar year Y:** the open days are the previous holiday year's leftover plus the current holiday year's remaining. If that is more than 5, the excess (open − 5) is paid out in January, taken from the previous year's leftover first. Whatever is left of the previous year's leftover after that is lost. The current year's remaining days (less any of them paid out) stay usable until its own 31 December.
- **Booking Feriefri** in the form: a type choice (Vacation / Feriefri) next to the dates. A Feriefri range books working days in date order until the allowance is used up; a full day that doesn't fit becomes a half day if 0.5 fits; the rest is not booked and a message says how many days were left out. A single day or half day that doesn't fit is refused with "All N Feriefri days for 2025/26 are used."
- **Banner:** a separate Feriefri line when days will be paid out or lost at 31 December, e.g. "Feriefri: 2 days must be used by 31 December 2026, otherwise they are paid out in January."

**Agents:** `domain-logic` (day type, allowance, balance and payout rules), then `wpf-ui` (settings field, summary, list).

**Agents:** `domain-logic` (day type, allowance, balance rules), then `wpf-ui` (settings field, summary, list).

---

## 4. Danish public holidays

**Status:** Done

Danish public holidays (helligdage) are calculated automatically for every year, marked in the calendar (feature 5), and can't be booked as vacation or Feriefri. They don't count as working days, so they never use up vacation or Feriefri.

Holidays, matching the previous web version:

| Holiday | Date |
|---|---|
| Nytårsdag (New Year's Day) | 1 January |
| Skærtorsdag (Maundy Thursday) | Easter − 3 days |
| Langfredag (Good Friday) | Easter − 2 days |
| Påskedag (Easter Sunday) | Easter |
| 2. påskedag (Easter Monday) | Easter + 1 day |
| Kristi himmelfartsdag (Ascension Day) | Easter + 39 days |
| Pinsedag (Whit Sunday) | Easter + 49 days |
| 2. pinsedag (Whit Monday) | Easter + 50 days |
| Juleaftensdag (Christmas Eve) | 24 December |
| Juledag (Christmas Day) | 25 December |
| 2. juledag (Boxing Day) | 26 December |
| Nytårsaftensdag (New Year's Eve) | 31 December |

- Easter Sunday is calculated with the Gregorian (Western) Easter algorithm.
- Each holiday has its Danish name, shown in the calendar tooltip.
- Clicking, dragging or Shift+clicking over a holiday never books it. Ranges skip it.
- The manual public-holiday list stays, for extra days off such as Grundlovsdag (5 June) or company days. Manual days follow the same rules: marked, not bookable, not working days.
- Existing bookings that fall on a holiday (e.g. a manual holiday added after the day was booked) are not counted and are shown in the calendar as a conflict.

**Agents:** `domain-logic` (holiday calculation with tests for several years' Easter dates), then `wpf-ui` (marking and blocking in the calendar).

---

## 5. Full-year calendar

**Status:** Done

Show the whole selected year as a calendar, and register days by clicking them.

- 12 months in a grid (e.g. 4 × 3), weeks starting on Monday, with ISO week numbers.
- Each day shows its state by colour: free working day, weekend, public holiday, vacation (full or half), Feriefri (full or half), and today.
- Half days are drawn visibly different from full days (e.g. half-filled).
- Past days are still clickable, so taken vacation can be registered after the fact.
- Weekends and public holidays can't be booked; clicking them does nothing.
- Uses the existing year navigation (◀ year ▶). The summary and status banner update immediately after each click.
- Hovering a day shows a tooltip with the date, type and amount.

Layout (user decision): the summary cards, holiday-years card and banner stay at the top of the main area; below them a tab switch **Calendar | List**. Calendar (the default tab) shows the 12 months (4 × 3), scrolling if needed; List holds the existing booking form and vacation list. The public-holiday list stays in the sidebar.

**Collapsible summary** (user request): the stat cards and the holiday-years card (incl. Feriefri lines) can be collapsed with a toggle next to the heading ("Hide summary" / "Show summary"), so the calendar gets more room. The heading and the status banner stay visible. Expanded by default; the choice is remembered between sessions in the preferences file (next to the theme), not in the vacation data.

Also shown in the calendar: bookings that fall on a public holiday (conflicts, see feature 4) are marked so they stand out. Clicking to book is feature 6; in this feature the calendar is display-only.

**Agents:** `wpf-ui` (any calculation the calendar needs goes to `domain-logic` first).

---

## 6. Click to cycle day state, and range booking

**Status:** Done

Clicking the same day repeatedly switches between full and half days. Behaviour matches the previous web version:

- **Left-click** a working day cycles vacation: **free → full day → half day → free**.
- **Right-click** a working day cycles Feriefri the same way: **free → full day → half day → free**, within the Feriefri allowance rules (feature 3).
- Clicking a day booked with the other type replaces it (one type per day).

Booking a range in the calendar:

- **Drag** across days with the left button to book them as full vacation days, or with the right button for Feriefri.
- **Shift+click** books every day from the last clicked day to the clicked day, with the type of the button used.
- Weekends and public holidays in the range are skipped. Days already booked in the range are set to the new type as full days.
- Feriefri ranges stop at the allowance: days that don't fit are not booked, and a message says how many were left out.
- Days in a range that are already booked exactly as the range would book them are left as they are (description kept) and not counted; a range that changes nothing saves nothing.

Details agreed during implementation:

- A press and release on the same day is a click; only a real drag books a range. Escape or releasing outside a day cancels a drag. Shift+click without an earlier click acts as a plain click; the last clicked day is forgotten when the year changes.
- Going from full to half keeps the day's description; new calendar bookings have none. Clicking a weekend or holiday does nothing, even if an old booking sits on it.
- Messages use the booking form's wording. In the Calendar tab they sit below the calendar, and the banner keeps its height, so days don't move under the pointer between clicks.

**Agents:** `domain-logic` (cycle and range logic as tested functions), then `wpf-ui` (mouse handling).

---

## 7. Translations

**Status:** Done

The interface is available in four languages: **English**, **Danish** (Dansk), **Romanian** (Română) and **Redneck** (a full-on, good-natured American Southern dialect of English, a novelty option).

- **Language picker** in the sidebar next to Theme: System / English / Dansk / Română / Redneck (each language named in itself). **System** (the default) follows the Windows display language: Danish → Danish, Romanian → Romanian, anything else → English. Redneck is only used when picked. The choice is stored in the preferences file next to the theme, not in the vacation data.
- **Switching applies immediately** to everything on screen: labels, buttons, tooltips, headings, the summary, the banner, messages, the legend and hint line, calendar month and weekday headers, public holiday names, the List tab's form, column headers and type badges, and validation and error texts. A message showing at the moment of switching may simply be cleared.
- The app name **"Vacation Calculator" is never translated**.
- **Dates inside text** follow the language: English and Redneck "31 December 2026" / "Thursday 24 December 2026", Danish "31. december 2026" / "torsdag den 24. december 2026", Romanian "31 decembrie 2026" / "joi, 24 decembrie 2026". Inputs, numbers and table date columns keep following the Windows regional format. Holiday-year labels stay "2025/26".
- **Terms:** "Feriefri" stays as a name in English, Romanian and Redneck. Danish uses the natural Danish wording (ferie, feriefridage, ferieår, 5. ferieuge, overførsel, udbetaling, bortfalder). Public holiday names are translated: Danish keeps the real names (Nytårsdag, Skærtorsdag, …), the other languages use their own equivalents (e.g. "Maundy Thursday", "Ascension Day", "Whit Monday").
- **Plurals and numbers** are correct per language: "1 day" / "2 days" / "0,5 days"; Romanian "1 zi", "2 zile", "20 de zile", "2,5 zile".
- **Redneck** is full-on dialect, but every number, date, day type and consequence (transferred, paid out, lost) stays exact and unambiguous.
- **Layout:** every language meets the layout limits (default window fits 1366×768, at least 5 list rows, the sidebar scrolls as one area); longer text wraps rather than clips.
- **Core has no user-facing text:** holiday and month names become keys and numbers that the App translates.
- Translations are written by the agents. The Danish text is reviewed by the user; the Romanian text should ideally be checked by a native speaker.

**Agents:** `domain-logic` (holiday kinds and month numbers instead of names), then `wpf-ui` (string resources, language picker, all text, dates), `tester` (every key and placeholder present in all languages, screenshots of all four languages in both themes).

---

## 8. Simplified summary table

**Status:** Done

The holiday-years card in the summary becomes one table with a row per holiday year shown (the same years as today) and these columns, grouped under two headers:

| | Vacation days | | | | | Feriefri | | | | |
|---|---|---|---|---|---|---|---|---|---|---|
| Year | Transferred in | Earned so far | Used | Booked | Remaining | Allotted | Used | Booked | Remaining | Total remaining |

- **Used** = days on or before today; **Booked** = days after today (for both types, as everywhere else).
- Vacation **Remaining** is today's figure (a projected transfer-in doesn't count until the previous deadline; after a year's own deadline it is 0, because what was left has been transferred, paid out or lost, and only an over-booking stays negative). Feriefri **Remaining** is today's figure too. **Total remaining** = vacation remaining + Feriefri remaining.
- **Allotted** is the Feriefri allowance and stays editable per holiday year in its cell (Enter or Tab saves, as today), with the inherited/own information and a way to reset it.
- The separate Feriefri rows, the "To next year" and "Use by" columns and all notes in the table are removed. What the notes said (e.g. "advance holiday: 2,42 not earned yet", a projected transfer, "5 will be paid out in January 2028", an allowance that is exceeded) becomes a tooltip on the cell it concerns; the banner under the summary already explains the current year in full sentences. A cell that needs attention (e.g. negative remaining) may still be highlighted.
- The stat cards above the table don't change.
- All four languages; fits the layout limits (900 px minimum width, 1366×768 default, at least 5 list rows).

**Agents:** `wpf-ui` (display only; no Core change).

---

## 9. Release packaging (v1.0.0)

**Status:** In progress

- The release is a **portable zip**, no installer: `VacationCalculator-<version>-win-x64.zip` containing a self-contained, single-file `VacationCalculator.exe` for Windows 10/11 x64 that runs from any folder without installing .NET. No trimming (WPF doesn't support it).
- The version lives in the App project (`Version`, starting at 1.0.0) together with the product name "Vacation Calculator" and the other file properties shown in Windows (Explorer → Properties → Details).
- Data stays where it always is (`%APPDATA%\VacationCalculator`, or `--data <path>`), so moving or replacing the exe never touches it, and a newer version reads the same file.
- `build/package.ps1` builds the zip: it runs the tests first and stops if they fail, publishes the App with the release settings and zips the result into `artifacts/` (ignored by git).
- Release: a GitHub Release tagged `v1.0.0` on https://github.com/thomaslindgaard/VacationCalculator with the zip attached and short release notes. The main session confirms with the user before publishing it.
- The exe is not code-signed, so Windows SmartScreen may warn on first start ("More info" → "Run anyway").

**Agents:** `wpf-ui` (App project publish settings and version), main session (`build/package.ps1`, `.gitignore`, release), `tester` (runs the packaged exe from the zip), `code-reviewer`.

---

## Later ideas (not agreed)

- Main-holiday check (hovedferie): warn if fewer than 15 consecutive days (3 weeks) are booked between 1 May and 30 September.

## Sources

- [Dansk Industri: Ferie](https://www.danskindustri.dk/vi-radgiver-dig/personale/ferie-og-fridage/ferie/)
- [Azets: Ferieår](https://www.azets.com/da-dk/ressourcer/blog/feriear)
- [Dataløn: Guide til ferie, principper og regler](https://www.dataloen.dk/siteassets/pdf/ferielov/guide-ferie-principper-og-regler.pdf)

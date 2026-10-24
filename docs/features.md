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

## 2. Vacation follows the Danish Holiday Act (ferieloven)

**Status:** Planned

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
- **"5th week can be transferred by agreement with manager"** (on/off, default off). Whether your employment allows transferring unused 5th-week days to the next holiday year.
  - **Off:** unused 5th-week days are paid out at the deadline. The banner says they will be paid out.
  - **On:** for each holiday year you can register how many 5th-week days were transferred (at most the days beyond 20 that are still unused). Transferred days are added to the next holiday year and expire with it. Until a transfer is registered, the banner says unused 5th-week days "can be transferred by agreement before 31 December, otherwise they are paid out".

What the app shows:

- For each open holiday year: earned so far, used, booked ahead, remaining, and its deadline.
- The status banner warns about the holiday year whose deadline is 31 December of the selected calendar year. It splits unused days into **days that will be lost** (within the first 20) and **days that will be paid out** (the 5th week, unless transferred).
- **Booking beyond earned days is allowed.** The app never blocks a vacation booking because too few days have been earned. Instead it shows clearly that the number has been exceeded: the remaining figure goes negative and is highlighted, and the banner states by how many days the earned balance is exceeded. It also says so when bookings exceed what can be earned by 31 August of the holiday year. (Feriefri is different: it can't be booked beyond its allowance, see feature 3.)

Decisions:

- **Order of use:** when two holiday years are open (September to December), booked days use the oldest holiday year first.
- **Holiday obstacle:** transferring days from the first 4 weeks because of a holiday obstacle (illness, parental leave) is out of scope for now.
- **Existing settings:** the per-calendar-year "Days per year" and "Carried over from last year" fields are removed. No migration is needed.

The app is a planning aid, not legal advice. Collective agreements (overenskomster) can change some of these rules, e.g. by forbidding transfers or limiting them to 10 days.

When implemented, the "Domain rules" section of `CLAUDE.md` must be updated to match.

**Agents:** `domain-logic` (holiday-year model, accrual, deadline and payout calculations), then `wpf-ui` (settings, per-holiday-year summary, banner text).

---

## 3. Feriefri days

**Status:** Planned

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

**Agents:** `domain-logic` (day type, allowance, balance and payout rules), then `wpf-ui` (settings field, summary, list).

**Agents:** `domain-logic` (day type, allowance, balance rules), then `wpf-ui` (settings field, summary, list).

---

## 4. Danish public holidays

**Status:** Planned

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

**Status:** Planned (some details proposed below)

Show the whole selected year as a calendar, and register days by clicking them.

- 12 months in a grid (e.g. 4 × 3), weeks starting on Monday, with ISO week numbers.
- Each day shows its state by colour: free working day, weekend, public holiday, vacation (full or half), Feriefri (full or half), and today.
- Half days are drawn visibly different from full days (e.g. half-filled).
- Past days are still clickable, so taken vacation can be registered after the fact.
- Weekends and public holidays can't be booked; clicking them does nothing.
- Uses the existing year navigation (◀ year ▶). The summary and status banner update immediately after each click.
- Hovering a day shows a tooltip with the date, type and amount.

Proposed: the calendar becomes the main view, and the existing vacation list and public holiday list stay beside or below it.

**Agents:** `wpf-ui` (any calculation the calendar needs goes to `domain-logic` first).

---

## 6. Click to cycle day state, and range booking

**Status:** Planned

Clicking the same day repeatedly switches between full and half days. Behaviour matches the previous web version:

- **Left-click** a working day cycles vacation: **free → full day → half day → free**.
- **Right-click** a working day cycles Feriefri the same way: **free → full day → half day → free**, within the Feriefri allowance rules (feature 3).
- Clicking a day booked with the other type replaces it (one type per day).

Booking a range in the calendar:

- **Drag** across days with the left button to book them as full vacation days, or with the right button for Feriefri.
- **Shift+click** books every day from the last clicked day to the clicked day, with the type of the button used.
- Weekends and public holidays in the range are skipped. Days already booked in the range are set to the new type as full days.
- Feriefri ranges stop at the allowance: days that don't fit are not booked, and a message says how many were left out.

**Agents:** `domain-logic` (cycle and range logic as tested functions), then `wpf-ui` (mouse handling).

---

## Later ideas (not agreed)

- Main-holiday check (hovedferie): warn if fewer than 15 consecutive days (3 weeks) are booked between 1 May and 30 September.

## Sources

- [Dansk Industri: Ferie](https://www.danskindustri.dk/vi-radgiver-dig/personale/ferie-og-fridage/ferie/)
- [Azets: Ferieår](https://www.azets.com/da-dk/ressourcer/blog/feriear)
- [Dataløn: Guide til ferie, principper og regler](https://www.dataloen.dk/siteassets/pdf/ferielov/guide-ferie-principper-og-regler.pdf)

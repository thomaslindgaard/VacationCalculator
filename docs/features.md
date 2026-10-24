# Feature backlog

Planned features for Vacation Calculator, in the order they should be built. Each one depends on the ones before it.

Status: **Planned** = agreed, ready to implement. **Needs decisions** = open questions below must be answered first.

---

## 1. Half days

**Status:** Planned

Register a single day as a half day (0.5) instead of a full day.

- A booked day counts as either 1 or 0.5 days against the balance.
- Balances, used/booked/remaining figures and the status banner show half days (e.g. "12.5 days").
- A booking that spans several days uses full days. Half days are set per day (in practice through the calendar, feature 4).

**Data model change (prerequisite for features 2 to 5):** today an entry is a date range. Clicking single days in a calendar, mixing full and half days, and mixing vacation and Feriefri all work per day, so storage moves to one record per booked day: `Date`, `DayType` (Vacation / Feriefri), `Amount` (1 or 0.5) and an optional description. The "Add vacation" form still accepts a range and creates one record per working day in it.

- Existing data files with range entries must load and be converted on first save, without losing anything.
- The vacation list groups consecutive days of the same type back into ranges for display.

**Agents:** `domain-logic` (model, migration, calculations), then `wpf-ui` (show decimals, list grouping).

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
- Warns when bookings go beyond earned days (advance holiday) and when they exceed what can be earned by 31 August.

Decisions:

- **Order of use:** when two holiday years are open (September to December), booked days use the oldest holiday year first.
- **Holiday obstacle:** transferring days from the first 4 weeks because of a holiday obstacle (illness, parental leave) is out of scope for now.
- **Existing settings:** the per-calendar-year "Days per year" and "Carried over from last year" fields are removed. Existing data files still load; those two values are dropped, and all booked days are kept.

The app is a planning aid, not legal advice. Collective agreements (overenskomster) can change some of these rules, e.g. by forbidding transfers or limiting them to 10 days.

When implemented, the "Domain rules" section of `CLAUDE.md` must be updated to match.

**Agents:** `domain-logic` (holiday-year model, accrual, deadline and payout calculations, migration), then `wpf-ui` (settings, per-holiday-year summary, banner text).

---

## 3. Feriefri days

**Status:** Needs decisions

A second type of day off with its own yearly allowance, separate from regular vacation.

Agreed so far:

- **Period:** the allowance follows the calendar year (1 January to 31 December).
- Default allowance is 5 days per year, configurable per calendar year.
- **Partial years:** no automatic pro-rating. If employment starts or ends mid-year, the user sets that year's allowance by hand.
- Has its own balance: total / used / booked / remaining.
- Can be booked as full or half days.
- Shown separately from vacation in the summary, the list and the calendar.

Open questions:

1. **Expiry:** do unused Feriefri days expire on 31 December, carry over, or get paid out? Should the app warn about them the same way it warns about vacation?
2. **Carry-over:** should Feriefri have a "carried over from last year" field?
3. **Order of use:** does it matter which is used first? For example, should the app suggest using Feriefri before regular vacation?
4. **Deadline warning:** when working out whether days will be lost, should vacation and Feriefri compete for the same free working days left in the year? (Proposed: yes, combined.)

**Agents:** `domain-logic` (day type, allowance, balance rules), then `wpf-ui` (settings field, summary, list).

---

## 4. Full-year calendar

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

## 5. Click to cycle day state

**Status:** Needs decisions

Clicking the same day repeatedly switches between full and half days.

Proposed behaviour:

- A selector above the calendar picks what clicks register: **Vacation** or **Feriefri**.
- Clicking a free working day cycles: **free → full day → half day → free**.
- Clicking a day already booked with the *other* type switches it to the selected type as a full day.

Open questions:

1. Is the cycle order right (full first, then half, then cleared)? Or should it be half → full → free?
2. Is a type selector acceptable, or should the cycle go through both types instead: free → vacation → vacation ½ → Feriefri → Feriefri ½ → free?
3. Should there be a way to book a range in the calendar (e.g. click and drag, or Shift+click), or is the range form enough?

**Agents:** `domain-logic` (cycle logic as a tested function), then `wpf-ui` (click handling).

---

## Later ideas (not agreed)

- Main-holiday check (hovedferie): warn if fewer than 15 consecutive days (3 weeks) are booked between 1 May and 30 September.
- Mark public holidays by right-clicking a day in the calendar instead of using the date picker.
- Option to pre-fill Danish public holidays (helligdage).

## Sources

- [Dansk Industri: Ferie](https://www.danskindustri.dk/vi-radgiver-dig/personale/ferie-og-fridage/ferie/)
- [Azets: Ferieår](https://www.azets.com/da-dk/ressourcer/blog/feriear)
- [Dataløn: Guide til ferie, principper og regler](https://www.dataloen.dk/siteassets/pdf/ferielov/guide-ferie-principper-og-regler.pdf)

# Feature backlog

Planned features for Vacation Calculator, in the order they should be built. Each one depends on the ones before it.

Status: **Planned** = agreed, ready to implement. **Needs decisions** = open questions below must be answered first.

---

## 1. Half days

**Status:** Planned

Register a single day as a half day (0.5) instead of a full day.

- A booked day counts as either 1 or 0.5 days against the balance.
- Balances, used/booked/remaining figures and the status banner show half days (e.g. "12.5 days").
- A booking that spans several days uses full days. Half days are set per day (in practice through the calendar, feature 3).

**Data model change (prerequisite for features 2 to 4):** today an entry is a date range. Clicking single days in a calendar, mixing full and half days, and mixing vacation and Feriefri all work per day, so storage moves to one record per booked day: `Date`, `DayType` (Vacation / Feriefri), `Amount` (1 or 0.5) and an optional description. The "Add vacation" form still accepts a range and creates one record per working day in it.

- Existing data files with range entries must load and be converted on first save, without losing anything.
- The vacation list groups consecutive days of the same type back into ranges for display.

**Agents:** `domain-logic` (model, migration, calculations), then `wpf-ui` (show decimals, list grouping).

---

## 2. Feriefri days

**Status:** Needs decisions

A second type of day off with its own yearly allowance, separate from regular vacation.

Agreed so far:

- Default allowance is 5 days per year, configurable per year (like the vacation allowance).
- Has its own balance: total / used / booked / remaining.
- Can be booked as full or half days.
- Shown separately from vacation in the summary, the list and the calendar.

Open questions:

1. **Period:** does the Feriefri allowance follow the calendar year (1 January to 31 December) like vacation, or another period, e.g. 1 May to 30 April or 1 September to 31 August?
2. **Expiry:** do unused Feriefri days expire at the end of the period, can they be carried over, or are they paid out? Should the app warn about them the same way it warns about vacation?
3. **Carry-over:** should Feriefri have a "carried over from last year" field like vacation?
4. **Pro-rating:** if employment starts or ends mid-year, is the allowance reduced (e.g. 5 × months/12), or always the full number?
5. **Order of use:** does it matter which is used first? For example, should the app suggest using Feriefri before regular vacation?
6. **Deadline warning:** when working out whether days will be lost, should vacation and Feriefri compete for the same free working days left in the year? (Proposed: yes, combined.)

**Agents:** `domain-logic` (day type, allowance, balance rules), then `wpf-ui` (settings field, summary, list).

---

## 3. Full-year calendar

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

## 4. Click to cycle day state

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

- Mark public holidays by right-clicking a day in the calendar instead of using the date picker.
- Option to pre-fill a country's public holidays (e.g. Danish helligdage).

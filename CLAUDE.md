# Vacation Calculator

WPF desktop app (.NET 9, C#) that tracks vacation days under the Danish Holiday Act and warns when unused days must be taken before their 31 December deadline. It builds `VacationCalculator.exe`; in user-facing text the name is always written "Vacation Calculator".

## Commands

```
dotnet build VacationCalculator.sln
dotnet test
dotnet run --project src/VacationCalculator.App
```

Run `dotnet build` and `dotnet test` before reporting any change as done.

## Agent workflow (main session)

This section is for the main session. If you are running as one of the agents below, ignore it and do only your own task.

The main session never writes or changes app code (`src/`, `tests/`) itself. It plans, delegates to the agents in `.claude/agents/`, verifies, and commits. This applies to every change, however small.

### Agents

| Agent | Model | Owns | Does |
|---|---|---|---|
| `domain-logic` | opus | `src/VacationCalculator.Core`, its tests | Rules, calculations, models, persistence. Test-first. |
| `wpf-ui` | sonnet | `src/VacationCalculator.App` | XAML, themes, view models, bindings, messages. Checks the app at runtime with `--data`. Doesn't edit tests. |
| `tester` | sonnet | independent tests under `tests/`, and updating App tests after agreed changes | Independent edge-case and regression tests; PASS/FAIL verdict. Never edits `src/`. |
| `code-reviewer` | opus | nothing (read-only) | Reviews the full diff for bugs and data-loss risks. |

If the Agent tool says one of these agents doesn't exist, the session started before the agent files were added or changed (Claude Code loads `.claude/agents/` at startup). Stop and ask the user to restart Claude Code; don't substitute a general-purpose agent.

### Steps for every change

1. **Plan.** Restate the request, decide which agents are involved, and ask the user about anything genuinely ambiguous before starting.
2. **Implement.** A Core-only change goes to `domain-logic`, a UI-only change to `wpf-ui`. A feature that touches both: `domain-logic` first, then `wpf-ui`, passing along the Core API that `domain-logic` reported.
3. **Test.** Send `tester` the user's original request (not just the implementer's summary) and which agent made the change.
4. **Review.** Send `code-reviewer` the request and a summary of what changed.
5. **Fix loop.** If `tester` reports FAIL, `code-reviewer` reports a real problem, or a report is wrong or incomplete, send the specifics back to the owning implementation agent and repeat from step 3. Do not fix it in the main session.
6. **Verify and ship.** Run `dotnet build VacationCalculator.sln` and `dotnet test` yourself, then commit and push (see Git). Tell the user what changed and what was verified.
7. **Update the progress page** whenever a backlog feature's status or progress changes: when it starts, when an implementation step (e.g. the Core part) is done, and after the commit that finishes it. Never only edit `docs/progress.json` without publishing it to the page. The page is the Dashboard artifact https://claude.ai/artifact/Ap7ocvkS2ZMjKbNXwKN1nA; its data is `docs/progress.json`.
   - Edit `docs/progress.json`: the feature's `status` (Planned / In progress / Done), `progress` (0–1), `runsDone` (agent runs completed for the feature, counting every implementation, test, review and fix run), `runsLow`/`runsHigh` (estimated agent runs left; 0 when done) and `commit` (short hash of the feature commit). Add a row for any new backlog feature, with a size (S = 1, M = 2, L = 3 points).
   - Upload it to the artifact as an asset, update the `datasets/features` document's `source.url` and `updated` (read the Dashboard type's instructions before writing), and delete the previous asset. `updated.at` must be the real current UTC time (e.g. from `[DateTime]::UtcNow`), never an invented one.
   - Commit and push `docs/progress.json` on its own ("Update progress page").

### Writing a brief

Each agent only knows what its brief says (plus this file). Include the user's request in their words, the goal and acceptance criteria, relevant files, decisions already made, and what earlier agents reported (e.g. new Core API, failing test names).

### Done directly by the main session

Only work outside every agent's scope: answering questions, git, `CLAUDE.md`, `.claude/` (agent definitions, settings), `docs/`, the solution file and `.gitignore`.

### Keep CLAUDE.md and the agent files in line

The agent definitions in `.claude/agents/` repeat the conventions each agent needs. Whenever a rule or convention in this file changes (or an agent's scope or workflow changes), update the affected agent files in the same commit, and vice versa. Agents read both, and CLAUDE.md wins if they ever disagree. Agent file changes take effect in the next Claude Code session; until then, put new rules in the brief.

## Git

Every completed update to the app is committed and pushed to GitHub (`origin`, https://github.com/thomaslindgaard/VacationCalculator) without asking first:

- Commit once the change builds and tests pass, with a message describing what changed and why. Then `git push`.
- One commit per logical change; don't bundle unrelated work.
- Never commit or push a failing build or failing tests. Report the failure instead.
- Subagents (`domain-logic`, `wpf-ui`, `tester`, `code-reviewer`) don't commit. The main session commits after the work, and any review, is finished.
- If the push fails (e.g. auth or a diverged remote), keep the local commit, report the error, and don't force-push.

## Layout

- `src/VacationCalculator.Core` (net9.0), no UI dependencies:
  - `Models.cs` (`VacationData`, `BookedDay`, per-year settings and their inheritance, `DeadlineSettings.Split`), `HolidayYear.cs` (`HolidayYear`, `Accrual`)
  - `BalanceCalculator` (vacation allocation, balances, calendar-year status), `FeriefriCalculator`, `DanishHolidays`, `CalendarYear` (per-day state of a year for the calendar view, ISO weeks)
  - `Bookings` (booking, removing and grouping days; range cap), `DataStore` (JSON persistence, temp file then move)
- `src/VacationCalculator.App` (net9.0-windows, WPF):
  - `MainWindow.xaml` (sidebar + main content), `ViewModels/` (hand-rolled MVVM: `ObservableObject`, `RelayCommand`, `MainViewModel` with `Apply` for every save)
  - Main area: summary (collapsible) and banner on top, then Calendar | List tabs; `ViewModels/CalendarViews.cs` (month/day view objects, updated in place), `Controls/AdaptiveUniformGrid.cs`
  - `Themes/` (`LightTheme.xaml` / `DarkTheme.xaml` palettes with identical keys, `Controls.xaml` control styles), `Behaviors/` (attached behaviours, e.g. `CommitOnEnter`)
  - `Services/`: `ThemeService` (Light/Dark/System and the summary's collapsed state, stored in `vacation-calculator-preferences.json` next to the data file), `DateText` (English dates in text), `DataPathResolver` (`--data` option)
- `tests/VacationCalculator.Core.Tests` and `tests/VacationCalculator.App.Tests` (xUnit). Files named `*Independent*` and most App tests are written by `tester`.

## Domain rules

Vacation follows the Danish Holiday Act (ferieloven, samtidighedsferie); full spec in `docs/features.md` section 2. The app is a planning aid, not legal advice.

- **Holiday year** (`HolidayYear`, by start year): 1 Sep – 31 Aug. Its days must be used by **31 December of the following year**. Sep–Dec two holiday years are open; Jan–Aug one.
- **Accrual** (`Accrual`): 25/12 days per month of employment, a full year exactly 25, rounded once to 2 decimals. Days earned in a month are usable from the 1st of the next month. `VacationData.EmployedSince` starts accrual; a partial first month is pro-rated by days.
- **Allocation**: booked Vacation days go to the oldest open holiday year first, splitting a day when only 0.5 is left; over-booking goes to the newest open year. Booking beyond earned days (advance holiday) is never blocked, only shown.
- **Within a holiday year**, bookings use transferred-in days first, then the year's own days (first 20 before the 5th week).
- **At the deadline** (calculated, never registered; `DeadlineSettings.Split`): (1) unused transferred-in days are lost (days are never transferred more than one year); (2) own unused 5th-week days up to that year's transfer limit (0 = no transfer) move to the next holiday year, and other own unused days only through the part of the limit above 5 (personal agreement); (3) own 5th-week days not transferred are always paid out; (4) other own unused days are paid out up to that year's payout setting (`PayoutMode` None/Some/All), the rest lost.
- **Opening balance**: transfers are calculated only from the first tracked holiday year (stored on first save; default the oldest holiday year open then), which receives the opening balance as its transferred-in days. Earlier years transfer nothing.
- **Per-holiday-year settings** (`VacationData.GetDeadlineSettings`): transfer limit (default 5, may be higher by personal agreement) and payout setting (default None); a year without its own value inherits the nearest earlier year's, otherwise the default.
- **Feriefri** (`FeriefriCalculator`, feature 3): a separate allowance per holiday year (default 5, 0–20, inherited like the other per-year settings), all available from 1 Sep, used oldest first. On 31 December of calendar year Y, open days (previous holiday year's leftover + current remaining) above 5 are paid out in January (previous leftover first); the rest of the previous leftover is lost. Feriefri can't be booked beyond the allowance: book only through `Bookings.BookFeriefri`, never `BookRange` with `DayType.Feriefri`. It doesn't count toward vacation balances and has no opening balance.
- The calendar-year status (`BalanceCalculator.CalculateCalendarYear`) is about the holiday year whose deadline is 31 December of that calendar year.
- Only working days count: weekdays that are neither a Danish public holiday (`DanishHolidays`, calculated automatically: Nytårsdag, Easter-based days, Kristi himmelfartsdag, Pinse, 24–26 and 31 December) nor one of the user's manual holidays (that year's `PublicHolidays`). `BalanceCalculator.IsWorkingDay` applies the Danish holidays itself, so the `holidays` parameters everywhere mean manual holidays only.
- Bookings are stored per day (`BookedDay`: date, `DayType` Vacation/Feriefri, `Amount` 1 or 0.5, description), at most one record per date. Ranges are only an input/display concept: `Bookings.BookRange` creates one record per working day, and `Bookings.GroupPeriods` groups days back into periods for display. One booked range may cover at most one year (`Bookings.MaxRangeYears`).
- Balances count only `Vacation` days on working days, as decimals. Free working days left are counted from today (inclusive) through 31 December; a booked day of either type is not free, a half-booked day is half free.
- "Used" = vacation dates on or before today; "booked ahead" = after today.

## Conventions

- Put calculation logic in Core with tests, not in the view model. Core functions take `today` as a parameter so they're testable.
- Use `DateOnly` for dates in Core; convert at the view-model boundary (`DatePicker` uses `DateTime?`).
- No MVVM or UI frameworks/NuGet packages without asking; keep code-behind empty.
- UI must work in both light and dark themes: colours only come from the theme palettes (add a key to both `LightTheme.xaml` and `DarkTheme.xaml`) and are referenced with `DynamicResource`; no hard-coded colours elsewhere. Check new UI with screenshots in both themes.
- All user-facing text comes from the App's string resources and exists in every language: English, Danish, Romanian and Redneck (feature 7). Never hard-code text in XAML or view models; the app name "Vacation Calculator" is never translated. Core contains no user-facing text (holidays and months are keys/numbers the App translates). Dates inside text go through `Services/DateText` in the current language ("31 December 2026", "31. december 2026"); inputs, numbers and table date columns follow the Windows regional format.
- `MainViewModel` takes an injected clock (`Func<DateOnly>`); never read `DateTime.Today` in the App outside that default.
- Every user change that saves goes through `MainViewModel.Apply`, which snapshots the data and rolls back on a failed save. Each user action clears the messages first; `ErrorMessage` is for refusals, validation and save failures, `InfoMessage` for successful outcomes with notes.
- Layout limits: the default window fits a 1366×768 screen (716 DIP high) and the vacation list shows at least 5 rows at the default size; the sidebar scrolls as one area.
- For manual runs and screenshots, start the app with `--data <path>` to use a scratch data file (and preferences next to it) instead of the user's real `%APPDATA%` data.
- Data lives in `%APPDATA%\VacationCalculator\vacation-data.json`. Changes to the models must stay backward compatible with existing files (new properties need sensible defaults); never write code paths that could overwrite the user's file with empty data.

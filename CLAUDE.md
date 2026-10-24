# Vacation Calculator

WPF desktop app (.NET 9, C#) that tracks vacation days per calendar year and warns when unused days must be taken before 31 December. It builds `VacationCalculator.exe`; in user-facing text the name is always written "Vacation Calculator".

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
| `wpf-ui` | sonnet | `src/VacationCalculator.App` | XAML, styles, view models, bindings. Checks the app launches. |
| `tester` | sonnet | additional tests under `tests/` | Independent edge-case and regression tests; PASS/FAIL verdict. Never edits `src/`. |
| `code-reviewer` | opus | nothing (read-only) | Reviews the full diff for bugs and data-loss risks. |

If the Agent tool says one of these agents doesn't exist, the session started before the agent files were added or changed (Claude Code loads `.claude/agents/` at startup). Stop and ask the user to restart Claude Code; don't substitute a general-purpose agent.

### Steps for every change

1. **Plan.** Restate the request, decide which agents are involved, and ask the user about anything genuinely ambiguous before starting.
2. **Implement.** A Core-only change goes to `domain-logic`, a UI-only change to `wpf-ui`. A feature that touches both: `domain-logic` first, then `wpf-ui`, passing along the Core API that `domain-logic` reported.
3. **Test.** Send `tester` the user's original request (not just the implementer's summary) and which agent made the change.
4. **Review.** Send `code-reviewer` the request and a summary of what changed.
5. **Fix loop.** If `tester` reports FAIL, `code-reviewer` reports a real problem, or a report is wrong or incomplete, send the specifics back to the owning implementation agent and repeat from step 3. Do not fix it in the main session.
6. **Verify and ship.** Run `dotnet build VacationCalculator.sln` and `dotnet test` yourself, then commit and push (see Git). Tell the user what changed and what was verified.

### Writing a brief

Each agent only knows what its brief says (plus this file). Include the user's request in their words, the goal and acceptance criteria, relevant files, decisions already made, and what earlier agents reported (e.g. new Core API, failing test names).

### Done directly by the main session

Only work outside every agent's scope: answering questions, git, `CLAUDE.md`, `.claude/` (agent definitions, settings), the solution file and `.gitignore`.

## Git

Every completed update to the app is committed and pushed to GitHub (`origin`, https://github.com/thomaslindgaard/VacationCalculator) without asking first:

- Commit once the change builds and tests pass, with a message describing what changed and why. Then `git push`.
- One commit per logical change; don't bundle unrelated work.
- Never commit or push a failing build or failing tests. Report the failure instead.
- Subagents (`domain-logic`, `wpf-ui`, `tester`, `code-reviewer`) don't commit. The main session commits after the work, and any review, is finished.
- If the push fails (e.g. auth or a diverged remote), keep the local commit, report the error, and don't force-push.

## Layout

- `src/VacationCalculator.Core` (net9.0): models, `BalanceCalculator` (balance arithmetic), `Bookings` (booking and grouping operations), `DataStore` (JSON persistence). No UI dependencies.
- `src/VacationCalculator.App` (net9.0-windows, WPF): `MainWindow.xaml` (sidebar + main content), `ViewModels/` (hand-rolled MVVM: `ObservableObject`, `RelayCommand`, `MainViewModel`), `Themes/` (`LightTheme.xaml` / `DarkTheme.xaml` palettes with identical keys, `Controls.xaml` control styles), `Services/ThemeService.cs` (Light/Dark/System, stored in `vacation-calculator-preferences.json` next to the data file).
- `tests/VacationCalculator.Core.Tests` (xUnit): tests for Core. `tester` may add `tests/VacationCalculator.App.Tests` for view-model tests.

## Domain rules

- Allowance is per calendar year: `AllowanceDays + CarryOverDays`. Unused days expire on 31 December.
- Only working days count: weekdays that are not in that year's `PublicHolidays`. Public holidays are entered by the user; no country's calendar is built in.
- Bookings are stored per day (`BookedDay`: date, `DayType` Vacation/Feriefri, `Amount` 1 or 0.5, description), at most one record per date. Ranges are only an input/display concept: `Bookings.BookRange` creates one record per working day, and `Bookings.GroupPeriods` groups days back into periods for display.
- Balances count only `Vacation` days on working days, as decimals. Free working days left are counted from today (inclusive) through 31 December; a booked day of either type is not free, a half-booked day is half free.
- "Used" = vacation dates on or before today; "planned" = after today.

## Conventions

- Put calculation logic in Core with tests, not in the view model. Core functions take `today` as a parameter so they're testable.
- Use `DateOnly` for dates in Core; convert at the view-model boundary (`DatePicker` uses `DateTime?`).
- No MVVM or UI frameworks/NuGet packages without asking; keep code-behind empty.
- UI must work in both light and dark themes: colours only come from the theme palettes (add a key to both `LightTheme.xaml` and `DarkTheme.xaml`) and are referenced with `DynamicResource`; no hard-coded colours elsewhere. Check new UI with screenshots in both themes.
- `MainViewModel` takes an injected clock (`Func<DateOnly>`); never read `DateTime.Today` in the App outside that default.
- For manual runs and screenshots, start the app with `--data <path>` to use a scratch data file (and preferences next to it) instead of the user's real `%APPDATA%` data.
- Data lives in `%APPDATA%\VacationCalculator\vacation-data.json`. Changes to the models must stay backward compatible with existing files (new properties need sensible defaults); never write code paths that could overwrite the user's file with empty data.

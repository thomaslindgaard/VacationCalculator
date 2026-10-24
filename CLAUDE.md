# Vacation Calculator

WPF desktop app (builds `VacationCalculator.exe`; shown to users as "Vacation Calculator") (.NET 9, C#) that tracks vacation days per calendar year and warns when unused days must be taken before 31 December.

## Commands

```
dotnet build VacationCalculator.sln
dotnet test
dotnet run --project src/VacationCalculator.App
```

Run `dotnet build` and `dotnet test` before reporting any change as done.

## Git

Every completed update to the app is committed and pushed to GitHub (`origin`, https://github.com/thomaslindgaard/VacationCalculator) without asking first:

- Commit once the change builds and tests pass, with a message describing what changed and why. Then `git push`.
- One commit per logical change; don't bundle unrelated work.
- Never commit or push a failing build or failing tests. Report the failure instead.
- Subagents (`domain-logic`, `wpf-ui`, `code-reviewer`) don't commit. The main session commits after the work, and any review, is finished.
- If the push fails (e.g. auth or a diverged remote), keep the local commit, report the error, and don't force-push.

## Layout

- `src/VacationCalculator.Core` (net9.0): models, `BalanceCalculator` (all vacation-day arithmetic), `DataStore` (JSON persistence). No UI dependencies.
- `src/VacationCalculator.App` (net9.0-windows, WPF): `MainWindow.xaml`, `ViewModels/` (hand-rolled MVVM: `ObservableObject`, `RelayCommand`, `MainViewModel`). Shared styles live in `App.xaml`.
- `tests/VacationCalculator.Core.Tests` (xUnit): tests for Core only.

## Domain rules

- Allowance is per calendar year: `AllowanceDays + CarryOverDays`. Unused days expire on 31 December.
- Only working days count: weekdays that are not in that year's `PublicHolidays`. Public holidays are entered by the user; no country's calendar is built in.
- Entries are inclusive date ranges and may span years; each year counts only its own dates. Overlapping entries count each date once.
- "Used" = vacation dates on or before today; "planned" = after today.
- Whole days only (allowance and carry-over may be decimals).

## Conventions

- Put calculation logic in Core with tests, not in the view model. Core functions take `today` as a parameter so they're testable.
- Use `DateOnly` for dates in Core; convert at the view-model boundary (`DatePicker` uses `DateTime?`).
- No MVVM or UI frameworks/NuGet packages without asking; keep code-behind empty.
- Data lives in `%APPDATA%\VacationCalculator\vacation-data.json`. Changes to the models must stay backward compatible with existing files (new properties need sensible defaults); never write code paths that could overwrite the user's file with empty data.

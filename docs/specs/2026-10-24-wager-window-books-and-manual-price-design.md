# One wager window: books, a price of your own, and the journal's door

Date: 2026-10-24. Approved in conversation the same day.

## Why

Two complaints from logging real NFL bets:

1. The Place wager window showed the best price and nothing else. A bet already placed at
   DraftKings (Steelers −3) could not be recorded at DraftKings' number, let alone at the number
   actually taken an hour earlier.
2. The Journal's New entry form was a second, different way to record a bet — a text field and a
   separate dropdown to find the game, ordered nearest-to-now, and none of the wager window's
   price, checks or styling.

## Goals

- **One form, two doors.** The Place wager window (`BoardWager`) is the only place a bet is
  recorded. The Board row opens it with the game and side filled in; the Journal's *Log entry*
  opens the same window with a game search at the top. The Journal's own New entry form goes;
  the Journal keeps an inline form for **editing** an existing entry only.
- **Find the game in one control.** A search field that is its own dropdown: typing fills a list
  directly beneath it, **from today backward** (games still to come after, soonest first), each
  row matchup · kickoff · league. Picking one shows its sides. A last row, *No game — future or
  other*, records a bet with a described market and no game, as the old form could.
- **Choose the book.** Under the price, every book quoting the chosen side, each with price, line
  and capture age. Book chips filter the list (several at once); the filter is remembered with
  the operator's settings. Best is marked, not forced. Picking a book re-reads the headline
  price, EV, fair, the checks and the returns from that book's latest fetched rung, and the bet
  logs against that book.
- **Enter your own price.** An *Enter my own price* key swaps the headline for fields: price,
  line (spread/total), book (any known book, or one typed). The entry is stored as a manual price
  and the Journal tags it *manual*. Checks read the typed number; a refresh never overwrites it.
  A manual price lifts the closing-line block — the operator is saying what they took.
- **Keep the look.** Grouped sections with `PanelHeader` over `wgroup`s, the Before-you-log
  `CheckList`, `StakeRow`, note, Add to slip / Log action bar, `Note` for moved/logged/error.

## Non-goals

- No change to how prices are fetched, scored or graded.
- Parlays stay on the slip.
- No per-game book memory: the filter is one desk-wide choice.

## Decisions (builder's calls, stated)

- The game search lives in `GamePicker`, reused by the wizard and by the Journal's edit form.
  `SearchService.GamesAsync` gains a from-today order (amended the same day from newest-first,
  which put next month's games above the one being logged); the desk search keeps nearest-first.
- A game with no prices can still be picked; its sides are offered from the teams (moneyline,
  spread, total), and the Price section opens straight on a manual price.
- The hidden-books filter is an `AppSettings` row (`wager.books.hidden`, comma-separated), so it
  is personal and stays out of the data snapshot.
- `JournalEntry.ManualPrice` (bool, default false) records a typed price; `EntryDraft` and
  `SlipItem` carry it. One migration.
- EV for a manual price is read against the side's fair price only when the typed line equals
  the fair price's line; otherwise value is unread, as for any book off the reference number.

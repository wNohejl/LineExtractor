# An analyst on the desk: Claude reads the numbers, a model makes them

Date: 2026-10-26. Written from a `/goal` brief; not yet approved.
**Audience:** the developer deciding how an LLM attaches to LineOps' data, and whether a bet's
soundness should come from Claude, from a sports-trained LLM, or from something else.

## 0. The answer, first

**Neither LLM should decide whether a bet is sound.** A sound bet is a price that pays more than
the outcome's real chance is worth, and the real chance is a number. What produces that number,
and the results the reference interface shows, is a **calibrated numeric model** compared against
the **de-margined market price**, then **sized** and **judged by closing-line value**. A language
model, general or sports-trained, is the wrong tool for the number and the right tool for
everything around it: finding the data, explaining the verdict, noticing what the model cannot
see.

So the plan has two halves that must not be confused:

| Half | Produces | Built from | Trusted because |
|---|---|---|---|
| **The model** | Probabilities, fair prices at any number, edge, bet-to, stake | Code over the box scores and closes already in Postgres | It is backtested walk-forward and graded against the close, in tests |
| **The analyst** | Answers, explanations, briefs, "what the model doesn't know" | Claude calling read-only tools over the same data | It may only quote numbers a tool returned |

Claude is the better choice for the analyst than a sports-trained LLM. For the model, the
answer to "an LLM trained on sports data" is **yes to training on sports data, no to it being
an LLM**: a simulation and a regression fitted to 2,430 MLB and 391 NFL finals.

## 1. What the reference interface actually is

The Action Network page the brief cites ("Zerillo's MLB playoff model … using BARTOLO") is
not an LLM product. Read on 2026-10-26:

- **BARTOLO** ("Baseball Analytic Research Tool Optimized for Longterm Outcomes") is Sean
  Zerillo's PRO tool: a scoreboard of daily projections where each game opens onto "all the
  input data and statistics" behind it.
- Its numbers come from three sources: **Game Sim**, a Monte Carlo simulation of each game
  (the sole basis of game and series odds); **Playoff Fit**, a team model on expected
  wOBA/xwOBA, offensive strikeout rate, Outs Above Average and rotation Stuff+, backtested to
  2016 and calibrated to series prices; and a **Composite**, an equal blend of PECOTA,
  FanGraphs, ATC, THE BAT and OOPSY with the two above.
- Zerillo's documented daily method (2019 onward) is player projections (ATC) blended with
  Statcast form, scaled to the league run environment, with park factors, platoon splits,
  bullpen fatigue, starter stamina, lineup slots, defence and home field. Weather, late lineups
  and umpires are handled by hand.
- **A pick** shows: the market, the **projected price beside the listed price**, the **edge %**,
  a sentence of rationale naming the model behind it, **units**, the **book**, and a **bet-to**
  number (e.g. Guardians AL Pennant +360, 0.5u, bet to +300).
- Its headline claim is that the model "predicted 84% of directional moneyline moves" in 2026
  (self-reported). That is a **closing-line-value** claim: the market moved toward the model.

That is the interface to copy: projected vs listed, edge, bet-to, units, book, the inputs one
click away, and a record kept in CLV terms.

## 2. What the evidence says about LLMs pricing bets

- **KellyBench** (General Reasoning, arXiv 2604.27865): frontier models given a season of
  English Premier League data, lineups and public odds, betting to grow a bankroll. **Every
  model lost money on average over five seeds; the best averaged −8% ROI; several were
  ruined.** A human-expert rubric scored the best strategy (Claude Opus 4.6) at 26.5%
  sophistication.
- **FIFA World Cup 2026 benchmark** (arXiv 2607.17765): Claude Opus 4.8, GPT-5.5, Gemini 3.1 Pro
  and Grok as search-and-reason forecasting agents over 104 matches. **None beat the bookmaker's
  Brier score; a flat stake on the market favourite out-earned all four**; returns ran −18% to
  +10%; the four gave the same top pick in 92% of matches.
- **Calibration beats accuracy** (Walsh & Joshi, *Machine Learning with Applications* 16,
  2024, NBA): choosing a model by calibration rather than accuracy produced markedly higher
  betting returns. A 2025 corrigendum revised the betting figures, so take the direction rather
  than the headline numbers. An LLM's stated "70% confident" is not a calibrated probability.
- **Sports-trained LLMs:** no peer-reviewed or independently audited result found. Published
  accuracy figures are vendors' own ledgers, and win rate is a weak test anyway: picking the
  moneyline favourite already wins most NFL games.

The pattern is consistent. An LLM reasons well about evidence and badly about prices it has to
beat, and fine-tuning one on sports text teaches it to talk about sports, not to out-price a
market. The information that moves a probability is in numbers, where a regression or a
simulation uses all of it and a language model approximates it.

## 3. What really produces results, in order of reliability

1. **Shopping a price against a sharp fair price.** Nine books against Pinnacle's no-vig number:
   the EV check LineOps already runs (`FairValue`, `Checks.Value`). This is the most dependable
   edge the desk has, and it needs no model.
2. **Pricing numbers the reference didn't.** `FairValue` deliberately refuses a fair price on a
   line Pinnacle did not quote ("that needs a model of how often games land on 2"). A
   simulation *is* that model: it prices −2.5, an alternate total, or a run line from one
   distribution, honestly.
3. **Being early.** A model that reacts to a starter announcement before the market does earns
   CLV. This is what BARTOLO's "directional moves" claim measures.
4. **Saying pass.** Most slates offer nothing; a model's most valuable output is often *Pass*,
   with a reason.
5. **Sizing.** Fractional Kelly on a calibrated edge, capped. Overbetting is how KellyBench's
   models were ruined.
6. **Measuring by CLV, not win rate.** Results are noisy for hundreds of bets; beating the close
   shows up in tens.

The realistic expectation, stated plainly: a model built from box scores will not beat closing
lines on major MLB and NFL sides overall. Its value is in (2), (3) and (4), and in making (1)
cover more numbers. A blend that leans mostly on the market and moves it a little is the
honest target, not a model that replaces it.

## 4. What the data supports today (measured 2026-10-26)

| | MLB | NFL |
|---|---|---|
| Finals with box scores | 2,430 regular + 37 post (2026) | 285 (2025), 106 of 272 (2026) |
| Batting lines | AB, H, R, HR, BB, K, RBI, OBP, SLG (54,790) | passing, rushing, receiving, defence |
| Pitching lines | IP, ER, H, BB, K, HR, PC (25,303) | |
| Closes | DraftKings reference on 2,466 games; a multi-book market on ~107; Pinnacle on 70 | DraftKings 214, ESPN BET 177; Pinnacle on 42 |
| Probable starters | 30 games (the spine annotates today and tomorrow only) | `Starters.LastQuarterback` infers the QB |
| Journal | 11 entries, 10 settled, none with a fair close | |

What follows from it:

- **Enough for a box-score model**: FIP-style pitcher ratings (K, BB, HR, IP), team offence
  rates, park factors, bullpen workload, NFL margin ratings. **Not enough for Statcast-grade
  inputs** (xwOBA, Stuff+, OAA). The model is a box-score model and says so.
- **The evaluation baseline exists**: a de-margined DraftKings close on nearly every MLB game is
  a legitimate reading of the market's probability to score a model against. (It is not a fair
  price for EV against DraftKings' own number; `FairValue`'s single-book rule still holds there.)
- **Forward validation is the only CLV test.** Odds history cannot be backfilled (ADR 0009), so
  scan-time prices exist only for games the desk watched live. Backtests score against the close;
  CLV of the model's picks accrues from the day it ships.
- **The journal is too small to judge anyone yet**, the operator included.
- **Timing:** MLB's season ends this week; NFL is at week 8. An NFL model can be used now; an MLB
  model is backtested on 2026 and ready for opening day 2027.

## 5. Goals, non-goals, anti-goals

**Goals**
- A model per league that produces a **fair probability and price for any side and number**,
  stored **before** the game, immutable, and graded after it.
- A deterministic **verdict** for any bet: *Sound*, *Lean*, *Pass* or *Unread*, with the
  numbers behind it.
- A **Projections** window shaped like BARTOLO's picks: projected vs listed, edge, bet-to,
  units, book, inputs on demand.
- **Claude attached to the data** through one read-only tool catalog, usable first from Claude
  desktop/Code over MCP and later from an **Analyst** window on the desk.
- A **model record**: log loss and calibration against the close, CLV of picks, directional moves.

**Non-goals**
- Fine-tuning or hosting any LLM.
- Statcast, injury feeds or paid stats (going-public §10b: the project stays local and free).
- Placing bets (README scope) or a new league (CLAUDE.md scope).

**Anti-goals: failure even if it works**
- **An LLM stating a probability or a "lock".** Every number Claude says came from a tool.
- **A backtest that leaks**: a projection that saw the close, the result, or a stat recorded after
  first pitch.
- **A fabricated line** where no market or model priced one (CLAUDE.md; ADR 0003: unread, not
  zero).
- **Spend that grows unseen.** The analyst's cost is metered and capped like a credit-billed feed.

## 6. The model

### 6.1 Shared shape (`LineOps.Core/Modeling`, pure, no dependencies)

- `GameProjection`: per game, per model version: the distribution it implies, and from it
  `P(side, number)` for moneyline, spread/run line and total at **any** number.
- `Blend`: `logit(p) = w·logit(p_model) + (1−w)·logit(p_market)`, with `w` per league and market
  fitted walk-forward on log loss. If the fit drives `w` to zero, the model adds nothing, and the
  record says so.
- `BetVerdict` (§7), `Kelly`, `BetTo`: the lowest price at which edge still meets the threshold,
  `decimal = (1 + threshold) / p`.

### 6.2 MLB v1: a run simulation

- **Offence:** season-to-date OBP, SLG, HR/PA, BB/PA, K/PA per team, shrunk toward league average
  by sample size, park-neutralised.
- **Starter:** FIP-like `(13·HR + 3·BB − 2·K) / IP` plus a league constant, shrunk by innings;
  expected innings from recent starts. (No HBP in the box score; the constant absorbs it.)
- **Bullpen:** relievers' aggregate of the same over the last 30 days; a fatigue term from relief
  pitches in the last three days (`PC` is stored).
- **Park:** home/away run ratios from 2,430 games, regressed heavily.
- **Runs:** each side's expected runs from offence × (starter share + bullpen share) × park ×
  home field; runs drawn from a negative binomial (MLB runs are over-dispersed); ties resolved by
  a simple extra-innings step. 20,000 draws per game.
- **Starter identity:** the spine's probable pitcher where present; for history, the pitcher who
  started per the box score. *Builder's call:* take the first pitcher ESPN lists for each side if
  the payload keeps that order, else the side's pitcher with the most IP, and check the rule
  against the 30 games that have a probable.

### 6.3 NFL v1: ratings and a margin distribution

- **Team rating:** margin-of-victory ratings (ridge regression on point differential with home
  field), carried from 2025 with a one-third regression to the mean at season start.
- **Quarterback:** an adjustment when `Starters.LastQuarterback` differs from the QB the rating
  was built on, sized from adjusted net yards per attempt. Limited, and labelled as such.
- **Cover and total:** a normal margin distribution with σ fitted from residuals. Key numbers (3,
  7) need more games than 391 to estimate; v1 leaves them out and says so rather than borrowing a
  published table.

### 6.4 The projection store (`LineOps.Data`)

`projection(id, game_id, model_key, model_version, market, outcome, line, probability,
fair_price, market_probability, market_basis, inputs jsonb, projected_at)`: append-only,
written only before `starts_at`, never updated. It carries the market's no-vig probability
**at projection time**, which is what makes directional-move and CLV scoring possible later.
Settlement (`SettlementService`) grades each row against the result and the close. Projections
are market data rather than personal, so they travel in the data snapshot.

### 6.5 Jobs (ADR 0012: named, triggered by state)

- `model:project:<sport>`: due when a game inside the movement window has a scan newer than its
  last projection, or a starter changed. Costs nothing; reads Postgres only.
- `model:backtest:<sport>`: run by hand from the History window; walk-forward, games strictly
  before the target's start, stats captured before first pitch.

### 6.6 Ship gates

- A **market-only baseline** (the blend with `w = 0`) lands first, so the harness has a number to
  beat before any model exists.
- A league's model ships to the desk only when its walk-forward blend's log loss is **no worse
  than the market-only baseline** against the close, with calibration shown in deciles.
- Stake suggestions appear only after **100 graded Sound picks** show non-negative CLV. Until
  then the window shows edge and bet-to, not units.

## 7. Is the bet sound? The verdict rules (`BetVerdict`)

Deterministic, unit-tested, and the only source of a verdict. Claude can call it; it cannot
overrule it.

| Verdict | When |
|---|---|
| **Sound** | Blended edge ≥ threshold **and** the price is fresh (`Checks.StaleAfter`) **and** not a closing line **and** a projection exists from after the last starter/lineup change |
| **Lean** | Edge positive but under threshold, or Sound in every respect but one input is stale |
| **Pass** | Edge ≤ 0 |
| **Unread** | No projection, or no market probability to blend with. Unread, not zero (ADR 0003) |

*Builder's calls, tunable and refitted on the backtest:* thresholds of **2% EV on favourites and
short underdogs, 3.5% at +150 or longer** (model error grows with the price); stake **¼ Kelly,
capped at 2% of bankroll**, against a bankroll the operator enters. That is a new operator
setting: `PerformanceAnalytics.BankrollCurve` is cumulative profit, not a bankroll size.

The wager window's `Checks.Build` gains a **Model** row beside Value: *Sound: model 58.1%,
fair −139, +3.4% at −125 (bet to −133).* The board's Value column gains the model as a basis
beside Pinnacle and consensus.

## 8. The analyst: Claude over the data

### 8.1 One tool catalog, two transports

A `LineOps.Analyst` library defines the tools once over existing read services; two thin hosts
expose them.

| Tool | Wraps |
|---|---|
| `find_games(text, date?)` | `SearchService.GamesAsync` |
| `get_board_row(game_id)` | `BoardService.GetRowAsync`: prices, best book, fair, EV |
| `get_projection(game_id)` | the projection store: probabilities, fair prices, inputs, w |
| `evaluate_bet(game_id, market, outcome, line, price, book)` | `BetVerdict`: **the only route to a verdict** |
| `get_matchup_form(game_id)` | `MatchupCrossReference.GetAsync` |
| `get_team_log(team, season, split?)` | `GameLogService.TeamGameLogAsync` + `GameLogSplits` |
| `get_head_to_head(a, b)` / `get_player_log(player, season)` / `get_box_score(game_id)` | `GameLogService` |
| `get_model_record(sport, season)` | projection grading: log loss, calibration, CLV, directional moves |
| `get_journal_summary(filters)` | `PerformanceAnalytics`; **off unless the operator allows it** (§10) |
| `query_sql(select)` | *optional:* a read-only Postgres role, `SELECT` only, 5 s timeout, 500 rows |

Every tool is read-only. Nothing the analyst can call writes a row, pulls a metered feed or logs a
bet.

### 8.2 Transport 1, now: an MCP server (`LineOps.Mcp`)

A stdio host on the official C# MCP SDK (`ModelContextProtocol` NuGet, maintained by Microsoft
and Anthropic) registered with Claude desktop and Claude Code. The operator asks "is Cubs +135
sound?" in Claude and it calls `find_games` → `get_board_row` → `evaluate_bet`. **No API key and
no per-token cost**: it runs on the existing Claude subscription. It also shows, before anything
is built into the desk, which questions are actually asked.

### 8.3 Transport 2, later: the Analyst window

The official C# SDK (`Anthropic` NuGet) with tool use, from the web host:

- **Model:** `claude-opus-5-5`, adaptive thinking, effort set explicitly (`medium` for chat,
  `high` for the daily brief), streaming, refusal fallbacks on.
- **Caching:** frozen system prompt and tool list first, with a cache breakpoint after them;
  the volatile parts (today's date, the question) after it.
- **System prompt rules:** quote only numbers a tool returned; a verdict comes from
  `evaluate_bet`; say *unread* when a tool returns none; name what the model does not see
  (injuries, lineups, weather, umpire) whenever it gives a verdict.
- **Daily brief:** one Batch request (half price) at slate time covering only Sound and Lean
  games: what the model likes, why, what would change it.
- **Metering:** an `analyst_run` row per request (tokens in/out/cached, dollars, tools called),
  a monthly dollar budget the operator sets, and a guard that refuses past it, the same pattern
  as `CreditBudgetGuard`. Ops shows the burn. `AnalystRuns` holds the operator's questions, so it
  joins `JournalEntries`, `Parlays` and `AppSettings` in `publish-data.ps1`'s personal list.
- **Key:** `Analyst:ApiKey` in `appsettings.Local.json` or user-secrets, as the odds key is.

**Cost, estimated:** a question with three to six tool calls is about 40–80K input tokens across
the loop (most cached after the first turn) and 2–5K output, which at Opus 5.5's $4 / $20 per
million tokens (cache reads $0.20) comes to roughly **$0.15–0.30 a question**. A brief on four
games a day in Batch is about **$10 a month**. These are estimates; `analyst_run` measures the
real figure. Sonnet 5.5 ($2 / $10) halves it and Haiku 5.5 ($0.10 / $0.50) cuts it by about 40×,
at some cost in judgement; that trade is the operator's (§10).

## 9. The desk

Follows the panel anatomy (ADR 0020) and the HIG component rules (`apple-hig-components`).

- **Projections** (new window; sport picker): one row per game, shaped like a BARTOLO pick:
  matchup and starters · model % and fair price · best listed price and its book · edge · bet-to
  · verdict chip · units (once §6.6 allows). The total on the same row. Row actions: **Why**
  (opens the Analyst with the game loaded), **Inputs** (every number the projection used, as
  BARTOLO shows "all the input data"), **Place wager** (the existing `BoardWager`, prefilled).
- **Analyst** (new window, floating when the desk is full): a conversation, each answer with the
  tool calls it made listed under it so a number can be traced to its source.
- **Performance → Model record:** log loss vs close, calibration deciles, CLV of Sound picks,
  directional moves predicted. The desk's own version of BARTOLO's "84%" claim, measured rather
  than asserted.

## 10. Decisions that are yours

Defaults are what the builder does if nothing is said.

1. **Paid API at all?** The analyst window is the project's first metered cost that is not a free
   tier. *Default: build the MCP server first (no cost) and decide on the window after a few
   weeks of use.*
2. **May the journal reach Anthropic's API?** The journal and parlays are personal and excluded
   from data snapshots. *Default: no; `get_journal_summary` stays off until switched on in
   operator settings.*
3. **Which Claude model for the window?** *Default: Opus 5.5 at medium effort; Sonnet 5.5 or Haiku
   5.5 if spend matters more than depth.*
4. **League order.** *Default: harness and market baseline, then NFL (in season), then MLB
   (backtest on 2026 for 2027).* The reverse order suits a goal of matching the reference
   interface first.
5. **Units.** *Default: no stake suggestions until the §6.6 gate.*

## 11. Phases

Each phase is one plan, committed on `LineX_Development`, verified by the CLAUDE.md list.

| # | Phase | Ends with | Size |
|---|---|---|---|
| 1 | **MCP analyst** over today's data: catalog minus projection tools, read-only role, `LineOps.Mcp` | Claude desktop answers board, form and value questions from LineOps | 2–3 days |
| 2 | **Projection store and harness**: entity, migration, grading in settlement, market-only baseline, record metrics | `get_model_record` reports the baseline's log loss and calibration | 3–4 days |
| 3 | **NFL model** + `BetVerdict` + Model check row | NFL projections on the board and in the wager window, behind the §6.6 gate | 1 week |
| 4 | **Projections window** and Model record | The BARTOLO-shaped view for NFL | 3–4 days |
| 5 | **MLB model** (simulation) + backtest on 2026 | Gate passed or failed, with the numbers; ready for 2027 | 1–2 weeks |
| 6 | **Analyst window** (only if §10.1 is yes): Claude API, metering, daily brief | In-desk answers within a budget | 1 week |
| 7 | *Optional:* Claude web search for lineup and injury news as **veto flags** feeding `BetVerdict` | A starter scratch turns Sound into Unread before the market moves | 3–4 days |

## 12. Risks

- **The model may add nothing.** The likeliest outcome on MLB sides is `w ≈ 0`. That is a result,
  not a failure: the harness says so, and the value lies in pricing off-reference numbers and
  timing.
- **Leakage** is the classic way a betting backtest lies. Projections are written before the start
  and immutable; backtests are walk-forward only; a test asserts no input row is captured after
  first pitch.
- **ESPN is unofficial** and can change shape; the model degrades to *Unread*, never to a guess.
- **An analyst that sounds sure** is persuasive when it is wrong. The tool-only-numbers rule and
  the visible tool trail are the defence; an eval of about 30 real questions
  (`/claude-api build-eval`) checks it before the window ships.

## 13. Sources

- Action Network, *Zerillo's MLB playoff model … using BARTOLO*; *Opening Pitch*; *How to bet
  baseball using model projections* (Sean Zerillo).
- General Reasoning, *KellyBench* (arXiv 2604.27865).
- *FIFA World Cup 2026 as a Contamination-Free Benchmark for LLM Forecasting Agents*
  (arXiv 2607.17765).
- Walsh & Joshi, *Machine learning for sports betting: should model selection be based on accuracy
  or calibration?*, Machine Learning with Applications 16 (2024), and its 2025 corrigendum.
- Microsoft Learn, *Get started with .NET AI and the Model Context Protocol* (the C# MCP SDK).
- Anthropic model pricing as of 2026-10-06 (Opus 5.5 $4/$20, Sonnet 5.5 $2/$10, Haiku 5.5
  $0.10/$0.50 per million tokens).

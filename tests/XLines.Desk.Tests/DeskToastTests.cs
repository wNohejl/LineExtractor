using System.Text.RegularExpressions;
using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Tests.Support;
using XLines.Desk.Windowing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Desk.Tests;

/// <summary>
/// The desk's notices (ADR 0028): one stack, newest in front, the rest layered behind it with a
/// count; fanned out on demand, each dismissible, with Clear all; brief ones going on their own,
/// failures and anything with a key staying; clocks held while the stack is being read; two live
/// regions; and the window manager's notice said through the same stack.
/// </summary>
public class DeskToastTests : DeskTestContext
{
    /// <summary>A clock a test moves by hand, firing whatever timers fall due.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public DateTimeOffset Now { get; private set; } = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, Now + dueTime);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            Now += by;

            foreach (var timer in _timers.Where(t => !t.Disposed && t.Due <= Now).ToList())
            {
                timer.Disposed = true;
                timer.Fire();
            }
        }

        private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            public DateTimeOffset Due { get; private set; } = due;
            public bool Disposed { get; set; }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = owner.Now + dueTime;
                return true;
            }

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private readonly ManualTime _time = new();

    public DeskToastTests()
    {
        Services.AddScoped(_ => new DeskToasts(_time));
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());
    }

    private DeskToasts Toasts => Services.GetRequiredService<DeskToasts>();

    // The bench hears a leave at once; LeaveGrace has its own test.
    private IRenderedComponent<DeskToastStack> Stack() => RenderComponent<DeskToastStack>(p => p.Add(x => x.LeaveGrace, TimeSpan.Zero));

    [Fact]
    public void One_notice_shows_whole_with_its_icon_words_and_dismiss_and_no_count()
    {
        var stack = Stack();
        stack.InvokeAsync(() => Toasts.Success("Saved."));

        var toast = stack.Find(".toast");
        Assert.Contains("toast--front", toast.ClassList);
        Assert.Contains("toast--positive", toast.ClassList);
        Assert.Equal("Saved.", toast.QuerySelector(".toast__text")!.TextContent);
        Assert.NotNull(toast.QuerySelector(".toast__icon svg"));
        Assert.Equal("Dismiss: Saved.", toast.QuerySelector(".toast__dismiss")!.GetAttribute("aria-label"));
        Assert.Null(toast.QuerySelector(".toast__count"));
        Assert.Empty(stack.FindAll(".toasts__head"));
        Assert.DoesNotContain("toasts--stacked", stack.Find(".toasts").ClassList);
    }

    [Fact]
    public void Several_are_layered_newest_in_front_with_a_count()
    {
        var stack = Stack();
        stack.InvokeAsync(() =>
        {
            Toasts.Info("First.");
            Toasts.Warn("Second.");
            Toasts.Success("Third.");
            Toasts.Info("Fourth.");
        });

        // Drawn newest first — in front in the capsule, at the top of the fanned list — so the DOM
        // and Tab follow the eye.
        var toasts = stack.FindAll(".toast");
        Assert.Equal(["Fourth.", "Third.", "Second.", "First."], toasts.Select(t => t.QuerySelector(".toast__text")!.TextContent));
        Assert.Contains("toast--front", toasts[0].ClassList);
        Assert.Contains("toast--depth-1", toasts[1].ClassList);
        Assert.Contains("toast--depth-2", toasts[2].ClassList);
        Assert.Contains("toast--deep", toasts[3].ClassList);
        Assert.Contains("toasts--stacked", stack.Find(".toasts").ClassList);
        Assert.DoesNotContain("toasts--fanned", stack.Find(".toasts").ClassList);

        // The count's key leads the capsule, before the notices: a figure, with its words for when
        // it is fanned or alone in a narrow header.
        var count = stack.Find(".toast__count");
        Assert.Null(toasts[0].QuerySelector(".toast__count"));
        Assert.Equal("4", count.QuerySelector(".toast__count-figure")!.TextContent);
        Assert.Equal("Activities · 4", count.QuerySelector(".toasts__title")!.TextContent);
        Assert.Equal("false", count.GetAttribute("aria-expanded"));
        Assert.Equal("Show all 4 activities", count.GetAttribute("aria-label"));
        Assert.Equal("Activities, 4 activities", stack.Find(".toasts").GetAttribute("aria-label"));
        Assert.Single(stack.FindAll(".toast__count"));
    }

    [Fact]
    public void The_stack_is_called_Activities_with_the_count_in_words()
    {
        var stack = Stack();
        stack.InvokeAsync(() => Toasts.Info("Only."));

        // One: the region is named, singular; there is no head line and no count key.
        Assert.Equal("Activities, 1 activity", stack.Find(".toasts").GetAttribute("aria-label"));
        Assert.Empty(stack.FindAll(".toasts__head"));

        stack.InvokeAsync(() => Toasts.Info("Another."));
        Assert.Equal("Activities, 2 activities", stack.Find(".toasts").GetAttribute("aria-label"));
        Assert.Equal("Activities · 2", stack.Find(".toasts__title").TextContent);
        Assert.Equal("Clear all", stack.Find(".toasts__clear").TextContent);
        Assert.Equal("Clear all activities", stack.Find(".toasts__clear").GetAttribute("aria-label"));
        Assert.Equal("Show all 2 activities", stack.Find(".toast__count").GetAttribute("aria-label"));

        stack.Find(".toast__count").Click();
        Assert.Equal("Fold all 2 activities", stack.Find(".toast__count").GetAttribute("aria-label"));

        // No user-facing word still says "notification".
        Assert.DoesNotContain("otification", stack.Markup);

        Assert.Equal("1 activity", DeskToastStack.CountLabel(1));
        Assert.Equal("12 activities", DeskToastStack.CountLabel(12));
        Assert.Equal("Activity · 1", DeskToastStack.HeadLabel(1));
        Assert.Equal("Activities · 12", DeskToastStack.HeadLabel(12));
    }

    [Fact]
    public void An_empty_stack_carries_no_name()
    {
        var stack = Stack();
        Assert.Null(stack.Find(".toasts").GetAttribute("aria-label"));
        Assert.Equal("true", stack.Find(".toasts").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void The_count_fans_the_stack_out_and_Escape_folds_it()
    {
        var stack = Stack();
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Info("Second."); });

        stack.Find(".toast__count").Click();
        Assert.Contains("toasts--fanned", stack.Find(".toasts").ClassList);
        Assert.Equal("true", stack.Find(".toast__count").GetAttribute("aria-expanded"));
        Assert.Equal("Activities · 2", stack.Find(".toasts__title").TextContent);

        stack.Find(".toasts__capsule").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.DoesNotContain("toasts--fanned", stack.Find(".toasts").ClassList);
        Assert.Equal("false", stack.Find(".toast__count").GetAttribute("aria-expanded"));

        // The stylesheet fans on the one class the component sets, never on :hover or :focus-within.
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        Assert.Contains(".toasts--fanned .toasts__list", css);
        Assert.DoesNotContain(":focus-within, .toasts--open", css);
    }

    [Fact]
    public void Each_is_dismissed_on_its_own_and_Clear_all_takes_the_lot()
    {
        var stack = Stack();
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Info("Second."); Toasts.Info("Third."); });

        // Drawn newest first: Third, Second, First.
        stack.FindAll(".toast__dismiss")[1].Click();
        Assert.Equal(["Third.", "First."], Toasts.Shown.Select(t => t.Message));

        stack.Find(".toasts__clear").Click();
        Assert.Empty(Toasts.Shown);
        Assert.Empty(stack.FindAll(".toast"));
    }

    [Fact]
    public void The_key_runs_its_action_and_takes_the_notice_down()
    {
        var undone = 0;
        var stack = Stack();
        stack.InvokeAsync(() => Toasts.Removed("Dropped a line.", () => undone++));

        var key = stack.Find(".toast__action");
        Assert.Equal("Undo", key.TextContent);
        key.Click();

        Assert.Equal(1, undone);
        Assert.Empty(Toasts.Shown);
    }

    [Fact]
    public void Brief_notices_go_on_their_own_and_failures_and_keys_stay()
    {
        Toasts.Success("Done.");
        Toasts.Info("For your information.");
        Toasts.Warn("Careful.");
        Toasts.Fail("Did not finish.");
        Toasts.Added("Added one.", "View", () => { });
        Toasts.Removed("Removed one.");
        Toasts.Removed("Removed two.", () => { });

        _time.Advance(DeskToasts.BriefLife);

        Assert.Equal(["Removed two.", "Added one.", "Did not finish.", "Careful."], Toasts.Shown.Select(t => t.Message));

        // A warning, and a key that only undoes, go at eight seconds; the rest stand.
        _time.Advance(DeskToasts.WarningLife - DeskToasts.BriefLife);
        Assert.Equal(["Added one.", "Did not finish."], Toasts.Shown.Select(t => t.Message));

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(["Added one.", "Did not finish."], Toasts.Shown.Select(t => t.Message));
    }

    [Fact]
    public void Clocks_stop_while_the_stack_is_held_and_give_time_after()
    {
        Toasts.Success("Done.");
        _time.Advance(TimeSpan.FromSeconds(4));

        Toasts.Held = true;
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(Toasts.Shown);

        // A notice left with a second gets the least time after a hold, not the second.
        Toasts.Held = false;
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(Toasts.Shown);
        _time.Advance(DeskToasts.AfterHold);
        Assert.Empty(Toasts.Shown);
    }

    [Fact]
    public void The_pointer_and_focus_on_the_stack_hold_the_clocks()
    {
        var stack = Stack();
        stack.InvokeAsync(() => Toasts.Success("Done."));

        stack.Find(".toasts__capsule").TriggerEvent("onpointerenter", new PointerEventArgs());
        Assert.True(Toasts.Held);
        stack.Find(".toasts__capsule").TriggerEvent("onfocusin", new FocusEventArgs());
        stack.Find(".toasts__capsule").TriggerEvent("onpointerleave", new PointerEventArgs());
        Assert.True(Toasts.Held);
        stack.Find(".toasts__capsule").TriggerEvent("onfocusout", new FocusEventArgs());
        Assert.False(Toasts.Held);
    }

    [Fact]
    public void Two_live_regions_carry_the_newest_words_a_failure_assertively()
    {
        var stack = Stack();

        var polite = stack.Find("[role=status]");
        var assertive = stack.Find("[role=alert]");
        Assert.Equal("polite", polite.GetAttribute("aria-live"));
        Assert.Equal("assertive", assertive.GetAttribute("aria-live"));
        Assert.Equal("false", polite.GetAttribute("aria-atomic"));
        Assert.Equal("additions", polite.GetAttribute("aria-relevant"));

        stack.InvokeAsync(() => Toasts.Success("Saved."));
        Assert.StartsWith("Saved.", stack.Find("[role=status]").TextContent.Trim());

        stack.InvokeAsync(() => Toasts.Fail("Did not finish."));
        Assert.StartsWith("Did not finish.", stack.Find("[role=alert]").TextContent.Trim());
        Assert.StartsWith("Saved.", stack.Find("[role=status]").TextContent.Trim());

        // The visible stack is not itself live, so nothing is read twice.
        Assert.Null(stack.Find(".toasts").GetAttribute("aria-live"));
        Assert.Null(stack.Find(".toast").GetAttribute("role"));
    }

    [Fact]
    public void The_same_words_twice_are_two_notices()
    {
        Toasts.Info("Again.");
        Toasts.Info("Again.");

        Assert.Equal(2, Toasts.Shown.Count);
        Toasts.Dismiss(Toasts.Shown[0]);
        Assert.Single(Toasts.Shown);
    }

    [Fact]
    public void The_stack_keeps_at_most_its_limit_dropping_the_oldest_timed_first()
    {
        Toasts.Fail("Kept failure.");
        for (var i = 0; i < DeskToasts.MaxKept; i++)
            Toasts.Info($"Info {i}.");

        Assert.Equal(DeskToasts.MaxKept, Toasts.Shown.Count);
        Assert.Contains(Toasts.Shown, t => t.Message == "Kept failure.");
        Assert.DoesNotContain(Toasts.Shown, t => t.Message == "Info 0.");
    }

    [Fact]
    public void A_full_stack_of_notices_that_stay_never_drops_the_one_just_raised()
    {
        var departed = new List<DeskToast>();
        Toasts.Departed += departed.Add;

        for (var i = 0; i < DeskToasts.MaxKept; i++)
            Toasts.Fail($"Failure {i}.");

        // The newest is timed and the rest are not; it is still the one the operator has not read.
        var fresh = Toasts.Raise("Saved.", DeskState.Positive);

        Assert.Equal(DeskToasts.MaxKept, Toasts.Shown.Count);
        Assert.Same(fresh, Toasts.Shown[0]);
        Assert.Equal("Failure 0.", Assert.Single(departed).Message);
    }

    [Fact]
    public void The_window_notice_leaves_with_the_desk()
    {
        var manager = Services.GetRequiredService<WindowManager>();
        var one = manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!);
        var bridge = RenderComponent<DeskNoticeToasts>();
        bridge.InvokeAsync(() => manager.Close(one.Id));
        Assert.Single(Toasts.Shown);

        // The stack lives in the layout and outlasts the desk: a Reopen left on it would walk
        // history back from a desk that is gone.
        DisposeComponents();

        Assert.Empty(Toasts.Shown);
    }

    [Fact]
    public void A_toast_dismissed_with_the_pointer_does_not_leave_the_clocks_held()
    {
        var stack = Stack();
        stack.InvokeAsync(() => Toasts.Info("Closed One."));

        // The pointer comes onto the stack and presses ✕; the toast, and the element the pointer
        // was over, go — no leave event follows.
        stack.Find(".toasts__capsule").TriggerEvent("onpointerenter", new PointerEventArgs());
        Assert.True(Toasts.Held);
        stack.Find(".toast__dismiss").Click();

        Assert.False(Toasts.Held);
        Assert.Single(stack.FindAll(".toasts")); // the container stays, empty and inert
        Assert.True(stack.Find(".toasts").HasAttribute("inert"));

        stack.InvokeAsync(() => Toasts.Success("Saved."));
        _time.Advance(DeskToasts.BriefLife);
        Assert.Empty(Toasts.Shown);
    }

    [Fact]
    public void Escape_folds_the_fan_even_with_the_pointer_still_on_it()
    {
        var stack = Stack();
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Info("Second."); });
        var section = () => stack.Find(".toasts");
        var capsule = () => stack.Find(".toasts__capsule");

        capsule().TriggerEvent("onpointerenter", new PointerEventArgs());
        Assert.Contains("toasts--fanned", section().ClassList);
        Assert.Equal("true", stack.Find(".toast__count").GetAttribute("aria-expanded"));

        capsule().KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.DoesNotContain("toasts--fanned", section().ClassList);
        Assert.Equal("false", stack.Find(".toast__count").GetAttribute("aria-expanded"));

        // A fresh visit fans it again, and the count pressed from the keyboard folds it too.
        capsule().TriggerEvent("onpointerleave", new PointerEventArgs());
        capsule().TriggerEvent("onpointerenter", new PointerEventArgs());
        Assert.Contains("toasts--fanned", section().ClassList);
        stack.Find(".toast__count").Click(Key);
        Assert.DoesNotContain("toasts--fanned", section().ClassList);
        Assert.Equal("false", stack.Find(".toast__count").GetAttribute("aria-expanded"));
    }

    /// <summary>A key pressed with Enter or Space: a click that counts no presses.</summary>
    private static readonly MouseEventArgs Key = new() { Detail = 0 };

    /// <summary>A key pressed with the pointer: a click of one press.</summary>
    private static readonly MouseEventArgs Press = new() { Detail = 1 };

    [Fact]
    public void The_count_pressed_with_the_pointer_pins_a_hovered_fan_open_and_a_second_press_folds_it()
    {
        var stack = Stack();
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Info("Second."); });
        var section = () => stack.Find(".toasts");
        var capsule = () => stack.Find(".toasts__capsule");

        // Hovering already fans it; the press the pointer then makes pins it, so it stays fanned
        // once the pointer has gone.
        capsule().TriggerEvent("onpointerenter", new PointerEventArgs());
        stack.Find(".toast__count").Click(Press);
        Assert.Contains("toasts--fanned", section().ClassList);

        capsule().TriggerEvent("onpointerleave", new PointerEventArgs());
        Assert.Contains("toasts--fanned", section().ClassList);
        Assert.Equal("true", stack.Find(".toast__count").GetAttribute("aria-expanded"));

        // Pinned, the count folds it.
        capsule().TriggerEvent("onpointerenter", new PointerEventArgs());
        stack.Find(".toast__count").Click(Press);
        Assert.DoesNotContain("toasts--fanned", section().ClassList);
        Assert.Equal("false", stack.Find(".toast__count").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_notice_dismissed_from_the_keyboard_hands_focus_to_the_next_then_back()
    {
        var module = JSInterop.SetupModule(DeskAssets.Module("toasts.js"));
        var stack = Stack();
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Info("Second."); });

        // Tab brings focus in (toasts.js passes on only focus the keyboard put there), and Enter
        // presses the dismiss key: a click that counts no presses.
        stack.Find(".toasts__capsule").TriggerEvent("onfocusin", new FocusEventArgs());
        Assert.True(Toasts.Held);
        var first = Toasts.Shown.Single(t => t.Message == "First.");
        var second = Toasts.Shown.Single(t => t.Message == "Second.");

        stack.FindAll(".toast__dismiss")[0].Click(Key); // Second, the newest, drawn first
        var moved = module.Invocations["refocus"].Last();
        Assert.Equal(first.Id, moved.Arguments[1]);
        Assert.True(Toasts.Held);

        stack.Find(".toast__dismiss").Click(Key);
        Assert.Null(module.Invocations["refocus"].Last().Arguments[1]);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void A_notice_dismissed_with_the_pointer_moves_no_focus_and_holds_nothing_once_the_pointer_leaves()
    {
        var module = JSInterop.SetupModule(DeskAssets.Module("toasts.js"));
        var stack = Stack();
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Info("Second."); Toasts.Info("Third."); });
        var section = () => stack.Find(".toasts");
        var capsule = () => stack.Find(".toasts__capsule");

        // The pointer comes on, and the browser focuses the key it presses.
        capsule().TriggerEvent("onpointerenter", new PointerEventArgs());
        capsule().TriggerEvent("onfocusin", new FocusEventArgs());
        stack.FindAll(".toast__dismiss")[0].Click(Press);

        Assert.Equal(2, Toasts.Shown.Count);
        Assert.Empty(module.Invocations["refocus"]);

        // Leaving lets go: the clocks run and the stack folds, without a click elsewhere.
        capsule().TriggerEvent("onpointerleave", new PointerEventArgs());
        Assert.False(Toasts.Held);
        Assert.DoesNotContain("toasts--fanned", section().ClassList);
    }

    [Fact]
    public void Notices_raised_together_are_announced_together_in_order_and_cleared_after()
    {
        var stack = Stack();
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Success("Second."); });

        Assert.Equal(["First.", "Second."], stack.Find("[role=status]").QuerySelectorAll("p").Select(p => p.TextContent));

        stack.Find(".toasts__clear").Click();
        Assert.Empty(stack.Find("[role=status]").QuerySelectorAll("p"));
    }

    [Fact]
    public void A_key_that_only_undoes_is_timed_and_one_that_leads_on_stays()
    {
        var undo = Toasts.RaiseUndo("Closed One.", "Reopen One", () => { });
        var lead = Toasts.Raise("Added one.", DeskState.Positive, "Show", () => { });

        Assert.Equal(DeskToasts.UndoLife, DeskToasts.LifeOf(undo));
        Assert.Null(DeskToasts.LifeOf(lead));

        // Held while read, like any timed notice.
        Toasts.Held = true;
        _time.Advance(DeskToasts.UndoLife * 2);
        Assert.Equal(2, Toasts.Shown.Count);
        Toasts.Held = false;
        _time.Advance(DeskToasts.UndoLife);
        Assert.Equal(["Added one."], Toasts.Shown.Select(t => t.Message));
    }

    private static string DeskCss()
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css")).Replace("\r\n", "\n");

    private static string RuleOf(string css, string selector)
        => Regex.Match(css, $@"(?m)^{Regex.Escape(selector)}\s*\{{[^}}]*\}}").Value;

    [Fact]
    public void The_stack_lives_in_the_header_between_the_strip_and_the_keys()
    {
        var header = RenderComponent<DeskHeader>();

        // In the bar, after the window strip and before the desk's own keys: the free stretch.
        var children = header.Find(".hdr").Children.Select(c => c.ClassList).ToList();
        var bar = children.FindIndex(c => c.Contains("bar"));
        var stack = children.FindIndex(c => c.Contains("toasts"));
        var tools = children.FindIndex(c => c.Contains("hdr__tools"));
        Assert.True(bar >= 0 && bar < stack && stack < tools, "the stack sits between the strip and the keys");

        // The layout no longer floats one over the page.
        var layout = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Layout", "MainLayout.razor"));
        Assert.DoesNotContain("<XLines.Desk.Primitives.DeskToastStack", layout);

        // It grows into free space only (basis 0), is a title bar tall, measures itself, and
        // never floats: no fixed position, no z-index of its own.
        var css = DeskCss();
        var rule = RuleOf(css, ".toasts");
        Assert.Contains("flex: 9 1 0;", rule);
        Assert.Contains("container: activities / inline-size;", rule);
        Assert.Contains("height: var(--titlebar-h);", rule);
        Assert.DoesNotContain("position: fixed", rule);
        Assert.DoesNotContain("z-index", rule);
    }

    [Fact]
    public void Layers_step_out_sideways_and_the_fan_drops_beneath_the_capsule()
    {
        var css = DeskCss();

        // The edges behind step out to the right, a little shorter each.
        Assert.Contains(".toasts--stacked:not(.toasts--fanned) .toasts__list { padding-right: calc(2 * var(--toast-step)); }", css);
        Assert.Contains(".toast--depth-1 { z-index: 2; translate: var(--toast-step) 0; scale: 1 .88;", css);
        Assert.Contains(".toast--depth-2 { z-index: 1; translate: calc(2 * var(--toast-step)) 0; scale: 1 .76;", css);
        Assert.Contains("isolation: isolate;", RuleOf(css, ".toasts__list"));

        // Fanned, the list drops beneath as a popover would — with no z-index, so the header's
        // own menus and drawers draw over it.
        var fanned = RuleOf(css, ".toasts--fanned .toasts__list");
        Assert.Contains("position: absolute;", fanned);
        Assert.Contains("top: calc(100% + var(--space-2));", fanned);
        Assert.DoesNotContain("z-index", fanned);

        // Too narrow for a notice: the key alone, in words.
        Assert.Contains("@container activities (max-width: 240px)", css);

        // It slides in along the bar and settles; under reduced motion it only fades.
        Assert.Matches(@"@keyframes toast-arrive\s*\{\s*from\s*\{[^}]*translateX\(16px\)", css);
        Assert.Matches(@"@media \(prefers-reduced-motion: reduce\)\s*\{\s*\.toast\s*\{[^}]*animation-name:\s*toast-fade", css);

        // What the floating stack needed is gone, not left behind.
        Assert.DoesNotContain("--z-under", css);
        Assert.DoesNotContain("--header-edge", css);
        Assert.DoesNotContain(".mud-overlay-dialog) .toasts", css);
        Assert.DoesNotContain("block\"]) .toasts", css);
        var js = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "js", "toasts.js"));
        Assert.DoesNotContain("--header-edge", js);

        // The connection banner keeps a place of its own, above the footer.
        Assert.Contains("bottom: calc(var(--footer-h) + var(--space-8));", RuleOf(css, "#blazor-error-ui"));
    }

    [Fact]
    public void A_long_message_and_a_long_key_both_cut_to_one_line_and_keep_their_words()
    {
        const string message = "Closed Every book · Minnesota Vikings at San Francisco 49ers.";
        const string label = "Reopen Every book · Minnesota Vikings at San Francisco 49ers";

        var stack = Stack();
        stack.InvokeAsync(() => Toasts.RaiseUndo(message, label, () => { }));

        // The whole of each is its title (and the key's name is its full text).
        Assert.Equal(message, stack.Find(".toast__text").GetAttribute("title"));
        Assert.Equal(label, stack.Find(".toast__action").GetAttribute("title"));
        Assert.Equal(label, stack.Find(".toast__action").TextContent);

        // The words keep a few words' width and are cut, never broken letter by letter; the key
        // gives way first, with an ellipsis. Fanned, the words wrap by words.
        var css = DeskCss();
        var text = RuleOf(css, ".toast__text");
        Assert.Contains("min-width: 8ch;", text);
        Assert.Contains("white-space: nowrap;", text);
        Assert.Contains("text-overflow: ellipsis;", text);
        Assert.DoesNotContain("anywhere", text);

        var action = RuleOf(css, ".toast__action");
        Assert.Contains("max-width: 45%;", action);
        Assert.Contains("text-overflow: ellipsis;", action);

        var fanned = RuleOf(css, ".toasts--fanned .toast__text");
        Assert.Contains("overflow-wrap: break-word;", fanned);
        Assert.Contains("min-width: 12ch;", fanned);
    }

    [Fact]
    public void Where_only_the_key_fits_one_notice_is_opened_from_it()
    {
        var stack = Stack();
        stack.InvokeAsync(() => Toasts.Info("Only."));

        // The key is on the page for a narrow header (the stylesheet shows it there), named.
        var key = stack.Find(".toast__count");
        Assert.Equal("Activity · 1", key.QuerySelector(".toasts__title")!.TextContent);
        Assert.Equal("Show 1 activity", key.GetAttribute("aria-label"));

        // Pointing at one notice does not fan it; the key does.
        stack.Find(".toasts__capsule").TriggerEvent("onpointerenter", new PointerEventArgs());
        Assert.DoesNotContain("toasts--fanned", stack.Find(".toasts").ClassList);
        stack.Find(".toast__count").Click(new MouseEventArgs { Detail = 1 });
        Assert.Contains("toasts--fanned", stack.Find(".toasts").ClassList);
        Assert.Equal("Fold 1 activity", stack.Find(".toast__count").GetAttribute("aria-label"));
        Assert.Equal(stack.Find(".toasts__list").Id, stack.Find(".toast__count").GetAttribute("aria-controls"));
    }


    [Fact]
    public void The_capsule_not_the_slot_hears_the_pointer_and_focus()
    {
        var stack = Stack();
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Info("Second."); });

        // The slot spans the header's free stretch; crossing empty header must not fan or hold.
        Assert.ThrowsAny<Exception>(() => stack.Find(".toasts").TriggerEvent("onpointerenter", new PointerEventArgs()));
        Assert.False(Toasts.Held);

        stack.Find(".toasts__capsule").TriggerEvent("onpointerenter", new PointerEventArgs());
        Assert.True(Toasts.Held);
        Assert.Contains("toasts--fanned", stack.Find(".toasts").ClassList);

        // The capsule holds the count, Clear all and the list.
        var capsule = stack.Find(".toasts__capsule");
        Assert.NotNull(capsule.QuerySelector(".toast__count"));
        Assert.NotNull(capsule.QuerySelector(".toasts__clear"));
        Assert.NotNull(capsule.QuerySelector(".toasts__list"));

        // Empty, the slot takes no room from the strip and no gap.
        var css = DeskCss();
        var empty = RuleOf(css, ".toasts--empty");
        Assert.Contains("flex: 0 0 0;", empty);
        Assert.Contains("min-width: 0;", empty);
        Assert.DoesNotContain("gap", RuleOf(css, ".toasts"));
    }

    [Fact]
    public void The_way_from_the_capsule_to_the_list_is_bridged_and_a_leave_waits()
    {
        // The gap between the capsule and the list beneath it is part of the capsule.
        var css = DeskCss();
        var bridge = RuleOf(css, ".toasts--fanned .toasts__capsule::after");
        Assert.Contains("top: 100%;", bridge);
        Assert.Contains("height: calc(var(--space-2) + 1px);", bridge);
        Assert.Contains("top: calc(100% + var(--space-2));", RuleOf(css, ".toasts--fanned .toasts__list"));

        // The default grace is short and not zero.
        var grace = new DeskToastStack().LeaveGrace;
        Assert.InRange(grace, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public void A_leave_folds_only_after_the_grace_and_a_return_inside_it_keeps_the_list()
    {
        var stack = RenderComponent<DeskToastStack>(p => p.Add(x => x.LeaveGrace, TimeSpan.FromMilliseconds(120)));
        stack.InvokeAsync(() => { Toasts.Info("First."); Toasts.Info("Second."); });
        var capsule = () => stack.Find(".toasts__capsule");
        var fanned = () => stack.Find(".toasts").ClassList.Contains("toasts--fanned");

        capsule().TriggerEvent("onpointerenter", new PointerEventArgs());
        Assert.True(fanned());

        // Leaving and coming back inside the grace is not leaving.
        capsule().TriggerEvent("onpointerleave", new PointerEventArgs());
        Assert.True(fanned());
        capsule().TriggerEvent("onpointerenter", new PointerEventArgs());
        Thread.Sleep(300);
        Assert.True(fanned());
        Assert.True(Toasts.Held);

        // Leaving for longer folds it and lets the clocks run.
        capsule().TriggerEvent("onpointerleave", new PointerEventArgs());
        stack.WaitForAssertion(() => Assert.False(fanned()), TimeSpan.FromSeconds(3));
        Assert.False(Toasts.Held);
    }

    [Fact]
    public void Focus_moves_on_only_from_the_stack_and_never_out_of_a_window_an_action_opened()
    {
        var js = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "js", "toasts.js"));

        // refocus leaves focus where an action put it: anywhere real outside the stack.
        Assert.Contains("if (active && active !== document.body && active.isConnected && !section.contains(active)) return;", js);

        // Escape folding the list takes focus off an older notice onto the count's key.
        Assert.Matches(@"e\.key !== 'Escape'[\s\S]*?toast--front[\s\S]*?\.toast__count", js);

        // With nowhere to go back to: the header's first key, else the desk, which can take focus.
        Assert.Contains("document.querySelector('.hdr button:not([disabled])')", js);
        var desk = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Windowing", "Desk.razor"));
        Assert.Contains("<main class=\"desk\" data-desk aria-label=\"Desk\" tabindex=\"-1\">", desk);
    }

    [Fact]
    public void Past_the_limit_a_failure_outlasts_every_other_kind()
    {
        // Eleven failures and a key that leads on — none timed — then one more failure.
        for (var i = 0; i < DeskToasts.MaxKept - 1; i++)
            Toasts.Fail($"Failure {i}.");
        Toasts.Added("Added one.", "Show", () => { });

        Toasts.Fail("One more failure.");

        Assert.Equal(DeskToasts.MaxKept, Toasts.Shown.Count);
        Assert.DoesNotContain(Toasts.Shown, t => t.Message == "Added one.");
        Assert.Contains(Toasts.Shown, t => t.Message == "Failure 0.");
        Assert.Equal(DeskToasts.MaxKept, Toasts.Shown.Count(t => t.State == DeskState.Negative));
    }

    // ------------------------------------------------------- the window notice ----

    [Fact]
    public void A_late_clear_for_an_older_notice_leaves_a_newer_one()
    {
        var manager = Services.GetRequiredService<WindowManager>();
        var one = manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!);
        manager.Close(one.Id);
        var older = manager.Notice!;

        manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!);
        manager.Close(manager.Windows.Single().Id);
        var newer = manager.Notice!;

        Assert.False(manager.ClearNotice(older));
        Assert.Same(newer, manager.Notice);
        Assert.True(manager.ClearNotice(newer));
        Assert.Null(manager.Notice);
    }

    [Fact]
    public void A_refusal_that_expires_after_a_close_does_not_take_the_close_s_notice()
    {
        var manager = Services.GetRequiredService<WindowManager>();
        var bridge = RenderComponent<DeskNoticeToasts>();
        var two = manager.Catalog.Find(FakeWindowCatalog.Two)!;

        bridge.InvokeAsync(() =>
        {
            for (var i = 0; i <= DeskSettings.MaxOpenWindows; i++)
                manager.Open(two, new Dictionary<string, object> { ["Id"] = i });
        });
        Assert.Equal(DeskState.Warning, Assert.Single(Toasts.Shown).State);
        _time.Advance(TimeSpan.FromSeconds(5));

        bridge.InvokeAsync(() => manager.Close(manager.Windows[0].Id));
        var closed = manager.Notice;
        Assert.StartsWith("Closed", closed);

        // Past the moment the refusal was due; the close's toast still has its own time.
        _time.Advance(TimeSpan.FromSeconds(4));

        Assert.Same(closed, manager.Notice);
        Assert.Equal(closed, Assert.Single(Toasts.Shown).Message);
    }

    [Fact]
    public void The_window_manager_s_notice_is_a_toast_with_its_way_back()
    {
        var manager = Services.GetRequiredService<WindowManager>();
        var one = manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!);
        var bridge = RenderComponent<DeskNoticeToasts>();
        var stack = Stack();

        bridge.InvokeAsync(() => manager.Close(one.Id));

        var toast = Assert.Single(Toasts.Shown);
        Assert.Equal("Closed One.", toast.Message);
        Assert.Equal("Reopen One", toast.ActionLabel);
        Assert.Equal("Reopen One", stack.Find(".toast__action").TextContent);

        // A new sentence replaces the last one's toast; clearing the sentence takes it down.
        bridge.InvokeAsync(() => manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!));
        Assert.Empty(Toasts.Shown);
    }

    [Fact]
    public void The_ceiling_s_refusal_is_a_warning_that_goes_on_its_own_and_clears_the_sentence()
    {
        var manager = Services.GetRequiredService<WindowManager>();
        var bridge = RenderComponent<DeskNoticeToasts>();
        var two = manager.Catalog.Find(FakeWindowCatalog.Two)!;

        bridge.InvokeAsync(() =>
        {
            for (var i = 0; i <= DeskSettings.MaxOpenWindows; i++)
                manager.Open(two, new Dictionary<string, object> { ["Id"] = i });
        });

        var toast = Assert.Single(Toasts.Shown);
        Assert.Equal(DeskState.Warning, toast.State);
        Assert.Null(toast.ActionLabel);
        Assert.NotNull(manager.Notice);

        _time.Advance(DeskToasts.WarningLife);
        bridge.WaitForAssertion(() => Assert.Null(manager.Notice));
        Assert.Empty(Toasts.Shown);
    }

    [Fact]
    public void Dismissing_the_notice_toast_clears_the_manager_s_sentence()
    {
        var manager = Services.GetRequiredService<WindowManager>();
        var one = manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!);
        var bridge = RenderComponent<DeskNoticeToasts>();
        var stack = Stack();
        bridge.InvokeAsync(() => manager.Close(one.Id));

        stack.Find(".toast__dismiss").Click();

        bridge.WaitForAssertion(() => Assert.Null(manager.Notice));
    }

    [Fact]
    public void The_header_draws_no_notice()
    {
        var manager = Services.GetRequiredService<WindowManager>();
        var one = manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!);
        manager.Close(one.Id);
        Assert.NotNull(manager.Notice);

        var header = RenderComponent<DeskHeader>();

        // No sentence of its own: the only live regions in the header are the Activities stack's
        // two hidden logs, and the manager's sentence reaches them only as a toast.
        Assert.DoesNotContain("Closed One.", header.Markup);
        Assert.All(header.FindAll("[role=status]"), log => Assert.Contains("sr-only", log.ClassList));
        Assert.Empty(header.FindAll(".hdr__notice"));
    }

    [Fact]
    public void The_header_draws_the_stack_and_nothing_draws_a_snackbar()
    {
        var header = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Windowing", "DeskHeader.razor"));
        Assert.Contains("<XLines.Desk.Primitives.DeskToastStack />", header);

        var layout = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Layout", "MainLayout.razor"));
        Assert.DoesNotContain("<MudSnackbarProvider", layout);

        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        Assert.DoesNotContain(".hdr__notice", css);
    }
}

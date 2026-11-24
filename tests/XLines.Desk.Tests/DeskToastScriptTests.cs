using XLines.Desk.Tests.Support;

namespace XLines.Desk.Tests;

/// <summary>
/// The browser halves of the toast stack and the popover (toasts.js, popover.js), which bUnit
/// cannot run. What is pinned here is that each script still does the thing the C# relies on;
/// how it looks and moves is checked by hand in a browser.
/// </summary>
public class DeskToastScriptTests
{
    private static string Script(string name)
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "js", name));

    [Fact]
    public void Toasts_js_passes_on_only_focus_the_keyboard_put_there()
    {
        var js = Script("toasts.js");

        // Pointer focus (a pressed key the browser focused) is not reported, so it neither holds
        // the stack nor moves focus on when its notice goes.
        Assert.Contains(":focus-visible", js);
    }

    [Fact]
    public void Toasts_js_hears_focus_moving_inside_the_stack_as_no_change()
    {
        var js = Script("toasts.js");

        // Tab between the stack's keys is a focusout and a focusin; the focusout whose next stop is
        // inside the stack goes no further, so an Escape's fold holds and nothing folds per Tab.
        Assert.Matches(@"'focusout'[\s\S]*section\.contains\(e\.relatedTarget\)[\s\S]*stopPropagation", js);
    }

    [Fact]
    public void An_Escape_the_stack_handled_does_not_also_close_a_popover()
    {
        Assert.Contains("preventDefault", Script("toasts.js"));
        Assert.Matches(@"event\.key !== 'Escape' \|\| event\.defaultPrevented", Script("popover.js"));
    }

    [Fact]
    public void The_popover_measures_without_its_entrance_and_never_overruns_the_room()
    {
        var js = Script("popover.js");

        // The entrance's lift is taken out of the measurement, and the panel is held to the room
        // there is, however little.
        Assert.Contains("liftOf(panel)", js);
        Assert.DoesNotContain("Math.max(MIN_HEIGHT, room)", js);
    }

    [Fact]
    public void Toasts_js_brings_the_front_notice_into_view_when_the_stack_fans()
    {
        Assert.Contains("toasts--fanned", Script("toasts.js"));
    }
}

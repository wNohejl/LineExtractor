using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace LineOps.Web.Components.Desk;

/// <summary>
/// The mechanics every field shares, so the three call-site components stay thin.
///
/// A field is the key's inverse — cut into the panel, unlit, not pressable (ADR 0008).
/// The look is entirely <c>.field</c> in lineops.css and this class does not touch it;
/// what it adds is the part the hand-authored markup never had: a real label bound to
/// the control by id, a required mark, and a place for an error to sit next to the
/// field rather than somewhere else on the panel.
///
/// Two rules keep it honest at the call sites that already exist:
///
///   · <b>The frame is opt-in.</b> With no <see cref="Label"/> and no <see cref="Error"/>
///     the control renders bare — no wrapper element. Every field on the desk today is a
///     direct child of a <c>.row</c> flexbox carrying its own <c>flex</c>/<c>max-width</c>,
///     and quietly wrapping it in a div would move all of them. A frame appears only when
///     there is something to put in it.
///   · <b>The caller never loses an attribute, and never overwrites ours.</b> Blazor applies
///     a splat last, so an element that sets <c>class</c> itself and also splats hands any
///     caller passing <c>class</c> the power to erase it — the bug written up in
///     2026-08-26-gate-switch-and-glide-design.md, where an incoming <c>style</c> dropped
///     DeskSwitch's own custom properties and collapsed the control. Here every attribute is
///     merged into one dictionary in <see cref="Attributes"/> before rendering, so ordering
///     stops deciding anything: <c>class</c> and <c>style</c> are composed by the component,
///     and everything else the caller passes (placeholder, type, min, max, step, disabled)
///     lands untouched.
/// </summary>
/// <typeparam name="TValue">
/// What the field holds. <c>string</c> needs no conversion; numeric and enum types round-trip
/// through <see cref="BindConverter"/> under the invariant culture, which is what Blazor's own
/// <c>@bind</c> does for <c>type="number"</c> and is therefore what the migrated call sites had.
/// </typeparam>
public abstract class DeskFieldBase<TValue> : ComponentBase
{
    private static int _seed;

    private readonly string _instance = $"fld{Interlocked.Increment(ref _seed)}";

    /// <summary>The bound value. Use <c>@bind-Value</c>; a one-way <c>Value</c> alone also renders.</summary>
    [Parameter] public TValue? Value { get; set; }

    /// <summary>
    /// Deliberately <c>EventCallback&lt;TValue&gt;</c> rather than <c>TValue?</c>: half the
    /// desk's fields bind to non-nullable string properties, and a nullable callback would
    /// make every one of those call sites warn on assignment.
    /// </summary>
    [Parameter] public EventCallback<TValue> ValueChanged { get; set; }

    /// <summary>
    /// The field's name, rendered as a real <c>&lt;label for&gt;</c>. A placeholder is not a
    /// label — it leaves as soon as there is text in the box, which is exactly when someone
    /// checking their work needs to read it.
    /// </summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Marks the control <c>required</c> and prints the mark beside the label.</summary>
    [Parameter] public bool Required { get; set; }

    /// <summary>
    /// What is wrong with the current entry. Renders next to the field, announces itself,
    /// and points the control at it through <c>aria-describedby</c>.
    /// </summary>
    [Parameter] public string? Error { get; set; }

    /// <summary>Tabular figures, for a field whose content is a number.</summary>
    [Parameter] public bool Mono { get; set; }

    /// <summary>Extra classes, appended last so a one-off can still win.</summary>
    [Parameter] public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? Extra { get; set; }

    /// <summary>The id shared by the label's <c>for</c> and the control, generated per instance.</summary>
    protected string FieldId => $"{_instance}-field";

    protected string ErrorId => $"{_instance}-error";

    protected bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>
    /// Whether there is anything to wrap the control in. Bare otherwise — see the frame rule
    /// on the class itself.
    /// </summary>
    protected bool Framed => !string.IsNullOrWhiteSpace(Label) || HasError;

    /// <summary>
    /// The DOM event that commits a value. <c>change</c> — that is, on blur or on picking —
    /// is the default because validating a half-typed entry on every keystroke tells people
    /// they are wrong while they are still saying it. Fields that genuinely filter as you
    /// type opt in.
    /// </summary>
    protected virtual string ChangeEvent => "onchange";

    /// <summary>
    /// The caller's <c>style</c>, which the component moves rather than merges: onto the
    /// wrapper when there is one (it is layout, and the wrapper is what sits in the row),
    /// onto the control when there is not.
    /// </summary>
    protected string? Style
        => Extra is not null
           && Extra.TryGetValue("style", out var declared)
           && declared?.ToString() is { Length: > 0 } value
            ? value
            : null;

    /// <summary>The control's classes. <c>.field</c> always, then the opt-ins, then the caller's.</summary>
    protected string ControlClass
        => string.Join(' ', new[] { "field", Mono ? "num" : null, Class }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>
    /// Every attribute the control carries, resolved in one place so that render order
    /// cannot decide a winner. The component's own accessibility wiring goes in first, the
    /// caller's splat goes in second and wins any key it names — except <c>class</c> and
    /// <c>style</c>, which are composed above and are not merged here at all.
    /// </summary>
    protected IReadOnlyDictionary<string, object> Attributes
    {
        get
        {
            var attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = FieldId,
                ["class"] = ControlClass
            };

            if (!Framed)
            {
                // Unwrapped, the control is the thing in the row, so it takes the layout.
                if (Style is { } style)
                    attributes["style"] = style;
            }

            if (Required)
            {
                attributes["required"] = true;
                attributes["aria-required"] = "true";
            }

            if (HasError)
            {
                attributes["aria-invalid"] = "true";
                attributes["aria-describedby"] = ErrorId;
            }

            // Nothing here names the control: a <label for> already does that, and adding
            // aria-labelledby on top would be a second mechanism saying the same thing.
            // Without a label, the caller's own aria-label — splatted below — is what names it.
            if (FormatValue(Value) is { } formatted)
                attributes["value"] = formatted;

            if (ValueChanged.HasDelegate)
            {
                attributes[ChangeEvent] = EventCallback.Factory.Create<ChangeEventArgs>(
                    this, OnChangeAsync);
            }

            if (Extra is not null)
            {
                foreach (var (key, value) in Extra)
                {
                    if (!string.Equals(key, "class", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(key, "style", StringComparison.OrdinalIgnoreCase))
                    {
                        attributes[key] = value;
                    }
                }
            }

            return attributes;
        }
    }

    private async Task OnChangeAsync(ChangeEventArgs e)
        => await ValueChanged.InvokeAsync(ParseValue(e.Value));

    /// <summary>
    /// <c>null</c> means "render no value attribute at all", which is how a call site that
    /// still drives the control itself — passing raw <c>value</c> and <c>@onchange</c> —
    /// keeps working: nothing of ours is there to collide with what it splats.
    /// </summary>
    private static string? FormatValue(TValue? value) => value switch
    {
        null => null,
        string s => s,
        _ => BindConverter.FormatValue(value, CultureInfo.InvariantCulture)?.ToString()
    };

    /// <summary>
    /// An entry that will not convert leaves the value alone rather than snapping it to
    /// <c>default</c>: clearing a number field should not silently write a zero.
    /// </summary>
    private TValue ParseValue(object? raw)
    {
        var text = raw?.ToString();

        if (typeof(TValue) == typeof(string))
            return (TValue)(object)(text ?? string.Empty);

        if (string.IsNullOrEmpty(text))
            return default!;

        return BindConverter.TryConvertTo<TValue>(text, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : Value!;
    }

    /// <summary>The label's id, so the control can point back at it.</summary>
    protected string LabelId => $"{_instance}-label";
}

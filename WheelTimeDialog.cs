using System.Globalization;
using DotNative;

namespace DotNative.WheelTimePicker;

internal sealed class WheelTimeDialog : Component, IDisposable
{
    private readonly TimePickerOptions options;
    private readonly Action<TimeOnly?> close;
    private readonly IUiDispatcher dispatcher;
    private readonly State<TimeOnly> selected;
    private readonly AnimationController fade;
    private readonly WheelPicker hour;
    private readonly WheelPicker minute;
    private readonly WheelPicker? period;
    private readonly FontSource? itemFont;
    private readonly FontSource? titleFont;
    private readonly FontSource? actionFont;
    private bool closing;
    private bool disposed;

    private static Color ColorOf(uint bits) =>
        new((byte)(bits >> 16), (byte)(bits >> 8), (byte)bits, (byte)(bits >> 24));

    internal WheelTimeDialog(
        TimePickerOptions options,
        Action<TimeOnly?> close,
        IUiDispatcher dispatcher
    )
    {
        this.options = options;
        this.close = close;
        this.dispatcher = dispatcher;
        var initial = TimeWheel.Snap(
            options.InitialTime ?? TimeOnly.FromDateTime(DateTime.Now),
            options.MinuteInterval
        );
        selected = new(initial);
        itemFont = options.FontData is { } data
            ? FontSource.FromBytes(options.FontFamily, data)
            : null;
        titleFont = options.TitleFontData is { } title
            ? FontSource.FromBytes(options.TitleFontFamily, title)
            : null;
        actionFont = options.ActionFontData is { } action
            ? FontSource.FromBytes(options.ActionFontFamily, action)
            : null;
        fade = new(dispatcher);
        WheelPicker Wheel(string[] items, int index, Action<int> changed) =>
            new(items, index, changed, dispatcher)
            {
                Looping = options.Looping,
                ShowDividers = false,
                Font = itemFont,
                FontSize = options.FontSize,
                Foreground = ColorOf(options.ItemColor),
            };
        var hours = options.Is24Hour
            ? Labels(0, 23)
            : Enumerable
                .Range(1, 12)
                .Select(value => value.ToString("00", CultureInfo.InvariantCulture))
                .ToArray();
        hour = Wheel(hours, HourIndex(initial), index => SelectHour(index));
        minute = Wheel(
            TimeWheel.MinuteLabels(options.MinuteInterval),
            initial.Minute / options.MinuteInterval,
            index => SelectMinute(index)
        );
        period = options.Is24Hour
            ? null
            : Wheel(
                TimeWheel.PeriodLabels(options.Locale),
                initial.Hour >= 12 ? 1 : 0,
                index => SelectPeriod(index)
            );
        dispatcher.Post(() =>
        {
            if (!disposed)
                _ = fade.AnimateToAsync(1, TimeSpan.FromMilliseconds(150), Easings.EaseOut);
        });
    }

    private static string[] Labels(int first, int last) =>
        Enumerable
            .Range(first, last - first + 1)
            .Select(value => value.ToString("00", CultureInfo.InvariantCulture))
            .ToArray();

    private int HourIndex(TimeOnly time) =>
        options.Is24Hour ? time.Hour : (time.Hour + 11) % 12;

    private void SelectHour(int index)
    {
        var time = selected.Value;
        int hour;
        if (options.Is24Hour)
            hour = index;
        else
        {
            hour = (index + 1) % 12;
            if (time.Hour >= 12)
                hour += 12;
        }
        selected.Value = new TimeOnly(hour, time.Minute);
    }

    private void SelectMinute(int index) =>
        selected.Value = new TimeOnly(selected.Value.Hour, index * options.MinuteInterval);

    private void SelectPeriod(int index)
    {
        var hour = selected.Value.Hour % 12;
        if (index == 1)
            hour += 12;
        selected.Value = new TimeOnly(hour, selected.Value.Minute);
    }

    private async Task FinishAsync(TimeOnly? value)
    {
        if (closing || disposed)
            return;
        closing = true;
        try
        {
            await fade.AnimateToAsync(0, TimeSpan.FromMilliseconds(150), Easings.EaseOut);
            if (!disposed)
                close(value);
        }
        catch (OperationCanceledException) { }
    }

    public override Element Build(BuildContext context)
    {
        var viewport = context.Get<ViewportSize>();
        var width = Math.Max(1, Math.Min(328, viewport.Width - 80));
        var titleHeight = MathF.Ceiling(options.TitleFontSize * 4 / 3) * 2;
        var height = 24 + titleHeight + 160 + 72;
        var ink = ColorOf(options.TextColor);
        var columns = period is null ? 2 : 3;
        Text Title() =>
            titleFont is { } font
                ? new Text(options.Title).FontSource(font).FontWeight(FontWeight.Bold)
                : new Text(options.Title).FontWeight(FontWeight.Bold);
        Button Action(string text, Action action) =>
            (
                actionFont is { } font
                    ? new Button(text, action).FontSource(font)
                    : new Button(text, action)
            )
                .FontSize(options.ActionFontSize)
                .FontWeight(FontWeight.SemiBold)
                .ForegroundColor(ink)
                .MinWidth(64)
                .Height(48)
                .Padding(12, 0)
                .CornerRadius(20)
                .OnState(InteractionState.Pressed, new(Background: new Color(0, 0, 0, 26)));
        var dividerSpacing = viewport.Width * 0.02f;
        Element Divider(float top)
        {
            Element Segment() =>
                new Container(
                    new Container()
                        .BackgroundColor(ColorOf(options.ItemColor))
                        .Position(PositionMode.Absolute)
                        .Left(dividerSpacing)
                        .Right(dividerSpacing)
                        .Top(0)
                        .Height(2)
                )
                    .Width(0)
                    .Grow()
                    .Height(2);
            var segments = Enumerable.Range(0, columns).Select(_ => Segment()).ToArray();
            return new HStack(segments)
                .Position(PositionMode.Absolute)
                .Left(14)
                .Right(14)
                .Top(top)
                .Height(2);
        }
        Element Column(WheelPicker wheel, string key) =>
            new Container(wheel).Width(0).Grow().WithKey(key);
        var wheels = period is { } dayPeriod
            ? new Element[] { Column(hour, "hour"), Column(minute, "minute"), Column(dayPeriod, "period") }
            : new Element[] { Column(hour, "hour"), Column(minute, "minute") };
        var panel = new VStack(
            new Button("", () => { })
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
                .WidthPercent(100)
                .HeightPercent(100),
            Title()
                .FontSize(options.TitleFontSize)
                .MaxLines(2)
                .TextOverflow(TextOverflow.Wrap)
                .ForegroundColor(ink)
                .Position(PositionMode.Absolute)
                .Left(24)
                .Right(24)
                .Top(24)
                .Height(titleHeight),
            new HStack(wheels)
                .FlowDirection(FlowDirection.LeftToRight)
                .Position(PositionMode.Absolute)
                .Left(14)
                .Right(14)
                .Top(24 + titleHeight)
                .Height(160),
            Divider(24 + titleHeight + 80 - 18 - 1),
            Divider(24 + titleHeight + 80 + 18 - 1),
            new HStack(
                Action(options.ConfirmText, () => _ = FinishAsync(selected.Value)),
                Action(options.CancelText, () => _ = FinishAsync(null))
            )
                .Spacing(8)
                .MainAlignment(MainAxisAlignment.End)
                .Position(PositionMode.Absolute)
                .Left(24)
                .Right(24)
                .Top(height - 72)
                .Height(48)
        )
            .Width(width)
            .Height(height)
            .CornerRadius(28)
            .BackgroundColor(ColorOf(options.BackgroundColor))
            .ClipToBounds()
            .Position(PositionMode.Absolute)
            .Left((viewport.Width - width) / 2)
            .Top(
                options.SafeArea.Top
                    + (viewport.Height - options.SafeArea.Top - options.SafeArea.Bottom - height)
                        / 2
            );
        return new VStack(
            new Button("", () => _ = FinishAsync(null))
                .WidthPercent(100)
                .HeightPercent(100)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
                .BackgroundColor(new Color(0, 0, 0, 138)),
            panel
        )
            .WidthPercent(100)
            .HeightPercent(100)
            .Opacity(fade.Value);
    }

    public void Dispose()
    {
        disposed = true;
        fade.Dispose();
        hour.Dispose();
        minute.Dispose();
        period?.Dispose();
    }
}

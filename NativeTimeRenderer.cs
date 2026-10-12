using System.Globalization;
using DotNative.Plugins;

namespace DotNative.WheelTimePicker;

internal sealed class NativeTimeRenderer(IPlatformChannels channels, TimePickerOptions defaults)
    : IWheelTimePicker
{
    private readonly MethodChannel channel = channels.Get("dotnative.wheeltimepicker");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<TimeOnly?> ShowAsync(
        TimePickerOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var value = options ?? defaults;
        TimeWheel.ValidateInterval(value.MinuteInterval);
        var time = TimeWheel.Snap(
            value.InitialTime ?? TimeOnly.FromDateTime(DateTime.Now),
            value.MinuteInterval
        );
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await channel
                .InvokeAsync(
                    "show",
                    new Dictionary<string, object?>
                    {
                        ["initialTime"] = time.ToString("HH:mm", CultureInfo.InvariantCulture),
                        ["minuteInterval"] = value.MinuteInterval,
                        ["is24Hour"] = value.Is24Hour,
                        ["locale"] = value.Locale,
                        ["title"] = value.Title,
                        ["confirmText"] = value.ConfirmText,
                        ["cancelText"] = value.CancelText,
                        ["looping"] = value.Looping,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (result is null)
                return null;
            if (
                result is not string text
                || !TimeOnly.TryParseExact(
                    text,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var selected
                )
                || selected.Minute % value.MinuteInterval != 0
            )
                throw new InvalidDataException("The native picker returned an invalid time.");
            return selected;
        }
        finally
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    await channel
                        .InvokeAsync("cancel", cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}

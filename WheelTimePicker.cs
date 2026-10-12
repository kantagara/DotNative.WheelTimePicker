using System.Globalization;
using DotNative;
using DotNative.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNative.WheelTimePicker;

public interface IWheelTimePicker
{
    /// <summary>Returns the selected time, or null when the dialog is dismissed.</summary>
    Task<TimeOnly?> ShowAsync(
        TimePickerOptions? options = null,
        CancellationToken cancellationToken = default
    );
}

public enum TimePickerRenderer
{
    Automatic,
    SharedWheel,
}

public sealed record TimePickerOptions
{
    public TimePickerRenderer Renderer { get; init; } = TimePickerRenderer.Automatic;

    /// <summary>Null starts at the current local time, snapped down to <see cref="MinuteInterval"/>.</summary>
    public TimeOnly? InitialTime { get; init; }

    /// <summary>
    /// Minute step. Must be a divisor of 60, so the wheel can show 1, 2, 3, 4, 5, 6, 10, 12, 15, 20 or 30.
    /// </summary>
    public int MinuteInterval { get; init; } = 1;

    public bool Is24Hour { get; init; } = true;

    /// <summary>Locale tag such as sr-Latn-RS. Null uses the current culture. Title, ConfirmText and CancelText stay the strings the app already translated.</summary>
    public string? Locale { get; init; }
    public string Title { get; init; } = "Choose time";
    public string ConfirmText { get; init; } = "OK";
    public string CancelText { get; init; } = "Cancel";
    public bool Looping { get; init; } = true;
    public uint BackgroundColor { get; init; } = 0xfff6f6f6;
    public uint TextColor { get; init; } = 0xff000000;
    public string FontFamily { get; init; } = "sans-serif";
    public string TitleFontFamily { get; init; } = "sans-serif";
    public string ActionFontFamily { get; init; } = "sans-serif";
    public EdgeInsets SafeArea { get; init; }
    public float FontSize { get; init; } = 16;
    public uint ItemColor { get; init; } = 0xff000000;
    public float TitleFontSize { get; init; } = 24;
    public float ActionFontSize { get; init; } = 14;
    public byte[]? FontData { get; init; }
    public byte[]? TitleFontData { get; init; }
    public byte[]? ActionFontData { get; init; }
}

public static class TimeWheel
{
    public static void ValidateInterval(int interval)
    {
        if (interval is < 1 or > 30 || 60 % interval != 0)
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "MinuteInterval must divide 60 and be from 1 through 30."
            );
    }

    public static TimeOnly Snap(TimeOnly time, int interval)
    {
        ValidateInterval(interval);
        return new TimeOnly(time.Hour, time.Minute - time.Minute % interval);
    }

    public static CultureInfo Culture(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return CultureInfo.CurrentCulture;
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException(
                "Locale is not a known culture.",
                nameof(locale),
                exception
            );
        }

        if (!string.Equals(culture.Name, locale, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Locale is not a known culture.", nameof(locale));
        return culture;
    }

    public static string[] PeriodLabels(string? locale)
    {
        var names = Culture(locale).DateTimeFormat;
        var morning = string.IsNullOrWhiteSpace(names.AMDesignator) ? "AM" : names.AMDesignator;
        var evening = string.IsNullOrWhiteSpace(names.PMDesignator) ? "PM" : names.PMDesignator;
        return [morning, evening];
    }

    public static string[] MinuteLabels(int interval)
    {
        ValidateInterval(interval);
        return Enumerable
            .Range(0, 60 / interval)
            .Select(index => (index * interval).ToString("00", CultureInfo.InvariantCulture))
            .ToArray();
    }
}

internal sealed class WheelTimePickerService(
    IOverlayPresenter overlays,
    IUiDispatcher dispatcher,
    IPlatformChannels channels,
    PresentationTarget target,
    TimePickerOptions defaults
) : IWheelTimePicker
{
    private readonly NativeTimeRenderer native = new(channels, defaults);

    public Task<TimeOnly?> ShowAsync(
        TimePickerOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var value = options ?? defaults;
        if (!Enum.IsDefined(value.Renderer))
            throw new ArgumentOutOfRangeException(nameof(options));
        TimeWheel.ValidateInterval(value.MinuteInterval);
        _ = TimeWheel.Culture(value.Locale);
        foreach (var size in new[] { value.FontSize, value.TitleFontSize, value.ActionFontSize })
            if (!float.IsFinite(size) || size <= 0)
                throw new ArgumentException(
                    "Font sizes must be positive and finite.",
                    nameof(options)
                );
        if (
            value.Renderer == TimePickerRenderer.Automatic
            && target.Platform is NativePlatform.IOS or NativePlatform.Android
        )
            return native.ShowAsync(value, cancellationToken);
        return overlays.ShowAsync<TimeOnly?>(
            close => new WheelTimeDialog(value, close, dispatcher),
            cancellationToken
        );
    }
}

public static class WheelTimePickerServices
{
    public static IServiceCollection AddWheelTimePicker(
        this IServiceCollection services,
        TimePickerOptions? options = null
    )
    {
        services.TryAddSingleton<IWheelTimePicker>(provider => new WheelTimePickerService(
            provider.GetRequiredService<IOverlayPresenter>(),
            provider.GetRequiredService<IUiDispatcher>(),
            provider.GetRequiredService<IPlatformChannels>(),
            provider.GetRequiredService<PresentationTarget>(),
            options ?? new()
        ));
        return services;
    }
}

public static class WheelTimePickerServiceProviderExtensions
{
#if NET10_0_OR_GREATER
    extension(IServiceProvider services)
    {
        public IWheelTimePicker WheelTimePicker => services.GetRequiredService<IWheelTimePicker>();
    }
#else
    public static IWheelTimePicker WheelTimePicker(this IServiceProvider services) =>
        services.GetRequiredService<IWheelTimePicker>();
#endif
}

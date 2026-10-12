# DotNative.WheelTimePicker

Time selection with the same split as the date wheel. iOS uses `UIDatePicker` in
wheel time mode. Android uses native `NumberPicker` wheels. macOS, Windows and
Linux draw DotNative's shared `WheelPicker` through `IOverlayPresenter`.
`TimePickerRenderer.SharedWheel` forces that shared dialog on every platform.

```csharp
TimeOnly? selected = await picker.ShowAsync(
    new TimePickerOptions
    {
        MinuteInterval = 15,
        Is24Hour = true,
        Locale = "sr-Latn-RS",
        Title = chooseStartTime,
        ConfirmText = save,
        CancelText = cancel,
    },
    token
);
```

`Title`, `ConfirmText` and `CancelText` are the strings the screen already translated.
The plugin does not look up a catalog. A longer title wraps to two lines. `Locale`
selects the culture used for the AM/PM wheel and for the native picker.

`MinuteInterval` is the minute step. It must divide 60, so 1, 2, 3, 4, 5, 6,
10, 12, 15, 20 and 30 are valid. The initial time snaps down onto that step.
Dismissal returns null.

On net10.0, `services.WheelTimePicker` is the extension property. On net9.0,
call `services.WheelTimePicker()`.

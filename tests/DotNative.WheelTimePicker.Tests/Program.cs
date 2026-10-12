using System.Collections.Concurrent;
using DotNative;
using DotNative.WheelTimePicker;
using Xunit;

TimeWheel.ValidateInterval(1);
TimeWheel.ValidateInterval(15);
TimeWheel.ValidateInterval(30);
Assert.Throws<ArgumentOutOfRangeException>(() => TimeWheel.ValidateInterval(7));
Assert.Throws<ArgumentOutOfRangeException>(() => TimeWheel.ValidateInterval(60));
Assert.Equal(new TimeOnly(10, 5), TimeWheel.Snap(new TimeOnly(10, 7), 5));
Assert.Equal(["00", "15", "30", "45"], TimeWheel.MinuteLabels(15));
Assert.Equal(["AM", "PM"], TimeWheel.PeriodLabels("en-US"));
Assert.Equal("sr-Latn-RS", TimeWheel.Culture("sr-Latn-RS").Name);
Assert.Throws<ArgumentException>(() => TimeWheel.Culture("not-a-locale"));

var dispatcher = new PumpDispatcher();
using var dialog = new WheelTimeDialog(
    new TimePickerOptions { MinuteInterval = 15, Is24Hour = true, InitialTime = new(9, 7) },
    _ => { },
    dispatcher
);
dispatcher.Drain();
using var host = new RenderHost(dialog, dispatcher, new Sink());
host.SetViewport(390, 844);
host.RenderNow();
Console.WriteLine("PASS time wheel");

sealed class PumpDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> queue = new();
    public bool CheckAccess() => true;
    public void Post(Action action) => queue.Enqueue(action);
    public void Drain()
    {
        while (queue.TryDequeue(out var action))
            action();
    }
}

sealed class Sink : ICommandTransport
{
    public void Flush(ReadOnlySpan<byte> commands) { }
}

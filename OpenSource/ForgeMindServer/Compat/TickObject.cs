// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Compat;

/// <summary>
/// Standalone replacement for the SiliconLife host's TickObject.
/// The host drove ticks from its MainLoop; the MCP server has no main loop,
/// so each tick object owns a <see cref="Timer"/> that fires
/// <see cref="OnTick"/> at the configured interval.
/// </summary>
public abstract class TickObject : IDisposable
{
    private Timer? _timer;

    /// <summary>
    /// Gets the tick interval (time between ticks)
    /// </summary>
    public TimeSpan Interval { get; }

    /// <summary>
    /// Initializes a new instance of the TickObject class
    /// </summary>
    /// <param name="interval">The tick interval; zero means no periodic ticking</param>
    /// <param name="autoRegister">Whether to start the timer immediately (default: true)</param>
    protected TickObject(TimeSpan interval = default, bool autoRegister = true)
    {
        Interval = interval;

        if (autoRegister && interval > TimeSpan.Zero)
        {
            _timer = new Timer(static state => ((TickObject)state!).SafeTick(), this, interval, interval);
        }
    }

    /// <summary>
    /// Called on every interval elapse.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since the last tick</param>
    protected abstract void OnTick(TimeSpan deltaTime);

    private void SafeTick()
    {
        try
        {
            OnTick(Interval);
        }
        catch
        {
            // A failing tick must not kill the timer thread
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}

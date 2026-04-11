using Shelly.Interop;

namespace Shelly.Services;

public static class SleepPrevention
{
    private static bool _preventing;

    public static void PreventSleep()
    {
        if (_preventing) return;
        NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED);
        _preventing = true;
    }

    public static void AllowSleep()
    {
        if (!_preventing) return;
        NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        _preventing = false;
    }
}

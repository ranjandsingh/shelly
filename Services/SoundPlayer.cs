using System.Media;

namespace Shelly.Services;

public static class SoundPlayer
{
    private static DateTime _lastSoundTime = DateTime.MinValue;
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(1);

    public static void PlayWaitingForInput()
    {
        PlayThrottled(SystemSounds.Exclamation);
    }

    public static void PlayTaskCompleted()
    {
        PlayThrottled(SystemSounds.Asterisk);
    }

    private static void PlayThrottled(SystemSound sound)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSoundTime < ThrottleInterval) return;
        _lastSoundTime = now;

        try { sound.Play(); }
        catch { /* ignore audio errors */ }
    }
}

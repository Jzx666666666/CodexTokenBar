namespace CodexTokenBar.Domain;

public static class RetrySchedule
{
    private static readonly int[] DelaySeconds = [2, 5, 15, 30, 60];

    public static TimeSpan GetDelay(int zeroBasedAttempt)
    {
        if (zeroBasedAttempt < 0)
            throw new ArgumentOutOfRangeException(nameof(zeroBasedAttempt));

        var index = Math.Min(zeroBasedAttempt, DelaySeconds.Length - 1);
        return TimeSpan.FromSeconds(DelaySeconds[index]);
    }
}

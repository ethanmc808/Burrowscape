using System.Collections.Generic;

// Plain C# (not a MonoBehaviour) sliding-window rate tracker for the balance-tuning debug UI
// (ResourceBalanceDebugPanel). Deliberately a rolling window of the last N completed one-minute
// averages, NOT a cumulative all-time average — a cumulative average gets less responsive to recent
// changes the longer the game has run (a fresh tuning change gets diluted into dozens of old samples),
// which fights the entire point of a balance-tuning tool: change a number, observe, adjust. A rolling
// window never carries data older than N minutes, so it stays responsive to recent changes while still
// smoothing out one-off blips (e.g. both Coal Room workers stepping out to eat at the same moment) that
// a raw single-minute readout would show as noise.
//
// Usage: call AddSecondSample(value) once per real-world second with that second's amount (a rate for
// Power, or a this-second production/consumption bucket for Water/Carrots). Every 60 samples, the
// minute's average is pushed into a fixed-capacity queue (capacity = windowSizeMinutes); WindowAverage
// is the mean of whatever's currently in that queue. Deliberately does NOT interpolate/creep mid-minute
// — WindowAverage holds the last completed window-average until the next full minute finishes.
public class RollingMinuteAverage
{
    private readonly int windowSizeMinutes;
    private readonly Queue<float> completedMinuteAverages = new Queue<float>();

    private float currentMinuteSum;
    private int currentMinuteSampleCount;

    public float WindowAverage { get; private set; }

    public RollingMinuteAverage(int windowSizeMinutes)
    {
        this.windowSizeMinutes = windowSizeMinutes;
    }

    public void AddSecondSample(float value)
    {
        currentMinuteSum += value;
        currentMinuteSampleCount++;

        if (currentMinuteSampleCount < 60) return;

        float minuteAverage = currentMinuteSum / 60f;
        completedMinuteAverages.Enqueue(minuteAverage);
        while (completedMinuteAverages.Count > windowSizeMinutes)
            completedMinuteAverages.Dequeue();

        currentMinuteSum = 0f;
        currentMinuteSampleCount = 0;

        RecomputeWindowAverage();
    }

    private void RecomputeWindowAverage()
    {
        if (completedMinuteAverages.Count == 0)
        {
            WindowAverage = 0f;
            return;
        }

        float sum = 0f;
        foreach (float minuteAverage in completedMinuteAverages)
            sum += minuteAverage;
        WindowAverage = sum / completedMinuteAverages.Count;
    }
}

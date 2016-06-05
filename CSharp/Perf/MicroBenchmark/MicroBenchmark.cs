// This code is an aggregation of various means found across the web and is used
// to measure wall-clock time of small pieces of CLR code.
// It is mainly influenced by famous MeasureIt tool:
//     https://measureitdotnet.codeplex.com/SourceControl/latest#CodeTimers.cs
// But this version is designed to be used as a simple drop-in code,
// to be used in any codebase.
namespace MicroBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    /// <summary>
    /// Stats are computed over list of samples.
    /// Computed values include Min, Max, Median, and Stdev.
    /// </summary>
    public sealed class Stats
    {
        public double Min { get; private set; }
        public double Max { get; private set; }
        public double Median { get; private set; }
        public double Mean { get; private set; }
        public double Stdev { get; private set; }
        public int Count { get; private set; }
        public int[] Dispersion { get; private set; }

        public Stats(List<double> samples)
        {
            Count = samples.Count;
            Dispersion = Enumerable.Repeat(0, 10).ToArray();

            if (Count > 0)
            {
                samples.Sort();
                Median = Count % 2 == 0
                    ? (samples[(Count / 2) - 1] + samples[Count / 2]) / 2
                    : samples[Count / 2];

                Min = samples.Min();
                Max = samples.Max();
                Mean = samples.Sum() / Count;

                var squares = samples
                    .Select(e => e - Mean)
                    .Select(e => e * e)
                    .Sum();

                Stdev = Math.Sqrt(squares / Count);

                // Calculate samples dispersion.
                // <= -2σ <=> -1½σ <=> -σ <=> -½σ <=> Mean <=> ½σ <=> σ <=> 1½σ <=> 2σ =>
                var boundaries = Enumerable.Range(0, 9)
                    .Select(e => e * 0.5 - 2) // multipliers [-2.0 .. 0.5 .. 2.0]
                    .Select(e => Mean + Stdev * e)
                    .ToArray();

                for (int s = 0, b = 0; s < samples.Count; s++)
                {
                    if (b < boundaries.Length)
                    {
                        // Adjust bucket, since there are still more boundaries left.
                        while (b < boundaries.Length && boundaries[b] <= samples[s]) b++;
                    }

                    Dispersion[b]++;
                }
            }
            else
            {
                Min = double.MinValue;
                Max = double.MaxValue;
                Median = Mean = Stdev = 0.0;
                Count = 0;
            }
        }

        public override string ToString()
        {
            return string.Format(
                "mean={0:f3} median={1:f3} min={2:f3} max={3:f3} stdev={4:f3} count={5:f3} dispersion=[{6}]",
                Mean, Median, Min, Max, Stdev, Count, string.Join(" ", Dispersion));
        }
    };

    public delegate void SampleCallback(string name, int iterationCount, double sample);
    public delegate void MeasureCallback(string name, int iterationCount, List<double> samples);

    [Flags]
    public enum CodeTimerOptions
    {
        /// <summary>
        /// Nothing is done to stabilize potential randomizing effects.
        /// </summary>
        None = 0,

        /// <summary>
        /// If true CodeTimer will run the action once before doing a
        /// measurement run. This is useful when trying to avoid accounting for
        /// one-time overhead like JIT.
        /// Note that it could be a problem for not idempotant code under test.
        /// </summary>
        WarmUp = 1,

        /// <summary>
        /// If true CodeTimer will perform CG before running actual measurements.
        /// </summary>
        CleanGC = 2,

        /// <summary>
        /// Good default is to do both warm-up and GC.
        /// </summary>
        Default = WarmUp | CleanGC,
    }
     
    /// <summary>
    /// CodeTimer is a simple wrapper that uses System.Diagnostics.StopWatch
    /// to time some code under test to as high precision as stopwatch can
    /// provide, and trying best to eliminate (or at least stabilize) other factors,
    /// like JIT, GC, overhead of CodeTimer itself, etc.
    /// </summary>
    public sealed class CodeTimer
    {
        private CodeTimerOptions Options;

        /// <summary>
        /// The number of times the benchmark is run in a loop for a single measument.
        /// </summary>
        public int IterationCount { get; private set; }

        /// <summary>
        /// The number of measurments to make for a single benchmark. 
        /// </summary>
        public int SampleCount { get; private set; }

        /// <summary>
        /// The smallest time (in microseconds) that can be resolved by the timer.
        /// </summary>
        public static float ResolutionUsec { get { return 1000000.0F / Stopwatch.Frequency; } }

        public CodeTimer(
            int iterationCount,
            int sampleCount = 1,
            CodeTimerOptions options = CodeTimerOptions.Default)
        {
            IterationCount = iterationCount;
            Options = options;
            SampleCount = sampleCount;
        }

        /// <summary>
        /// OnMeasure is signaled every time after whole measure is complete. 
        /// </summary>
        public event MeasureCallback OnMeasure;

        /// <summary>
        /// OnSample is signaled every time after each sample is complete. 
        /// </summary>
        public event SampleCallback OnSample;

        /// <summary>
        /// Returns the number of microsecond it took to run the benchmark
        /// for `IterationCount` times.  
        /// </summary>
        public List<double> Measure(string name, Action action)
        {
            return Measure(name, 1, action);
        }
        /// <summary>
        /// Returns the number of microseconds it took to run the benchmark
        /// for `IterationCount` times divided by `scale`. 
        /// Scaling is useful if you want to normalize to a single iteration for example.  
        /// </summary>
        public List<double> Measure(string name, double scale, Action action)
        {
            var overheadUsec = GetOverheadUsec(action);
            var statsUsec = new List<double>();

            for (int i = 0; i < SampleCount; i++)
            {
                var totalMs = MeasureInternal(action);

                var sampleUsec =
                    (totalMs * 1000.0 - overheadUsec) / scale / IterationCount;

                statsUsec.Add(sampleUsec);
                if (OnSample != null)
                    OnSample(name, IterationCount, sampleUsec);
            }

            if (OnMeasure != null)
                OnMeasure(name, IterationCount, statsUsec);
            return statsUsec;
        }

        private double MeasureInternal(Action action)
        {
            var sw = Stopwatch.StartNew();

            // Run test once for warm up, so one-time stuff can be out of the way, like JIT etc. 
            if ((Options | CodeTimerOptions.WarmUp) != 0)
            {
                // Spin the CPU for a while. This should help insure that the CPU
                // gets out of any low power mode so so that we get more stable results.
                double x = 123456.789;
                while (sw.ElapsedMilliseconds < 42)
                {
                    // some math must get CPU warmed up...
                    x += Math.Sqrt(x);
                }

                action();
            }

            if ((Options | CodeTimerOptions.CleanGC) != 0)
            {
                // Make sure GC is consistent across measures.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
            }

            sw.Reset();
            sw.Start();
            for (int i = 0; i < IterationCount; i++) { action(); }

            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Prints the result of a CodeTimer to standard output and trace.
        /// This is a good default target for OnSample.  
        /// </summary>
        public static SampleCallback PrintSample = (string name, int iterationCount, double sample) =>
        {
            var message = string.Format(
                "{0}: count={1} time={2,9:f3} msec ", name, iterationCount, sample);
            Console.WriteLine(message);
            Trace.WriteLine(message);
        };

        /// <summary>
        /// Prints the mean with an error bound (2 standard deviations, which implys you have
        /// 95% confidence that sample value will be with the bounds (assuming normal distribution). 
        /// This is a good default target for OnMeasure.  
        /// </summary>
        public static MeasureCallback PrintMeasure = (string name, int iterationCount, List<double> samples) =>
        {
            var stats = new Stats(samples);
            // +- two standard deviations covers 95% of all samples in a normal distribution 
            double errorPercent = (stats.Stdev * 2 * 100) / Math.Abs(stats.Mean);
            double absError = stats.Mean * errorPercent / 100;
            string percentString = errorPercent < 400
                ? (errorPercent.ToString("f0") + "%").PadRight(5)
                : ">400%";
            var message = string.Format("{0}: {1}{2,9:f3} +- {3} or {4,7:f3} msec [{5}]",
                name,
                iterationCount == 1 ? "" : "∞ " + iterationCount + " ",
                stats.Mean,
                percentString,
                absError,
                DataToTicks(stats.Dispersion));
            Console.WriteLine(message);
            Trace.WriteLine(message);
        };


        // Converts histogram values into sparkle string.
        // This implementation secifically scales range (0..MaxValue] to the corresponding
        // ticks, special-casing 0 into space character.
        static string DataToTicks(IEnumerable<int> values)
        {
            const string ticks = "▁▂▃▄▅▆▇█";
            var factor = (double)values.Max() / (ticks.Length - 1);

            return new string(values
                .Select(e => ticks[(int)Math.Round(e / factor)])
                .ToArray());
        }


        /// <summary>
        /// Time the overhead (in msec) of the harness that does nothing so we can subtract it out.
        /// Note: We need the `action`, because calling delegates on static methods is more
        /// expensive than caling delegates on instance methods.
        /// </summary>
        double GetOverheadUsec(Action action)
        {
            // If `action` is static measure static overhead, otherwise measure instance overhead.
            if (action.Target == null)
            {
                // First warm up run...
                MeasureInternal(emptyStaticMethod);

                // and now get the min (to not get negative values) time over 10 runs.
                var staticOverheadUsec = double.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    staticOverheadUsec = Math.Min(
                        MeasureInternal(emptyStaticMethod),
                        staticOverheadUsec);
                }

                return staticOverheadUsec;
            }
            else
            {
                // First warm up run...
                MeasureInternal(this.emptyMethod);

                // and now get the min (to not get negative values) time over 10 runs.
                var instanceOverheadUsec = double.MaxValue;
                for (int i = 0; i < 10; i++)
                {
                    instanceOverheadUsec = Math.Min(
                        MeasureInternal(this.emptyMethod),
                        instanceOverheadUsec);
                }
                return instanceOverheadUsec;
            }
        }

        private static void emptyStaticMethod() { }

        private void emptyMethod() { }
    }
}

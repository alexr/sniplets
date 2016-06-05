using MicroBenchmark;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MicroBenchmarkSamples
{
    class Program
    {
        static void Main()
        {
            RegexVsStringOps();
        }

        /// <summary>
        /// Timing simple regex usage in comparison to equivalent string operations.
        /// </summary>
        static void RegexVsStringOps()
        {
            var timer = new CodeTimer(50, 50);
            timer.OnMeasure += CodeTimer.PrintMeasure;

            var regex = new Regex(@".*(foo|bar|baz).*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var r = new Random();
            var strings = Enumerable.Range(0, 10)
                .Select(e => e % 3 == 0
                    ? RandomString(r, 100, "foo", "bar", "baz")
                    : RandomString(r, 100))
                .ToArray();

            timer.Measure("Regex version", () =>
            {
                var x = 0;
                for (int i = 0; i < strings.Length; i++)
                {
                    var i1 = regex.Match(strings[i]);
                    if (i1.Success)
                        x++;
                }
            });

            timer.Measure("StrOp version", () =>
            {
                var x = 0;
                for (int i = 0; i < strings.Length; i++)
                {
                    var str = strings[i].ToLower();
                    var i1 = str.IndexOf("foo");
                    var i2 = str.IndexOf("bar");
                    var i3 = str.IndexOf("baz");
                    if (i1 > 0 || i2 > 0 || i3 > 0)
                        x++;
                }
            });

        }

        // Assumes mustHaveOneOf string length is smaller than size.
        static string RandomString(Random r, int size, params string[] mustHaveOneOf)
        {
            if (mustHaveOneOf.Length > 0)
            {
                var musthave = mustHaveOneOf[r.Next(mustHaveOneOf.Length)];
                var before = r.Next(size - musthave.Length);
                var after = size - before - musthave.Length;

                return new string(Enumerable.Repeat(0, before)
                    .Select(_ => (char)r.Next('A', 'Z'))
                    .Concat(musthave)
                    .Concat(Enumerable.Repeat(0, after)
                        .Select(_ => (char)r.Next('A', 'Z')))
                    .ToArray());
            }
            else
            {
                return new string(Enumerable.Repeat(0, size)
                    .Select(_ => (char)r.Next('A', 'Z'))
                    .ToArray());
            }
        }
    }
}

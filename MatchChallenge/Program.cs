using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Buffers;

namespace MatchChallenge
{
    public interface IMatcher
    {
        string MatchChallenge(string input);
    }

    public abstract class PartsMatcher : IMatcher
    {
        public string MatchChallenge(string input)
        {
            for (int i = 2; i <= input.Length / 2; i++)
            {
                if (input.Length % i == 0)
                {
                    var partLength = input.Length / i;
                    var data = new PartData(input, partLength, i);
                    if (AllPartsEqual(data))
                        return data.Part;
                }
            }
            return "-1";
        }

        protected virtual bool PreCheck(in PartData data)
        {
            var span = data.Input.AsSpan();
            return span[0] == span[data.PartLength];
        }

        protected abstract bool AllPartsEqual(in PartData data);

        protected record PartData(string Input, int PartLength, int PartCount)
        {
            private readonly string _part = Input[..PartLength];
            public string Part => _part;
        }
    }

    public class SubstringMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
        {
            for (int i = 1; i < data.PartCount; i++)
            {
                if (data.Input.Substring(i * data.PartLength, data.PartLength) != data.Part)
                    return false;
            }
            return true;
        }        
    }

    public class SpanSliceMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
        {            
            for (int i = 1; i < data.PartCount; i++)
            {
                if (!data.Input.AsSpan().Slice(i * data.PartLength, data.PartLength).SequenceEqual(data.Part.AsSpan()))
                    return false;
            }
            return true;
        }
    }

    public class SplitMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
            => PreCheck(data) && data.Input.Split(new string[] { data.Part }, StringSplitOptions.RemoveEmptyEntries).Length == 0;
    }

    public class ReplaceMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
            => PreCheck(data) && data.Input.Replace(data.Part, "").Length == 0;
    }

    public class RegexMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
            => Regex.IsMatch(data.Input, $"({data.Part}){{{data.PartCount}}}");
    }

    public class LinqMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
            => string.Concat(Enumerable.Repeat(data.Part, data.PartCount)) == data.Input;
    }

    public class PureRegexMatcher : IMatcher
    {
        private readonly Regex _regex = new("^(.+)\\1+", RegexOptions.Compiled);
        // also matching end $ is much slower

        public string MatchChallenge(string input)
        {        
            var m = _regex.Match(input);
            if (m.Success && m.Length == input.Length)
                return m.Groups[1].Value;
            return "-1";
        }
    }

    public class OffsetMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
        {
            for (int i = 0; i < data.Input.Length - data.PartLength; i++)
            {
                if (data.Input[i] != data.Input[i + data.PartLength])
                    return false;
            }
            return true;
        }
    }

    public class ModuloMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
        {
            for (int i = data.PartLength; i < data.Input.Length; i++)
            {
                if (data.Input[i] != data.Input[i % data.PartLength])
                    return false;
            }
            return true;
        }
    }

    public abstract class KmpMatcher : IMatcher
    {
        public string MatchChallenge(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "-1";

            int suffixLength = ComputeSuffixLength(input);
            if (suffixLength == 0)
                return "-1";

            int partLength = input.Length - suffixLength;
            if (input.Length % partLength != 0)
                return "-1";

            if (partLength % 2 == 0)
                partLength = input.Length / 2;
            return input.Substring(0, partLength);
        }

        protected abstract int ComputeSuffixLength(ReadOnlySpan<char> input);

        protected int ComputeSuffixLength(ReadOnlySpan<char> input, Span<int> lps)
        {
            int len = 0;
            int i = 1;

            lps[0] = 0;
            while (i < input.Length)
            {
                if (input[i] == input[len])
                {
                    lps[i++] = ++len;
                }
                else
                {
                    if (len != 0)
                        len = lps[len - 1];
                    else
                        lps[i++] = 0;
                }
            }
            return lps[input.Length - 1];
        }
    }

    public class KmpHeapMatcher : KmpMatcher
    {
        protected override int ComputeSuffixLength(ReadOnlySpan<char> input)
        {
            var lps = new int[input.Length];
            return ComputeSuffixLength(input, lps);
        }
    }

    public class KmpArrayPoolMatcher : KmpMatcher
    {
        protected override int ComputeSuffixLength(ReadOnlySpan<char> input)
        {
            var lps = ArrayPool<int>.Shared.Rent(input.Length);
            var suffixLength = ComputeSuffixLength(input, lps);
            ArrayPool<int>.Shared.Return(lps);
            return suffixLength;
        }
    }

    public class KmpStackAllocMatcher : KmpMatcher
    {
        protected override int ComputeSuffixLength(ReadOnlySpan<char> input)
        {
            Span<int> lps = stackalloc int[input.Length];
            return ComputeSuffixLength(input, lps);
        }
    }

    [MemoryDiagnoser, RankColumn, ShortRunJob]
    //[AnyCategoriesFilter("A", "B", "E")]
    //[AllCategoriesFilter("1")]
    public class MatcherBenchmarks
    {
        public enum InType
        {
            Good,
            Bad,
            Mixed
        }

        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        static Random _rnd = new Random();

        string _good = String.Empty;
        string _bad = String.Empty;

        IMatcher _substringMatcher = new SubstringMatcher();
        IMatcher _spanSliceMatcher = new SpanSliceMatcher();
        IMatcher _splitMatcher = new SplitMatcher();
        IMatcher _replaceMatcher = new ReplaceMatcher();
        IMatcher _regexMatcher = new RegexMatcher();
        IMatcher _linqMatcher = new LinqMatcher();
        IMatcher _pureRegexMatcher = new PureRegexMatcher();
        IMatcher _offsetMatcher = new OffsetMatcher();
        IMatcher _moduloMatcher = new ModuloMatcher();
        IMatcher _kmpHeapMatcher = new KmpHeapMatcher();
        IMatcher _kmpArrayPoolMatcher = new KmpArrayPoolMatcher();
        IMatcher _kmpStackAllocMatcher = new KmpStackAllocMatcher();

        [Params(InType.Mixed)]
        //[ParamsAllValues]
        public InType InputType;

        [Params(257)]
        public int BaseSize;

        [Params(257)]
        public int Repetition;

        [GlobalSetup]
        public void Setup()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < BaseSize; i++)
                sb.Append(alphabet[_rnd.Next(alphabet.Length)]);
            var value = sb.ToString();

            if (InputType == InType.Mixed || InputType == InType.Good)
            {
                sb.Clear();
                for (int i = 0; i < Repetition; i++)
                    sb.Append(value);

                _good = sb.ToString();
            }

            if (InputType == InType.Mixed || InputType == InType.Bad)
            {
                sb.Clear();
                for (int i = 0; i < BaseSize * Repetition; i++)
                    sb.Append(alphabet[_rnd.Next(alphabet.Length)]);
                _bad = sb.ToString();
            }                
        }

        string Test(IMatcher matcher)
        {
            switch (InputType)
            {
                case InType.Good:
                    return matcher.MatchChallenge(_good);
                case InType.Bad:
                    return matcher.MatchChallenge(_bad);
                case InType.Mixed:
                default:
                    matcher.MatchChallenge(_bad);
                    return matcher.MatchChallenge(_good);
            }
        }

        [Benchmark, BenchmarkCategory("A", "2")]
        public string Substring() => Test(_substringMatcher);

        [Benchmark, BenchmarkCategory("A", "1")]
        public string SpanSlice() => Test(_spanSliceMatcher);

        [Benchmark, BenchmarkCategory("B", "2")]
        public string Split() => Test(_splitMatcher);

        [Benchmark, BenchmarkCategory("B", "1")]
        public string Replace() => Test(_replaceMatcher);

        [Benchmark, BenchmarkCategory("C", "1")]
        public string Regex() => Test(_regexMatcher);

        [Benchmark, BenchmarkCategory("C", "2")]
        public string Linq() => Test(_linqMatcher);

        [Benchmark, BenchmarkCategory("C", "2")]
        public string PureRegex() => Test(_pureRegexMatcher);

        [Benchmark, BenchmarkCategory("D", "1")]
        public string Offset() => Test(_offsetMatcher);

        [Benchmark, BenchmarkCategory("D", "2")]
        public string Modulo() => Test(_moduloMatcher);

        [Benchmark, BenchmarkCategory("E", "2")]
        public string KmpHeap() => Test(_kmpHeapMatcher);

        [Benchmark, BenchmarkCategory("E", "1")]
        public string KmpArrayPool() => Test(_kmpArrayPoolMatcher);

        [Benchmark, BenchmarkCategory("E", "2")]
        public string KmpStackAlloc() => Test(_kmpStackAllocMatcher);
    }

    public class Program
    {
        public static void Main()
        {
#if DEBUG
            //var args = new string[] { "--list", "Flat" };
            //var args = new string[] { "-f", "*KmpStackAlloc" }; //must match filtered benchmarks
            var args = new string[] { "-f", "*MatcherBenchmarks*" };
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new BenchmarkDotNet.Configs.DebugInProcessConfig());
#else
            BenchmarkRunner.Run<MatcherBenchmarks>();
            //Console.WriteLine(new MatcherBenchmarks().KmpStackAlloc());
#endif
        }
    }
}

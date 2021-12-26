﻿using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            => data.Input.Split(new string[] { data.Part }, StringSplitOptions.RemoveEmptyEntries).Length == 0;
    }

    public class ReplaceMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
            => data.Input.Replace(data.Part, "").Length == 0;
    }

    public class RegexMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
        {
            return Regex.IsMatch(data.Input, $"({data.Part}){{{data.PartCount}}}");
        }
    }

    public class LinqMatcher : PartsMatcher
    {
        protected override bool AllPartsEqual(in PartData data)
        {
            return string.Concat(Enumerable.Repeat(data.Part, data.PartCount)) == data.Input;
        }
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

    public class KmpMatcher : IMatcher
    {
        public string MatchChallenge(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "-1";

            var lps = ComputeLpsArray(input);
            int suffixLength = lps[input.Length - 1];
            if (suffixLength == 0)
                return "-1";

            int partLength = input.Length - suffixLength;
            if (input.Length % partLength != 0)
                return "-1";

            if (partLength % 2 == 0)
                partLength = input.Length / 2;
            return input.Substring(0, partLength);
        }

        int[] ComputeLpsArray(ReadOnlySpan<char> input)
        {
            var lps = new int[input.Length];
            int len = 0;
            int i = 1;

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

            return lps;
        }
    }

    public class KmpStackAllocMatcher : IMatcher
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

        int ComputeSuffixLength(ReadOnlySpan<char> input)
        {
            Span<int> lps = stackalloc int[input.Length];
            int len = 0;
            int i = 1;

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

    [MemoryDiagnoser]
    [RankColumn]
    public class MatcherBenchmarks
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        static Random _rnd = new Random();

        string _good;
        string _bad;
        IMatcher _substringMatcher = new SubstringMatcher();
        IMatcher _spanSliceMatcher = new SpanSliceMatcher();
        IMatcher _splitMatcher = new SplitMatcher();
        IMatcher _replaceMatcher = new ReplaceMatcher();
        IMatcher _regexMatcher = new RegexMatcher();
        IMatcher _linqMatcher = new LinqMatcher();
        IMatcher _pureRegexMatcher = new PureRegexMatcher();
        IMatcher _offsetMatcher = new OffsetMatcher();
        IMatcher _moduloMatcher = new ModuloMatcher();
        IMatcher _kmpMatcher = new KmpMatcher();
        IMatcher _kmpStackAllocMatcher = new KmpStackAllocMatcher();

        public MatcherBenchmarks()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 257; i++)
                sb.Append(alphabet[_rnd.Next(alphabet.Length)]);
            var value = sb.ToString();

            sb.Clear();
            for (int i = 0; i < 257; i++)
                sb.Append(value);

            _good = sb.ToString();

            sb.Clear();
            for (int i = 0; i < 257 * 257; i++)
                sb.Append(alphabet[_rnd.Next(alphabet.Length)]);
            _bad = sb.ToString();
        }

        string Test(IMatcher matcher)
        {
            matcher.MatchChallenge(_bad);
            return matcher.MatchChallenge(_good);
        }

        [Benchmark]
        public string Substring() => Test(_substringMatcher);

        [Benchmark]
        public string SpanSlice() => Test(_spanSliceMatcher);

        [Benchmark]
        public string Split() => Test(_splitMatcher);

        [Benchmark]
        public string Replace() => Test(_replaceMatcher);

        [Benchmark]
        public string Regex() => Test(_regexMatcher);

        [Benchmark]
        public string Linq() => Test(_linqMatcher);

        [Benchmark]
        public string PureRegex() => Test(_pureRegexMatcher);

        [Benchmark]
        public string Offset() => Test(_offsetMatcher);

        [Benchmark]
        public string Modulo() => Test(_moduloMatcher);

        [Benchmark]
        public string Kmp() => Test(_kmpMatcher);

        [Benchmark]
        public string KmpStackAlloc() => Test(_kmpStackAllocMatcher);
    }

    public class Program
    {
        public static void Main()
        {
            BenchmarkRunner.Run<MatcherBenchmarks>();
            // Console.WriteLine(new MatcherBenchmarks().KmpMatcher());            
        }
    }
}

using BenchmarkDotNet.Attributes;
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

    public abstract class SubstringPartsMatcher : IMatcher
    {
        public string MatchChallenge(string input)
        {
            for (int i = 2; i <= input.Length / 2; i++)
            {
                if (input.Length % i == 0)
                {
                    var partLength = input.Length / i;
                    var part = input.Substring(0, partLength);
                    if (AllPartsEqual(input, part))
                        return part;
                }
            }
            return "-1";
        }

        protected abstract bool AllPartsEqual(string input, string part);
    }

    public class SubstringMatcher : SubstringPartsMatcher
    {
        protected override bool AllPartsEqual(string input, string part)
        {
            for (int i = 2; i < input.Length / part.Length; i++)
            {
                if (input.Substring(i * part.Length, part.Length) != part)
                    return false;
            }
            return true;
        }
    }

    public class SplitMatcher : SubstringPartsMatcher
    {
        protected override bool AllPartsEqual(string input, string part)
            => input.Split(new string[] { part }, StringSplitOptions.RemoveEmptyEntries).Length == 0;
    }

    public class RegexMatcher : SubstringPartsMatcher
    {
        protected override bool AllPartsEqual(string input, string part)
        {
            for (int i = 2; i < input.Length / part.Length; i++)
            {
                if (Regex.IsMatch(input, $"({part}){{{i}}}"))
                    return false;
            }
            return true;
        }
    }

    public class LinqMatcher : SubstringPartsMatcher
    {
        protected override bool AllPartsEqual(string input, string part)
        {
            for (int i = 2; i < input.Length / part.Length; i++)
            {
                if (string.Concat(Enumerable.Repeat(part, i)) == input)
                    return false;
            }
            return true;
        }
    }

    public class PureRegexMatcher : IMatcher
    {
        public string MatchChallenge(string input)
        {
            var m = Regex.Match(input, "(.+)\\1+");
            if (m.Success && m.Length == input.Length)
                return m.Groups[1].Value;
            return "-1";
        }
    }

    public abstract class StringIndexMatcher : IMatcher
    {
        public string MatchChallenge(string input)
        {
            for (int i = 2; i <= input.Length / 2; i++)
            {
                if (input.Length % i == 0)
                {
                    var partLength = input.Length / i;
                    if (AllPartsEqual(input, partLength))
                        return input.Substring(0, partLength);
                }
            }
            return "-1";
        }

        protected abstract bool AllPartsEqual(string input, int partLength);
    }

    public class OffsetMatcher : StringIndexMatcher
    {
        protected override bool AllPartsEqual(string input, int partLength)
        {
            for (int i = 0; i < input.Length / partLength - 1; i++)
            {
                for (int j = 0; j < partLength; j++)
                {
                    int index = i * partLength + j;
                    if (input[index] != input[index + partLength])
                        return false;
                }
            }
            return true;
        }
    }

    public class ModuloMatcher : StringIndexMatcher
    {
        protected override bool AllPartsEqual(string input, int partLength)
        {
            for (int i = partLength; i < input.Length; i++)
            {
                if (input[i] != input[i % partLength])
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

        int [] ComputeLpsArray(ReadOnlySpan<char> input)
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
                        i++;
                }
            }

            return lps;
        }
    }

    public class KmpMatcher2 : IMatcher
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
                        i++;
                }
            }

            return lps[input.Length - 1];
        }
    }

    [MemoryDiagnoser]
    public class MatcherBenchmarks
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        static Random _rnd = new Random();

        string _good;
        string _bad;
        IMatcher _substringMatcher = new SubstringMatcher();
        IMatcher _splitMatcher = new SplitMatcher();
        IMatcher _regexMatcher = new RegexMatcher();
        IMatcher _linqMatcher = new LinqMatcher();
        IMatcher _pureRegexMatcher = new PureRegexMatcher();
        IMatcher _offsetMatcher = new OffsetMatcher();
        IMatcher _moduloMatcher = new ModuloMatcher();
        IMatcher _kmpMatcher = new KmpMatcher();
        IMatcher _kmpMatcher2 = new KmpMatcher2();

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
        public string SubstringMatcher() => Test(_substringMatcher);

        [Benchmark]
        public string SplitMatcher() => Test(_splitMatcher);

        [Benchmark]
        public string RegexMatcher() => Test(_regexMatcher);

        [Benchmark]
        public string LinqMatcher() => Test(_linqMatcher);

        [Benchmark]
        public string PureRegexMatcher() => Test(_pureRegexMatcher);

        [Benchmark]
        public string OffsetMatcher() => Test(_offsetMatcher);

        [Benchmark]
        public string ModuloMatcher() => Test(_moduloMatcher);

        [Benchmark]
        public string KmpMatcher() => Test(_kmpMatcher);

        [Benchmark]
        public string KmpMatcher2() => Test(_kmpMatcher2);
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

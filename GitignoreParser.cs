﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using static GitignoreParserNet.RegexPatterns;

namespace GitignoreParserNet
{
    public sealed class GitignoreParser
    {
        private readonly (Regex Merged, Regex[] Individual) Positives;
        private readonly (Regex Merged, Regex[] Individual) Negatives;

        public GitignoreParser(string content, bool compileRegex = false)
        {
            (Positives, Negatives) = Parse(content, compileRegex);
        }

        public GitignoreParser(string gitignorePath, Encoding fileEncoding, bool compileRegex = false)
        {
            (Positives, Negatives) = Parse(File.ReadAllText(gitignorePath, fileEncoding), compileRegex);
        }

        public static ((Regex Merged, Regex[] Individual) positives, (Regex Merged, Regex[] Individual) negatives) Parse(string content, bool compileRegex = false)
        {
            var regexOptions = compileRegex ? RegexOptions.Compiled : RegexOptions.None;

            (List<string> positive, List<string> negative) = content
                .Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                .Aggregate<string, (List<string>, List<string>), (List<string>, List<string>)>(
                    (new List<string>(), new List<string>()),
                    ((List<string> positive, List<string> negative) lists, string line) =>
                    {
                        if (line.StartsWith('!'))
                            lists.negative.Add(line.Substring(1));
                        else
                            lists.positive.Add(line);
                        return (lists.positive, lists.negative);
                    },
                    ((List<string> positive, List<string> negative) lists) => lists
                );

            static (Regex Merged, Regex[] Individual) Submatch(List<string> list, RegexOptions regexOptions)
            {
                if (list.Count == 0)
                {
                    return (MatchEmptyRegex, Array.Empty<Regex>());
                }
                else
                {
                    var reList = list.OrderBy(str => str).Select(PrepareRegexPattern).ToList();
                    return (
                        new Regex($"(?:{string.Join(")|(?:", reList)})", regexOptions),
                        reList.Select(s => new Regex(s, regexOptions)).ToArray()
                    );
                }
            }

            return (Submatch(positive, regexOptions), Submatch(negative, regexOptions));
        }

        public static (IEnumerable<string> Accepted, IEnumerable<string> Denied) Parse(string content, string directoryPath)
        {
            GitignoreParser parser = new(content, false);
            DirectoryInfo directory = new(directoryPath);
            return (parser.Accepted(directory), parser.Denied(directory));
        }

        public static (IEnumerable<string> Accepted, IEnumerable<string> Denied) Parse(string gitignorePath, Encoding fileEncoding, string? directoryPath = null)
        {
            GitignoreParser parser = new(gitignorePath, fileEncoding, false);

            DirectoryInfo directory = directoryPath != null
                ? new(directoryPath)
                : (new FileInfo(gitignorePath).Directory ?? throw new DirectoryNotFoundException($"Couldn't find the parent dirrectory for \"{gitignorePath}\""));

            return (parser.Accepted(directory), parser.Denied(directory));
        }

        static List<string> ListFiles(DirectoryInfo directory, string rootPath = "")
        {
            if (rootPath.Length == 0)
                rootPath = directory.FullName;

            List<string> files = [
                directory.FullName.Substring(rootPath.Length) + '/'
            ];
            foreach (FileInfo file in directory.GetFiles())
                files.Add(file.FullName.Substring(rootPath.Length + 1));

            foreach (DirectoryInfo subDir in directory.GetDirectories())
                files.AddRange(ListFiles(subDir, rootPath));

            return files;
        }

        /// <summary>
        /// Notes:
        /// - you MUST postfix a input directory with '/' to ensure the gitignore
        ///   rules can be applied conform spec.
        /// - you MAY prefix a input directory with '/' when that directory is
        ///   'rooted' in the same directory as the compiled .gitignore spec file.
        /// </summary>
        /// <returns>
        /// TRUE when the given `input` path PASSES the gitignore filters,
        /// i.e. when the given input path is DENIED.
        /// </returns>
#if DEBUG
        public bool Accepts(string input, bool? expected = null)
#else
        public bool Accepts(string input)
#endif
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                input = input.Replace('\\', '/');

            if (!input.StartsWith('/'))
                input = "/" + input;

            var acceptTest = Negatives.Merged.IsMatch(input);
            var denyTest = Positives.Merged.IsMatch(input);
            var returnVal = acceptTest || !denyTest;

            // See the test/fixtures/gitignore.manpage.txt near line 680 (grep for "uber-nasty"):
            // to resolve chained rules which reject, then accept, we need to establish
            // the precedence of both accept and reject parts of the compiled gitignore by
            // comparing match lengths.
            // Since the generated consolidated regexes are lazy, we must loop through all lines' regexes instead:
#if DEBUG
            Match? acceptMatch = null, denyMatch = null;
#endif
            if (acceptTest && denyTest)
            {
                int acceptLength = 0, denyLength = 0;
                foreach (var re in Negatives.Individual)
                {
                    var m = re.Match(input);
                    if (m.Success && acceptLength < m.Value.Length)
                    {
#if DEBUG
                        acceptMatch = m;
#endif
                        acceptLength = m.Value.Length;
                    }
                }
                foreach (var re in Positives.Individual)
                {
                    var m = re.Match(input);
                    if (m.Success && denyLength < m.Value.Length)
                    {
#if DEBUG
                        denyMatch = m;
#endif
                        denyLength = m.Value.Length;
                    }
                }
                returnVal = acceptLength >= denyLength;
            }
#if DEBUG
            if (expected != null && expected != returnVal)
            {
                Diagnose(
                    "accepts",
                    input,
                    (bool)expected,
                    Negatives.Merged,
                    acceptTest,
                    acceptMatch,
                    Positives.Merged,
                    denyTest,
                    denyMatch,
                    "(Accept || !Deny)",
                    returnVal
                );
            }
#endif
            return returnVal;
        }

        public IEnumerable<string> Accepted(IEnumerable<string> input)
        {
            return input.Where(f => Accepts(f));
        }

        public IEnumerable<string> Accepted(DirectoryInfo directory)
        {
            var files = ListFiles(directory);
            return files.Where(f => Accepts(f));
        }

        /// <summary>
        /// Notes:
        /// - you MUST postfix a input directory with '/' to ensure the gitignore
        ///   rules can be applied conform spec.
        /// - you MAY prefix a input directory with '/' when that directory is
        ///   'rooted' in the same directory as the compiled .gitignore spec file.
        /// </summary>
        /// <returns>
        /// TRUE when the given `input` path FAILS the gitignore filters,
        /// i.e. when the given input path is ACCEPTED.
        /// </returns>
#if DEBUG
        public bool Denies(string input, bool? expected = null)
#else
        public bool Denies(string input)
#endif
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                input = input.Replace('\\', '/');

            if (!input.StartsWith('/'))
                input = "/" + input;

            var acceptTest = Negatives.Merged.IsMatch(input);
            var denyTest = Positives.Merged.IsMatch(input);
            // boolean logic:
            //
            // Denies = !Accepts =>
            // Denies = !(Accept || !Deny) =>
            // Denies = (!Accept && !!Deny) =>
            // Denies = (!Accept && Deny)
            var returnVal = !acceptTest && denyTest;

            // See the test/fixtures/gitignore.manpage.txt near line 680 (grep for "uber-nasty"):
            // to resolve chained rules which reject, then accept, we need to establish
            // the precedence of both accept and reject parts of the compiled gitignore by
            // comparing match lengths.
            // Since the generated regexes are all set up to be GREEDY, we can use the
            // consolidated regex for this, instead of having to loop through all lines' regexes:
#if DEBUG
            Match? acceptMatch = null, denyMatch = null;
#endif
            if (acceptTest && denyTest)
            {
                int acceptLength = 0, denyLength = 0;
                foreach (var re in Negatives.Individual)
                {
                    var m = re.Match(input);
                    if (m.Success && acceptLength < m.Value.Length)
                    {
#if DEBUG
                        acceptMatch = m;
#endif
                        acceptLength = m.Value.Length;
                    }
                }
                foreach (var re in Positives.Individual)
                {
                    var m = re.Match(input);
                    if (m.Success && denyLength < m.Value.Length)
                    {
#if DEBUG
                        denyMatch = m;
#endif
                        denyLength = m.Value.Length;
                    }
                }
                returnVal = acceptLength < denyLength;
            }
#if DEBUG
            if (expected != null && expected != returnVal)
            {
                Diagnose(
                    "denies",
                    input,
                    (bool)expected,
                    Negatives.Merged,
                    acceptTest,
                    acceptMatch,
                    Positives.Merged,
                    denyTest,
                    denyMatch,
                    "(!Accept && Deny)",
                    returnVal
                );
            }
#endif
            return returnVal;
        }

        public IEnumerable<string> Denied(IEnumerable<string> input)
        {
            return input.Where(f => Denies(f));
        }

        public IEnumerable<string> Denied(DirectoryInfo directory)
        {
            var files = ListFiles(directory);
            return files.Where(f => Denies(f));
        }

        /// <summary>
        /// <para>
        /// You can use this method to help construct the decision path when you
        /// process nested .gitignore files: .gitignore filters in subdirectories
        /// MAY override parent .gitignore filters only when there's actually ANY
        /// filter in the child .gitignore after all.
        /// </para>
        /// <para>
        /// Notes:
        /// - you MUST postfix a input directory with '/' to ensure the gitignore
        ///   rules can be applied conform spec.
        /// - you MAY prefix a input directory with '/' when that directory is
        ///   'rooted' in the same directory as the compiled .gitignore spec file.
        /// </para>
        /// </summary>
        /// <returns>
        /// TRUE when the given `input` path is inspected by any .gitignore
        /// filter line.
        /// </returns>
#if DEBUG
        public bool Inspects(string input, bool? expected = null)
#else
        public bool Inspects(string input)
#endif
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                input = input.Replace('\\', '/');

            if (!input.StartsWith('/'))
                input = "/" + input;

            var acceptTest = Negatives.Merged.IsMatch(input);
            var denyTest = Positives.Merged.IsMatch(input);
            // when any filter 'touches' the input path, it must match,
            // no matter whether it's a deny or accept filter line:
            var returnVal = acceptTest || denyTest;
#if DEBUG
            if (expected != null && expected != returnVal)
            {
                Diagnose(
                    "inspects",
                    input,
                    (bool)expected,
                    Negatives.Merged,
                    acceptTest,
                    null,
                    Positives.Merged,
                    denyTest,
                    null,
                    "(Accept || Deny)",
                    returnVal
                );
            }
#endif
            return returnVal;
        }

        [SuppressMessage("Major Code Smell", "S1121:Assignments should not be made from within sub-expressions")]
        private static string PrepareRegexPattern(string pattern)
        {
            // https://git-scm.com/docs/gitignore#_pattern_format
            //
            // * ...
            //
            // * If there is a separator at the beginning or middle (or both) of the pattern,
            //   then the pattern is relative to the directory level of the particular
            //   .gitignore file itself.
            //   Otherwise the pattern may also match at any level below the .gitignore level.
            //
            // * ...
            //
            // * For example, a pattern `doc/frotz/` matches `doc/frotz` directory, but
            //   not `a/doc/frotz` directory; however `frotz/` matches `frotz` and `a/frotz`
            //   that is a directory (all paths are relative from the .gitignore file).
            //
#if DEBUG
            string input = pattern;
#endif
            var reBuilder = new StringBuilder();
            bool rooted = false, directory = false;
            if (pattern.StartsWith('/'))
            {
                rooted = true;
                pattern = pattern.Substring(1);
            }
            if (pattern.EndsWith('/'))
            {
                directory = true;
                pattern = pattern.Substring(0, pattern.Length - 1);
            }

            string transpileRegexPart(string _re)
            {
                if (_re.Length == 0) return _re;
                // unescape for these will be escaped again in the subsequent `.Replace(...)`,
                // whether they were escaped before or not:
                _re = BackslashRegex.Replace(_re, "$1");
                // escape special regex characters:
                _re = SpecialCharactersRegex.Replace(_re, @"\$&");
                _re = QuestionMarkRegex.Replace(_re, "[^/]");
                _re = SlashDoubleAsteriksSlashRegex.Replace(_re, "(?:/|(?:/.+/))");
                _re = DoubleAsteriksSlashRegex.Replace(_re, "(?:|(?:.+/))");
                _re = SlashDoubleAsteriksRegex.Replace(_re, _ =>
                {
                    directory = true;       // `a/**` should match `a/`, `a/b/` and `a/b`, the latter by implication of matching directory `a/`
                    return "(?:|(?:/.+))";  // `a/**` also accepts `a/` itself
                });
                _re = DoubleAsteriksRegex.Replace(_re, ".*");
                // `a/*` should match `a/b` and `a/b/` but NOT `a` or `a/`
                // meanwhile, `a/*/` should match `a/b/` and `a/b/c` but NOT `a` or `a/` or `a/b`
                _re = SlashAsteriksEndOrSlashRegex.Replace(_re, "/[^/]+$1");
                _re = AsteriksRegex.Replace(_re, "[^/]*");
                _re = SlashRegex.Replace(_re, @"\/");
                return _re;
            }

            // keep character ranges intact:
            Regex rangeRe = RangeRegex;
            // ^ could have used the 'y' sticky flag, but there's some trouble with infinite loops inside
            //   the matcher below then...
            for (Match match; (match = rangeRe.Match(pattern)).Success;)
            {
                if (match.Groups[1].Value.Contains('/'))
                {
                    rooted = true;
                    // ^ cf. man page:
                    //
                    //   If there is a separator at the beginning or middle (or both)
                    //   of the pattern, then the pattern is relative to the directory
                    //   level of the particular .gitignore file itself. Otherwise
                    //   the pattern may also match at any level below the .gitignore level.
                }
                reBuilder.Append(transpileRegexPart(match.Groups[1].Value));
                reBuilder.Append('[').Append(match.Groups[2].Value).Append(']');

                pattern = pattern.Substring(match.Length);
            }
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                if (pattern.Contains('/'))
                {
                    rooted = true;
                    // ^ cf. man page:
                    //
                    //   If there is a separator at the beginning or middle (or both)
                    //   of the pattern, then the pattern is relative to the directory
                    //   level of the particular .gitignore file itself. Otherwise
                    //   the pattern may also match at any level below the .gitignore level.
                }
                reBuilder.Append(transpileRegexPart(pattern));
            }

            // prep regexes assuming we'll always prefix the check string with a '/':
            reBuilder.Preappend(rooted ? @"^\/" : @"\/");
            // cf spec:
            //
            //   If there is a separator at the end of the pattern then the pattern
            //   will only match directories, otherwise the pattern can match
            //   **both files and directories**.                   (emphasis mine)
            // if `directory`: match the directory itself and anything within
            // otherwise: match the file itself, or, when it is a directory, match the directory and anything within
            reBuilder.Append(directory ? @"\/" : @"(?:$|\/)");

            // regex validation diagnostics: better to check if the part is valid
            // then to discover it's gone haywire in the big conglomerate at the end.

            string re = reBuilder.ToString();

#if DEBUG
            try
            {
#pragma warning disable S1481 // Unused local variables should be removed
                Regex regex = new($"(?:{re})"); // throws ArgumentException if a regular expression parsing error occurred.
#pragma warning restore S1481 // Unused local variables should be removed
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("Failed regex: \n\tinput: {0}\n\tregex: {1}\n\texception: {2}", input, re, ex);
            }
#endif

            return re;
        }

#if DEBUG
        public sealed class OnExpectedMatchFailEventArgs(
            string query,
            string input,
            bool expected,
            Regex acceptRe,
            bool acceptTest,
            Match? acceptMatch,
            Regex denyRe,
            bool denyTest,
            Match? denyMatch,
            string combine,
            bool returnVal) : EventArgs
        {
            public string Query { get; set; } = query;
            public string Input { get; set; } = input;
            public bool Expected { get; set; } = expected;
            public Regex AcceptRe { get; set; } = acceptRe;
            public bool AcceptTest { get; set; } = acceptTest;
            public Match? AcceptMatch { get; set; } = acceptMatch;
            public Regex DenyRe { get; set; } = denyRe;
            public bool DenyTest { get; set; } = denyTest;
            public Match? DenyMatch { get; set; } = denyMatch;
            public string Combine { get; set; } = combine;
            public bool ReturnVal { get; set; } = returnVal;
        }
        public event EventHandler<OnExpectedMatchFailEventArgs>? OnExpectedMatchFail;

        /// <summary>
        /// Helper invoked when any `Accepts()`, `Denies()` or `Inspects()`
        /// fail to help the developer analyze what is going on inside:
        /// some gitignore spec bits are non-intuitive / non-trivial, after all.
        /// </summary>
        private void Diagnose(
            string query,
            string input,
            bool expected,
            Regex acceptRe,
            bool acceptTest,
            Match? acceptMatch,
            Regex denyRe,
            bool denyTest,
            Match? denyMatch,
            string combine,
            bool returnVal
        )
        {
            if (OnExpectedMatchFail != null)
            {
                OnExpectedMatchFail(this,
                    new OnExpectedMatchFailEventArgs(
                        query,
                        input,
                        expected,
                        acceptRe,
                        acceptTest,
                        acceptMatch,
                        denyRe,
                        denyTest,
                        denyMatch,
                        combine,
                        returnVal
                    )
                );
                return;
            }

            var log = new StringBuilder()
                .Append('\'').Append(query).AppendLine("': {")
                .Append("\tquery: '").Append(query).AppendLine("',")
                .Append("\tinput: '").Append(input).AppendLine("',")
                .Append("\texpected: '").Append(expected).AppendLine("',")
                .Append("\tacceptRe: '").Append(acceptRe).AppendLine("',")
                .Append("\tacceptTest: '").Append(acceptTest).AppendLine("',")
                .Append("\tacceptMatch: '").Append(acceptMatch).AppendLine("',")
                .Append("\tdenyRe: '").Append(denyRe).AppendLine("',")
                .Append("\tdenyTest: '").Append(denyTest).AppendLine("',")
                .Append("\tdenyMatch: '").Append(denyMatch).AppendLine("',")
                .Append("\tcombine: '").Append(combine).AppendLine("',")
                .Append("\treturnVal: '").Append(returnVal).AppendLine("'")
                .AppendLine("}")
                .ToString();
            Console.WriteLine(log);
        }
#endif
    }
}

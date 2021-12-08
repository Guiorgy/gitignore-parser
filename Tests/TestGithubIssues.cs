﻿using GitignoreParserNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace Tests
{
    [TestClass]
    public class TestGithubIssues
    {
        private static void gitignoreAccepts(GitignoreParser gitignore, string str, bool expected)
        {
            Assert.AreEqual(expected,
#if DEBUG
                    gitignore.Accepts(str, expected)
#else
                    gitignore.Accepts(str)
#endif
                    , $"Expected '{str}' to be {(expected ? "accepted" : "rejected")}!");
        }

        private static void gitignoreDenies(GitignoreParser gitignore, string str, bool expected)
        {
            Assert.AreEqual(expected,
#if DEBUG
                    gitignore.Denies(str, expected)
#else
                    gitignore.Denies(str)
#endif
                    , $"Expected '{str}' to be {(expected ? "denied" : "accepted")}!");
        }

        [TestClass]
        public class TestTestCaseA
        {
            [TestMethod("should only accept a/2/a")]
            public void OnlyAcceptA2A()
            {
                var gitignore = new GitignoreParser(File.ReadAllText(@"..\..\..\a\.gitignore-fixture", Encoding.UTF8));
                gitignoreAccepts(gitignore, "a/2/a", true);
                gitignoreAccepts(gitignore, "a/3/a", false);
            }
        }

        [TestClass]
        public class TestIssueN11
        {
            [TestMethod("should not fail test A")]
            public void TestA()
            {
                var gitignore = new GitignoreParser("foo.txt");
                gitignoreDenies(gitignore, "foo.txt", true);
                gitignoreAccepts(gitignore, "foo.txt", false);
            }

            [TestMethod("should not fail test B")]
            public void TestB()
            {
                var gitignore = new GitignoreParser("foo.txt");
                gitignoreDenies(gitignore, "a/foo.txt", true);
                gitignoreAccepts(gitignore, "a/foo.txt", false);
            }
        }

        [TestClass]
        public class TestIssueN12
        {
            [TestMethod("should not fail test A")]
            public void TestA()
            {
                var gitignore = new GitignoreParser("/ajax/libs/bPopup/*b*");
                gitignoreAccepts(gitignore, "/ajax/libs/bPopup/0.9.0", true);  //output false
            }

            [TestMethod("should not fail test B")]
            public void TestB()
            {
                var gitignore = new GitignoreParser("/ajax/libs/jquery-form-validator/2.2");
                gitignoreAccepts(gitignore, "/ajax/libs/jquery-form-validator/2.2.43", true); //output false
            }

            [TestMethod("should not fail test C")]
            public void TestC()
            {
                var gitignore = new GitignoreParser("/ajax/libs/punycode/2.0");
                gitignoreAccepts(gitignore, "/ajax/libs/punycode/2.0.0", true); //output false
            }

            [TestMethod("should not fail test D")]
            public void TestD()
            {
                var gitignore = new GitignoreParser("/ajax/libs/typescript/*dev*");
                gitignoreAccepts(gitignore, "/ajax/libs/typescript/2.0.6-insiders.20161014", true); //output false
            }
        }

        [TestClass]
        public class TestIssueN14
        {
            [TestMethod("should not fail test A")]
            public void TestA()
            {
                var gitignore = new GitignoreParser("node-modules");
                gitignoreDenies(gitignore, "packages/my-package/node-modules", true);
                gitignoreAccepts(gitignore, "packages/my-package/node-modules", false);

                gitignoreDenies(gitignore, "packages/my-package/node-modules/a", true);
                gitignoreAccepts(gitignore, "packages/my-package/node-modules/a", false);

                gitignoreDenies(gitignore, "node-modules/a", true);
                gitignoreAccepts(gitignore, "node-modules/a", false);
            }
        }

#if DEBUG
        [TestClass]
        // when using the `expected` argument, accepts/denies should fire the `Diagnose()` callback
        public class TestDiagnose
        {
            [TestMethod("Accepts() should fail and call Diagnose()")]
            public void TestA()
            {
                var gitignore = new GitignoreParser("node_modules");
                int count = 0;
                gitignore.OnFail += (sender, e) => count++;

                Assert.AreEqual(false, gitignore.Accepts("node_modules", true  /* INTENTIONALLY WRONG */), "Accepts() call is expected to return FALSE!");
                Assert.AreEqual(1, count, "gitignore.Diagnose() should have been invoked once from inside the Accepts() call!");
                // the next one is matching the expectation, hence no Diagnose() call:
                Assert.AreEqual(false, gitignore.Accepts("node_modules", false), "Accepts() call is expected to return FALSE!");
                Assert.AreEqual(1, count, "gitignore.Diagnose() should have been invoked once from inside the Accepts() call!");

                Assert.AreEqual(true, gitignore.Denies("node_modules", false  /* INTENTIONALLY WRONG */), "Denies() call is expected to return TRUE!");
                Assert.AreEqual(2, count, "gitignore.Diagnose() should have been invoked once from inside the Denies() call!");
                // the next one is matching the expectation, hence no Diagnose() call:
                Assert.AreEqual(true, gitignore.Denies("node_modules", true), "Denies() call is expected to return TRUE!");
                Assert.AreEqual(2, count, "gitignore.Diagnose() should have been invoked once from inside the Denies() call!");

                Assert.AreEqual(true, gitignore.Inspects("node_modules", false  /* INTENTIONALLY WRONG */), "Inspects() call is expected to return TRUE!");
                Assert.AreEqual(3, count, "gitignore.Diagnose() should have been invoked once from inside the Inspects() call!");
                // the next one is matching the expectation, hence no Diagnose() call:
                Assert.AreEqual(true, gitignore.Inspects("node_modules", true), "Inspects() call is expected to return TRUE!");
                Assert.AreEqual(3, count, "gitignore.Diagnose() should have been invoked once from inside the Inspects() call!");
            }
        }
#endif

        /* TODO: Consider using the "fixtures/gitignore.manpage.txt" file to generate more tests. */
    }
}

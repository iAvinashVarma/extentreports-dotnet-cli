﻿using AventStack.ExtentReports.MarkupUtils;
using AventStack.ExtentReports.CLI.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace AventStack.ExtentReports.CLI.Parser
{
    internal class XUnitParser : IParser
    {
        private ExtentReports _extent;
        private const string UnknownKey = "unknown";

        public XUnitParser(ExtentReports extent)
        {
            _extent = extent;
        }

        public void ParseTestRunnerOutput(string resultsFile)
        {
            var doc = XDocument.Load(resultsFile);
            DateTime timeStampParsed;

            if (doc.Root == null)
            {
                throw new NullReferenceException("Root element not found for " + resultsFile);
            }

            AddSystemInformation(doc);

            var suites = doc
                .Descendants("collection");

            foreach (var ts in suites.ToList())
            {
                var testName = GetTestName(ts);
                var test = _extent.CreateTest(testName);

                // any error messages and/or stack-trace
                var failure = ts.Element("failure");
                if (failure != null)
                {
                    var message = failure.Element("message");
                    if (message != null)
                    {
                        test.Fail(message.Value);
                    }

                    var stacktrace = failure.Element("stack-trace");
                    if (stacktrace != null && !string.IsNullOrWhiteSpace(stacktrace.Value))
                    {
                        test.Fail(MarkupHelper.CreateCodeBlock(stacktrace.Value));
                    }
                }

                var output = ts.Element("output")?.Value;
                if (!string.IsNullOrWhiteSpace(output))
                {
                    test.Info(output);
                }

                // get test suite level categories
                var suiteCategories = ParseTags(ts, false);

                // Test Cases
                foreach (var tc in ts.Descendants("test").ToList())
                {
                    var node = CreateNode(tc, test);

                    AssignStatusAndMessage(tc, node);
                    AssignTags(tc, node);

                    if (tc.Attribute("start-time") != null)
                    {
                        DateTime.TryParse(tc.Attribute("start-time").Value, out timeStampParsed);
                        node.Model.StartTime = timeStampParsed;
                    }
                    if (tc.Attribute("end-time") != null)
                    {
                        DateTime.TryParse(tc.Attribute("end-time").Value, out timeStampParsed);
                        node.Model.EndTime = timeStampParsed;
                    }
                }
            }
        }

        private static string GetTestName(XElement ts)
        {
            var testName = ts.Attribute("name").Value;
            return testName.Replace("Test collection for ", string.Empty);
        }

        private static ExtentTest CreateNode(XElement tc, ExtentTest test)
        {
            var name = tc.Attribute("name").Value;
            var descriptions =
                tc.Descendants("property")
                .Where(c => c.Attribute("name").Value.Equals("Description", StringComparison.CurrentCultureIgnoreCase));
            var description = descriptions.Any() ? descriptions.ToArray()[0].Attribute("value").Value : string.Empty;
            var node = test.CreateNode(name, description);
            return node;
        }

        private static void AssignStatusAndMessage(XElement tc, ExtentTest test)
        {
            var status = StatusExtensions.ToStatus(tc.Attribute("result").Value);

            // error and other status messages
            var statusMessage = tc.Element("failure") != null ? tc.Element("failure").Element("message").Value.Trim() : string.Empty;
            statusMessage += tc.Element("failure") != null && tc.Element("failure").Element("stack-trace") != null ? tc.Element("failure").Element("stack-trace").Value.Trim() : string.Empty;
            statusMessage += tc.Element("reason") != null && tc.Element("reason").Element("message") != null ? tc.Element("reason").Element("message").Value.Trim() : string.Empty;
            statusMessage += tc.Element("output") != null ? tc.Element("output").Value.Trim() : string.Empty;
            statusMessage = (status == Status.Fail || status == Status.Error) ? MarkupHelper.CreateCodeBlock(statusMessage).GetMarkup() : statusMessage;
            statusMessage = string.IsNullOrEmpty(statusMessage) ? status.ToString() : statusMessage;
            test.Log(status, statusMessage);
        }

        private static void AssignTags(XElement tc, ExtentTest test)
        {
            // get test case level categories
            var categories = ParseTags(tc, true);

            // if this is a parameterized test, get the categories from the parent test-suite
            var parameterizedTestElement = tc
                .Ancestors("test-suite").ToList()
                .Where(x => x.Attribute("type").Value.Equals("ParameterizedTest", StringComparison.CurrentCultureIgnoreCase))
                .FirstOrDefault();

            if (null != parameterizedTestElement)
            {
                var paramCategories = ParseTags(parameterizedTestElement, false);
                categories.UnionWith(paramCategories);
            }

            categories.ToList().ForEach(x => test.AssignCategory(x));
        }

        private static HashSet<string> ParseTags(XElement elem, bool allDescendents)
        {
            var parser = allDescendents
                ? new Func<XElement, string, IEnumerable<XElement>>((e, s) => e.Descendants(s))
                : new Func<XElement, string, IEnumerable<XElement>>((e, s) => e.Elements(s));

            var categories = new HashSet<string>();
            if (parser(elem, "categories").Any())
            {
                var tags = parser(elem, "categories").Elements("category").ToList();
                tags.ForEach(x => categories.Add(x.Attribute("name").Value));
            }

            return categories;
        }

        private void AddSystemInformation(XDocument doc)
        {
            if (doc.Descendants("assembly") == null)
                return;

            var env = doc.Descendants("assembly").FirstOrDefault();
            if (env == null)
                return;

            AddSystemInfo(env, "XUnit Version", $"{env.Attribute("test-framework")?.Value}");
            AddSystemInfo(env, "CLR Version", $"{env.Attribute("environment")?.Value}");
        }

        private void AddSystemInfo(XElement elem, string keyName, string attrName)
        {
            if (!string.IsNullOrWhiteSpace($"{elem.Attribute("os-version")?.Value}") && !$"{elem.Attribute("os-version")?.Value}".Equals(UnknownKey, StringComparison.CurrentCultureIgnoreCase))
                _extent.AddSystemInfo("NUnit Version", $"{elem.Attribute("os-version")?.Value}");
        }
    }
}
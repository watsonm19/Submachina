using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using Newtonsoft.Json;

namespace SynapticPro.TestRunner
{
    /// <summary>
    /// TestRunner service that can be called from NexusExecutor via reflection.
    /// This class is only compiled when UNITY_INCLUDE_TESTS is defined.
    /// </summary>
    public static class NexusTestRunnerService
    {
        // Static fields to store test execution state
        private static bool _isTestRunning = false;
        private static List<TestResultInfo> _testResults = new List<TestResultInfo>();
        private static int _totalTests = 0;
        private static int _completedTests = 0;
        private static string _currentTestMode = "";
        private static TestRunnerApi _testRunnerApi;

        private class TestResultInfo
        {
            public string name;
            public string status;
            public double duration;
            public string message;
        }

        /// <summary>
        /// Main entry point - called from NexusExecutor via reflection
        /// </summary>
        public static string Execute(string operation, string mode, string filter)
        {
            try
            {
                switch (operation.ToLower())
                {
                    case "run":
                        return RunTestsWithApi(mode, filter);
                    case "results":
                        return GetTestResults();
                    case "list":
                        return ListAvailableTests(mode);
                    default:
                        return JsonConvert.SerializeObject(new
                        {
                            success = false,
                            error = $"Unknown operation: {operation}. Use 'run', 'results', or 'list'."
                        });
                }
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { success = false, error = e.Message });
            }
        }

        private static string RunTestsWithApi(string testMode, string filter)
        {
            if (_isTestRunning)
            {
                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    status = "running",
                    mode = _currentTestMode,
                    totalTests = _totalTests,
                    completedTests = _completedTests,
                    message = "Tests are still running. Use operation='results' to check progress."
                });
            }

            // Reset state
            _isTestRunning = true;
            _testResults.Clear();
            _totalTests = 0;
            _completedTests = 0;
            _currentTestMode = testMode;

            // Create TestRunnerApi instance
            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();

            // Register callbacks
            _testRunnerApi.RegisterCallbacks(new SynapticTestCallbacks());

            // Build filter
            var testModeEnum = testMode.ToLower() == "playmode"
                ? TestMode.PlayMode
                : TestMode.EditMode;

            var executionSettings = new ExecutionSettings
            {
                filters = new[] { new Filter { testMode = testModeEnum } }
            };

            if (!string.IsNullOrEmpty(filter))
            {
                executionSettings.filters[0].testNames = new[] { filter };
            }

            // Start execution
            _testRunnerApi.Execute(executionSettings);

            return JsonConvert.SerializeObject(new
            {
                success = true,
                status = "started",
                mode = testMode,
                message = "Tests started. Use operation='results' to check progress and get results."
            });
        }

        private static string GetTestResults()
        {
            return JsonConvert.SerializeObject(new
            {
                success = true,
                isRunning = _isTestRunning,
                mode = _currentTestMode,
                totalTests = _totalTests,
                completedTests = _completedTests,
                results = _testResults.Select(r => new
                {
                    name = r.name,
                    status = r.status,
                    duration = r.duration,
                    message = r.message
                }).ToList(),
                summary = new
                {
                    passed = _testResults.Count(r => r.status == "Passed"),
                    failed = _testResults.Count(r => r.status == "Failed"),
                    skipped = _testResults.Count(r => r.status == "Skipped")
                }
            }, Formatting.Indented);
        }

        private static string ListAvailableTests(string testMode)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            var testModeEnum = testMode.ToLower() == "playmode"
                ? TestMode.PlayMode
                : TestMode.EditMode;

            var tests = new List<string>();

            api.RetrieveTestList(testModeEnum, (testRoot) =>
            {
                CollectTestNames(testRoot, tests);
            });

            return JsonConvert.SerializeObject(new
            {
                success = true,
                mode = testMode,
                testCount = tests.Count,
                tests = tests
            }, Formatting.Indented);
        }

        private static void CollectTestNames(ITestAdaptor test, List<string> tests)
        {
            if (test == null) return;

            if (test.IsSuite)
            {
                foreach (var child in test.Children)
                {
                    CollectTestNames(child, tests);
                }
            }
            else
            {
                tests.Add(test.FullName);
            }
        }

        private class SynapticTestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                _totalTests = CountTests(testsToRun);
                Debug.Log($"[Synaptic] Test run started. Total tests: {_totalTests}");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _isTestRunning = false;
                Debug.Log($"[Synaptic] Test run finished. Passed: {_testResults.Count(r => r.status == "Passed")}, Failed: {_testResults.Count(r => r.status == "Failed")}");
            }

            public void TestStarted(ITestAdaptor test)
            {
                // Optional: Log test start
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                _completedTests++;

                var testResult = new TestResultInfo
                {
                    name = result.Test.FullName,
                    status = result.TestStatus.ToString(),
                    duration = result.Duration,
                    message = result.Message ?? ""
                };

                _testResults.Add(testResult);
            }

            private int CountTests(ITestAdaptor test)
            {
                if (test == null) return 0;

                if (test.IsSuite)
                {
                    int count = 0;
                    foreach (var child in test.Children)
                    {
                        count += CountTests(child);
                    }
                    return count;
                }
                return 1;
            }
        }
    }
}

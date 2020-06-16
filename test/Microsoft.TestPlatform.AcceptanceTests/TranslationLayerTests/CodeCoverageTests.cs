﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.TestPlatform.AcceptanceTests.TranslationLayerTests
{
    using Microsoft.TestPlatform.TestUtilities;
    using Microsoft.TestPlatform.VsTestConsole.TranslationLayer.Interfaces;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;

    /// <summary>
    /// The Multi test run finalization tests using VsTestConsoleWrapper API's
    /// </summary>
    [TestClass]
    public class CodeCoverageTests : AcceptanceTestBase
    {
        /*
         * Below value is just safe coverage result for which all tests are passing.
         * Inspecting this value gives us confidence that there is no big drop in overall coverage.
         */
        private const double ExpectedMinimalModuleCoverage = 30.0;

        private IVsTestConsoleWrapper vstestConsoleWrapper;
        private RunEventHandler runEventHandler;
        private MultiTestRunFinalizationEventHandler multiTestRunFinalizationEventHandler;

        private void Setup()
        {
            this.vstestConsoleWrapper = this.GetVsTestConsoleWrapper();
            this.runEventHandler = new RunEventHandler();
            this.multiTestRunFinalizationEventHandler = new MultiTestRunFinalizationEventHandler();
        }

        [TestCleanup]
        public void Cleanup()
        {
            this.vstestConsoleWrapper?.EndSession();
        }

        [TestMethod]
        [NetFullTargetFrameworkDataSource]
        [NetCoreTargetFrameworkDataSource]
        public void TestRunWithCodeCoverage(RunnerInfo runnerInfo)
        {
            // arrange
            AcceptanceTestBase.SetTestEnvironment(this.testEnvironment, runnerInfo);
            this.Setup();

            // act
            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies(), this.GetCodeCoverageRunSettings(1), this.runEventHandler);

            // assert
            Assert.AreEqual(6, this.runEventHandler.TestResults.Count);

            int expectedNumberOfAttachments = testEnvironment.RunnerFramework.Equals(IntegrationTestBase.CoreRunnerFramework) &&
                testEnvironment.TargetFramework.Equals(IntegrationTestBase.CoreRunnerFramework) ? 2 : 1;
            Assert.AreEqual(expectedNumberOfAttachments, this.runEventHandler.Attachments.Count);

            AssertCoverageResults(this.runEventHandler.Attachments);
        }

        [TestMethod]
        [NetFullTargetFrameworkDataSource]
        [NetCoreTargetFrameworkDataSource]
        public void TestRunWithCodeCoverageParallel(RunnerInfo runnerInfo)
        {
            // arrange
            AcceptanceTestBase.SetTestEnvironment(this.testEnvironment, runnerInfo);
            this.Setup();

            // act
            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies(), this.GetCodeCoverageRunSettings(4), this.runEventHandler);

            // assert
            Assert.AreEqual(6, this.runEventHandler.TestResults.Count);
            Assert.AreEqual(testEnvironment.RunnerFramework.Equals(IntegrationTestBase.DesktopRunnerFramework) ? 1 : 2, this.runEventHandler.Attachments.Count);

            AssertCoverageResults(this.runEventHandler.Attachments);
        }

        [TestMethod]
        [NetFullTargetFrameworkDataSource]
        [NetCoreTargetFrameworkDataSource]
        public async Task TestRunWithCodeCoverageAndFinalization(RunnerInfo runnerInfo)
        {
            // arrange
            AcceptanceTestBase.SetTestEnvironment(this.testEnvironment, runnerInfo);
            this.Setup();

            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies().Take(1), this.GetCodeCoverageRunSettings(1), this.runEventHandler);
            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies().Skip(1), this.GetCodeCoverageRunSettings(1), this.runEventHandler);
            
            Assert.AreEqual(6, this.runEventHandler.TestResults.Count);
            Assert.AreEqual(2, this.runEventHandler.Attachments.Count);

            // act
            await this.vstestConsoleWrapper.FinalizeMultiTestRunAsync(runEventHandler.Attachments, multiTestRunFinalizationEventHandler, CancellationToken.None);

            // Assert
            multiTestRunFinalizationEventHandler.EnsureSuccess();
            Assert.AreEqual(testEnvironment.RunnerFramework.Equals(IntegrationTestBase.DesktopRunnerFramework) ? 1 : 2, this.multiTestRunFinalizationEventHandler.Attachments.Count);

            AssertCoverageResults(this.multiTestRunFinalizationEventHandler.Attachments);
        }

        [TestMethod]
        [NetFullTargetFrameworkDataSource]
        [NetCoreTargetFrameworkDataSource]
        public async Task TestRunWithCodeCoverageAndFinalizationModuleDuplicated(RunnerInfo runnerInfo)
        {
            // arrange
            AcceptanceTestBase.SetTestEnvironment(this.testEnvironment, runnerInfo);
            this.Setup();

            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies().Take(1), this.GetCodeCoverageRunSettings(1), this.runEventHandler);
            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies().Skip(1), this.GetCodeCoverageRunSettings(1), this.runEventHandler);
            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies().Skip(1), this.GetCodeCoverageRunSettings(1), this.runEventHandler);

            Assert.AreEqual(9, this.runEventHandler.TestResults.Count);
            Assert.AreEqual(3, this.runEventHandler.Attachments.Count);

            // act
            await this.vstestConsoleWrapper.FinalizeMultiTestRunAsync(runEventHandler.Attachments, multiTestRunFinalizationEventHandler, CancellationToken.None);

            // Assert
            multiTestRunFinalizationEventHandler.EnsureSuccess();
            Assert.AreEqual(testEnvironment.RunnerFramework.Equals(IntegrationTestBase.DesktopRunnerFramework) ? 1 : 3, this.multiTestRunFinalizationEventHandler.Attachments.Count);

            AssertCoverageResults(this.multiTestRunFinalizationEventHandler.Attachments);
        }

        [TestMethod]
        [NetFullTargetFrameworkDataSource]
        [NetCoreTargetFrameworkDataSource]
        public async Task TestRunWithCodeCoverageAndFinalizationCancelled(RunnerInfo runnerInfo)
        {
            // arrange
            AcceptanceTestBase.SetTestEnvironment(this.testEnvironment, runnerInfo);
            this.Setup();

            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies().Take(1), this.GetCodeCoverageRunSettings(1), this.runEventHandler);
            Assert.AreEqual(3, this.runEventHandler.TestResults.Count);
            Assert.AreEqual(1, this.runEventHandler.Attachments.Count);

            List<AttachmentSet> attachments = Enumerable.Range(0, 1000).Select(i => this.runEventHandler.Attachments.First()).ToList();

            CancellationTokenSource cts = new CancellationTokenSource();
            
            Task finalization = this.vstestConsoleWrapper.FinalizeMultiTestRunAsync(attachments, multiTestRunFinalizationEventHandler, cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(1));

            // act
            cts.Cancel();

            // Assert
            await finalization;
            multiTestRunFinalizationEventHandler.EnsureSuccess();

            if(testEnvironment.RunnerFramework.Equals(IntegrationTestBase.DesktopRunnerFramework))
            {
                Assert.AreEqual(0, this.multiTestRunFinalizationEventHandler.Attachments.Count);
            }            
        }

        [TestMethod]
        [NetFullTargetFrameworkDataSource]
        [NetCoreTargetFrameworkDataSource]
        public async Task EndSessionShouldEnsureVstestConsoleProcessDies(RunnerInfo runnerInfo)
        {
            var numOfProcesses = Process.GetProcessesByName("vstest.console").Length;

            AcceptanceTestBase.SetTestEnvironment(this.testEnvironment, runnerInfo);
            this.Setup();

            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies().Take(1), this.GetCodeCoverageRunSettings(1), this.runEventHandler);
            this.vstestConsoleWrapper.RunTests(this.GetTestAssemblies().Skip(1), this.GetCodeCoverageRunSettings(1), this.runEventHandler);

            Assert.AreEqual(6, this.runEventHandler.TestResults.Count);
            Assert.AreEqual(2, this.runEventHandler.Attachments.Count);

            await this.vstestConsoleWrapper.FinalizeMultiTestRunAsync(runEventHandler.Attachments, multiTestRunFinalizationEventHandler, CancellationToken.None);

            // act
            this.vstestConsoleWrapper?.EndSession();

            // Assert
            Assert.AreEqual(numOfProcesses, Process.GetProcessesByName("vstest.console").Length);

            this.vstestConsoleWrapper = null;
        }

        private IList<string> GetTestAssemblies()
        {
            return GetProjects().Select(p => this.GetAssetFullPath(p)).ToList();
        }

        private IList<string> GetProjects()
        {
            return new List<string> { "SimpleTestProject.dll", "SimpleTestProject2.dll" };
        }

        /// <summary>
        /// Default RunSettings
        /// </summary>
        /// <returns></returns>
        public string GetCodeCoverageRunSettings(int cpuCount)
        {
            string runSettingsXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
                                    <RunSettings>
                                        <RunConfiguration>
                                            <TargetFrameworkVersion>{FrameworkArgValue}</TargetFrameworkVersion>
                                            <TestAdaptersPaths>{GetCodeCoveragePath()}</TestAdaptersPaths>
                                            <MaxCpuCount>{cpuCount}</MaxCpuCount>
                                        </RunConfiguration>
                                        <DataCollectionRunSettings>
                                            <DataCollectors>
                                                <DataCollector friendlyName=""Code Coverage"" uri=""datacollector://Microsoft/CodeCoverage/2.0"" assemblyQualifiedName=""Microsoft.VisualStudio.Coverage.DynamicCoverageDataCollector, Microsoft.VisualStudio.TraceCollector, Version=11.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"">
                                                    <Configuration>
                                                      <CodeCoverage>
                                                        <ModulePaths>
                                                          <Exclude>
                                                            <ModulePath>.*CPPUnitTestFramework.*</ModulePath>
                                                          </Exclude>
                                                        </ModulePaths>

                                                        <!-- We recommend you do not change the following values: -->
                                                        <UseVerifiableInstrumentation>True</UseVerifiableInstrumentation>
                                                        <AllowLowIntegrityProcesses>True</AllowLowIntegrityProcesses>
                                                        <CollectFromChildProcesses>True</CollectFromChildProcesses>
                                                        <CollectAspDotNet>False</CollectAspDotNet>
                                                      </CodeCoverage>
                                                    </Configuration>
                                                </DataCollector>
                                            </DataCollectors>
                                        </DataCollectionRunSettings>
                                    </RunSettings>";
            return runSettingsXml;
        }

        private void AssertCoverageResults(IList<AttachmentSet> attachments)
        {
            if (attachments.Count == 1)
            {
                var xmlCoverage = GetXmlCoverage(attachments.First());

                foreach (var project in GetProjects())
                {
                    var moduleNode = GetModuleNode(xmlCoverage.DocumentElement, project.ToLower());
                    AssertCoverage(moduleNode, ExpectedMinimalModuleCoverage);
                }
            }
        }

        private XmlDocument GetXmlCoverage(AttachmentSet attachment)
        {
            string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".xml");

            var analyze = Process.Start(new ProcessStartInfo
            {
                FileName = GetCodeCoverageExePath(),
                Arguments = $"analyze /include_skipped_functions /include_skipped_modules /output:\"{output}\" \"{attachment.Attachments.First().Uri.LocalPath}\"",
                RedirectStandardOutput = true
            });

            string analysisOutput = analyze.StandardOutput.ReadToEnd();

            analyze.WaitForExit();
            Assert.IsTrue(0 == analyze.ExitCode, $"Code Coverage analyze failed: {analysisOutput}");

            XmlDocument coverage = new XmlDocument();
            coverage.Load(output);
            return coverage;
        }

        private string GetCodeCoveragePath()
        {
            return Path.Combine(IntegrationTestEnvironment.TestPlatformRootDirectory, "artifacts", IntegrationTestEnvironment.BuildConfiguration, "Microsoft.CodeCoverage");
        }

        private string GetCodeCoverageExePath()
        {
            return Path.Combine(GetCodeCoveragePath(), "CodeCoverage", "CodeCoverage.exe");
        }

        private XmlNode GetModuleNode(XmlNode node, string name)
        {
            return GetNode(node, "module", name);
        }

        private XmlNode GetNode(XmlNode node, string type, string name)
        {
            return node.SelectSingleNode($"//{type}[@name='{name}']");
        }

        private void AssertCoverage(XmlNode node, double expectedCoverage)
        {
            var coverage = double.Parse(node.Attributes["block_coverage"].Value);
            Console.WriteLine($"Checking coverage for {node.Name} {node.Attributes["name"].Value}. Expected at least: {expectedCoverage}. Result: {coverage}");
            Assert.IsTrue(coverage > expectedCoverage, $"Coverage check failed for {node.Name} {node.Attributes["name"].Value}. Expected at least: {expectedCoverage}. Found: {coverage}");
        }
    }
}
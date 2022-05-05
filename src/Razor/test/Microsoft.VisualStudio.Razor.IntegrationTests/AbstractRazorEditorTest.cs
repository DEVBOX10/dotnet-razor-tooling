﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.VisualStudio.Shell.Embeddable.Feedback;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Razor.IntegrationTests.InProcess;
using Microsoft.VisualStudio.Shell;
using Xunit.Harness;

namespace Microsoft.VisualStudio.Razor.IntegrationTests
{
    public abstract class AbstractRazorEditorTest : AbstractEditorTest
    {
        internal const string BlazorProjectName = "BlazorProject";

        private static readonly string s_pagesDir = Path.Combine("Pages");
        private static readonly string s_sharedDir = Path.Combine("Shared");
        internal static readonly string FetchDataRazorFile = Path.Combine(s_pagesDir, "FetchData.razor");
        internal static readonly string CounterRazorFile = Path.Combine(s_pagesDir, "Counter.razor");
        internal static readonly string IndexRazorFile = Path.Combine(s_pagesDir, "Index.razor");
        internal static readonly string ModifiedIndexRazorFile = Path.Combine(s_pagesDir, "ModifiedIndex.razor");
        internal static readonly string SemanticTokensFile = Path.Combine(s_pagesDir, "SemanticTokens.razor");
        internal static readonly string MainLayoutFile = Path.Combine(s_sharedDir, "MainLayout.razor");
        internal static readonly string ErrorCshtmlFile = Path.Combine(s_pagesDir, "Error.cshtml");
        internal static readonly string ImportsRazorFile = "_Imports.razor";

        internal static readonly string IndexPageContent = @"@page ""/""

<PageTitle>Index</PageTitle>

<h1>Hello, world!</h1>

Welcome to your new app.

<SurveyPrompt Title=""How is Blazor working for you?"" />";

        internal static readonly string MainLayoutContent = @"@inherits LayoutComponentBase

<PageTitle>BlazorApp</PageTitle>

<div class=""page"">
    <div class=""sidebar"">
        <NavMenu />
    </div>

    <main>
        <div class=""top-row px-4"">
            <a href=""https://docs.microsoft.com/aspnet/"" target=""_blank"">About</a>
        </div>

        <article class=""content px-4"">
            @Body
        </article>
    </main>
</div>
";

        private const string RazorComponentElementClassification = "RazorComponentElement";
        private const string RazorOutputLogId = "RazorOutputLog";
        private const string LogHubLogId = "RazorLogHub";

        protected override string LanguageName => LanguageNames.Razor;

        private static bool s_customLoggersAdded = false;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            // Add custom logs on failure if they haven't already been.
            if (!s_customLoggersAdded)
            {
                DataCollectionService.RegisterCustomLogger(RazorOutputPaneLogger, RazorOutputLogId, "log");
                DataCollectionService.RegisterCustomLogger(RazorLogHubLogger, LogHubLogId, "zip");

                s_customLoggersAdded = true;
            }

            await TestServices.SolutionExplorer.CreateSolutionAsync("BlazorSolution", HangMitigatingCancellationToken);
            await TestServices.SolutionExplorer.AddProjectAsync("BlazorProject", WellKnownProjectTemplates.BlazorProject, groupId: WellKnownProjectTemplates.GroupIdentifiers.Server, templateId: null, LanguageName, HangMitigatingCancellationToken);
            await TestServices.SolutionExplorer.RestoreNuGetPackagesAsync(HangMitigatingCancellationToken);
            await TestServices.Workspace.WaitForProjectSystemAsync(HangMitigatingCancellationToken);

            await TestServices.Workspace.WaitForAsyncOperationsAsync(FeatureAttribute.LanguageServer, HangMitigatingCancellationToken);

            // We open the Index.razor file, and wait for 3 RazorComponentElement's to be classified, as that
            // way we know the LSP server is up, running, and has processed both local and library-sourced Components
            await TestServices.SolutionExplorer.AddFileAsync(BlazorProjectName, ModifiedIndexRazorFile, IndexPageContent, open: true, HangMitigatingCancellationToken);

            // Razor extension doesn't launch until a razor file is opened, so wait for it to equalize
            await TestServices.Workspace.WaitForAsyncOperationsAsync(FeatureAttribute.LanguageServer, HangMitigatingCancellationToken);
            await TestServices.Workspace.WaitForAsyncOperationsAsync(FeatureAttribute.Workspace, HangMitigatingCancellationToken);
            await TestServices.Workspace.WaitForProjectSystemAsync(HangMitigatingCancellationToken);

            await EnsureExtensionInstalledAsync(HangMitigatingCancellationToken);

            try
            {
                await TestServices.Editor.WaitForClassificationAsync(HangMitigatingCancellationToken, expectedClassification: RazorComponentElementClassification, count: 3);
            }
            catch (OperationCanceledException)
            {
                // DataCollectionService does not fire in the case that errors or exceptions are thrown during Initialization.
                // Let's capture some of the things we care about most manually.
                var logHubFilePath = CreateLogFileName(LogHubLogId, "zip");
                RazorLogHubLogger(logHubFilePath);
                var outputPaneFilePath = CreateLogFileName(RazorOutputLogId, "log");
                RazorOutputPaneLogger(outputPaneFilePath);
                throw;
            }

            // Close the file we opened, just in case, so the test can start with a clean slate
            await TestServices.Editor.CloseDocumentWindowAsync(HangMitigatingCancellationToken);

            static void RazorLogHubLogger(string filePath)
            {
                var componentModel = GlobalServiceProvider.ServiceProvider.GetService<SComponentModel, IComponentModel>();
                if (componentModel is null)
                {
                    // Unable to get componentModel
                    return;
                }

                var feedbackFileProviders = componentModel.GetExtensions<IFeedbackDiagnosticFileProvider>();

                // Collect all the file names first since they can kick of file creation events that might need extra time to resolve.
                var files = new List<string>();
                foreach (var feedbackFileProvider in feedbackFileProviders)
                {
                    files.AddRange(feedbackFileProvider.GetFiles());
                }

                _ = CollectLogHubAsync(files, filePath);
            }

            static async Task CollectLogHubAsync(IEnumerable<string> files, string destination)
            {
                // What's important in this weird threading stuff is ensuring we vacate the thread RazorLogHubLogger was called on
                // because if we don't it ends up blocking the thread that creates the zip file we need.
                await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    foreach (var file in files)
                    {
                        var name = Path.GetFileName(file);

                        // Only caputre loghub
                        if (name.Contains("LogHub") && Path.GetExtension(file) == ".zip")
                        {
                            await Task.Run(() =>
                            {
                                WaitForFileExistsAsync(file);
                                if (File.Exists(file))
                                {
                                    File.Copy(file, destination);
                                }
                            });
                        }
                    }
                });
            }

            static void RazorOutputPaneLogger(string filePath)
            {
                // JoinableTaskFactory.Run isn't an option because we might be disposing already.
                // Don't use ThreadHelper.JoinableTaskFactory in test methods, but it's correct here.
#pragma warning disable VSTHRD103 // Call async methods when in an async method
                ThreadHelper.JoinableTaskFactory.Run(async () =>
#pragma warning restore VSTHRD103 // Call async methods when in an async method
                {
                    try
                    {
                        var testServices = await Extensibility.Testing.TestServices.CreateAsync(ThreadHelper.JoinableTaskFactory);
                        var paneContent = await testServices.Output.GetRazorOutputPaneContentAsync(CancellationToken.None);
                        File.WriteAllText(filePath, paneContent);
                    }
                    catch (Exception)
                    {
                        // Eat any errors so we don't block further collection
                    }
                });
            }

            static void WaitForFileExistsAsync(string file)
            {
                const int MaxRetries = 50;
                var retries = 0;
                while (!File.Exists(file) && retries < MaxRetries)
                {
                    retries++;
                    // Free your thread
                    Thread.Yield();
                    // Wait a bit
                    Thread.Sleep(100);
                }
            }
        }

        private async Task EnsureExtensionInstalledAsync(CancellationToken cancellationToken)
        {
            const string AssemblyName = "Microsoft.AspNetCore.Razor.LanguageServer";
            using var semaphore = new SemaphoreSlim(1);
            await semaphore.WaitAsync(cancellationToken);

            AppDomain.CurrentDomain.AssemblyLoad += CurrentDomain_AssemblyLoad;

            var localAppData = Environment.GetEnvironmentVariable("LocalAppData");
            Assembly? assembly = null;
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                assembly = assemblies.FirstOrDefault((assembly) => assembly.GetName().Name.Equals(AssemblyName));
                if (assembly is null)
                {
                    await semaphore.WaitAsync(cancellationToken);
                }

                semaphore.Release();
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyLoad -= CurrentDomain_AssemblyLoad;
            }

            if (assembly is null)
            {
                throw new NotImplementedException($"Integration test did not load extension");
            }

            var version = assembly.GetName().Version;

            if (!version.Equals(new Version(42, 42, 42, 42)) || !assembly.Location.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotImplementedException($"Integration test not running against Experimental Extension {assembly.Location}");
            }

            void CurrentDomain_AssemblyLoad(object sender, AssemblyLoadEventArgs args)
            {
                if (args.LoadedAssembly.GetName().Name.Equals(AssemblyName, StringComparison.Ordinal))
                {
                    assembly = args.LoadedAssembly;
                    semaphore.Release();
                }
            }
        }

        // We use reflection to get at a couple of the internals of DataCollectionService so that we use the propper LogDirectory.
        private static string CreateLogFileName(string logId, string extension)
        {
            var dataCollectionServiceType = typeof(DataCollectionService);
            var getLogDirectoryMethod = dataCollectionServiceType.GetMethod("GetLogDirectory", BindingFlags.Static | BindingFlags.NonPublic);
            var logDirectory = getLogDirectoryMethod.Invoke(obj: null, new object[] { });

            var createLogFileNameMethod = dataCollectionServiceType.GetMethod("CreateLogFileName", BindingFlags.Static | BindingFlags.NonPublic);
            var timestamp = DateTimeOffset.UtcNow;
            var testName = "TestInitialization";
            var errorId = "InitializationError";
            var @params = new object[] { logDirectory, timestamp, testName, errorId, logId, extension };
            var logFileName = (string)createLogFileNameMethod.Invoke(obj: null, @params);
            return logFileName;
        }
    }
}

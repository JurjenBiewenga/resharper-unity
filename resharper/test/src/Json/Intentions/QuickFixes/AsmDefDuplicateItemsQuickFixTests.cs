﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Metadata.Reader.API;
using JetBrains.ProjectModel.Update;
using JetBrains.ReSharper.FeaturesTestFramework.Intentions;
using JetBrains.ReSharper.Plugins.Unity.Json.Feature.Services.QuickFixes;
using JetBrains.ReSharper.TestFramework;
using JetBrains.Util;
using NUnit.Framework;
using PlatformID = JetBrains.Application.platforms.PlatformID;

namespace JetBrains.ReSharper.Plugins.Unity.Tests.Json.Intentions.QuickFixes
{
    [TestUnity]
    [TestFileExtension(".asmdef")]
    public class AsmDefDuplicateItemsQuickFixAvailabilityTests : QuickFixAvailabilityTestBase
    {
        protected override string RelativeTestDataPath => @"Json\Intentions\QuickFixes\DuplicateItems\Availability";

        [Test] public void Test01() { DoNamedTest("Test01_SecondProject.asmdef"); }
        [Test] public void Test02() { DoNamedTest("Test02_SecondProject.asmdef"); }

        // If we don't have valid (but duplicated references), the invalid reference error trumps the duplicate item warning
#if RESHARPER
        protected override TestSolutionConfiguration CreateSolutionConfiguration(PlatformID platformID,
            ICollection<KeyValuePair<TargetFrameworkId, IEnumerable<string>>> referencedLibraries,
            IEnumerable<string> fileSet)
#else
        protected override TestSolutionConfiguration CreateSolutionConfiguration(
            ICollection<KeyValuePair<Util.Dotnet.TargetFrameworkIds.TargetFrameworkId, IEnumerable<string>>> referencedLibraries,
            IEnumerable<string> fileSet)
#endif
        {
            if (fileSet == null)
                throw new ArgumentNullException(nameof(fileSet));

            var mainProjectFileSet = fileSet.Where(filename => !filename.Contains("_SecondProject"));
            var mainAbsoluteFileSet = mainProjectFileSet.Select(path => TestDataPath2.Combine(path)).ToList();

            var descriptors = new Dictionary<IProjectDescriptor, IList<Pair<IProjectReferenceDescriptor, IProjectReferenceProperties>> >();

            var mainDescriptorPair = CreateProjectDescriptor(
#if RESHARPER
                platformID,
#endif
                ProjectName, ProjectName, mainAbsoluteFileSet,
                referencedLibraries, ProjectGuid);
            descriptors.Add(mainDescriptorPair.First, mainDescriptorPair.Second);

            var referencedProjectFileSet = fileSet.Where(filename => filename.Contains("_SecondProject")).ToList();
            if (Enumerable.Any(referencedProjectFileSet))
            {
                var secondAbsoluteFileSet =
                    referencedProjectFileSet.Select(path => TestDataPath2.Combine(path)).ToList();
                var secondProjectName = "Second_" + ProjectName;
                var secondDescriptorPair = CreateProjectDescriptor(
#if RESHARPER
                    platformID,
#endif
                    secondProjectName, secondProjectName,
                    secondAbsoluteFileSet, referencedLibraries, SecondProjectGuid);
                descriptors.Add(secondDescriptorPair.First, secondDescriptorPair.Second);
            }

            return new TestSolutionConfiguration(SolutionFileName, descriptors);
        }
    }

    [TestUnity]
    [TestFileExtension(".asmdef")]
    public class AsmDefDuplicateItemsQuickFixTests : QuickFixTestBase<AsmDefRemoveDuplicateItemQuickFix>
    {
        protected override string RelativeTestDataPath => @"Json\Intentions\QuickFixes\DuplicateItems";

        [Test] public void Test01() { DoNamedTest(); }
        [Test] public void Test02() { DoNamedTest(); }
    }
}
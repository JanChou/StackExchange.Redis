﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace StackExchange.Redis.Tests
{
    /// <summary>
    /// Override for <see cref="Xunit.FactAttribute"/> that truncates our DisplayName down.
    /// 
    /// Attribute that is applied to a method to indicate that it is a fact that should
    /// be run by the test runner. It can also be extended to support a customized definition
    /// of a test method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("StackExchange.Redis.Tests.FactDiscoverer", "StackExchange.Redis.Tests")]
    public class FactAttribute : Xunit.FactAttribute
    {
        public RedisFeatures Requires { get; set; }
    }

    /// <summary>
    /// Override for <see cref="Xunit.TheoryAttribute"/> that truncates our DisplayName down.
    /// 
    /// Marks a test method as being a data theory. Data theories are tests which are
    /// fed various bits of data from a data source, mapping to parameters on the test
    /// method. If the data source contains multiple rows, then the test method is executed
    /// multiple times (once with each data row). Data is provided by attributes which
    /// derive from Xunit.Sdk.DataAttribute (notably, Xunit.InlineDataAttribute and Xunit.MemberDataAttribute).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("StackExchange.Redis.Tests.TheoryDiscoverer", "StackExchange.Redis.Tests")]
    public class TheoryAttribute : Xunit.TheoryAttribute { }

    public class FactDiscoverer : Xunit.Sdk.FactDiscoverer
    {
        public FactDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

        protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
            => new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), testMethod);
    }

    public class TheoryDiscoverer : Xunit.Sdk.TheoryDiscoverer
    {
        public TheoryDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow)
            => new[] { new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), testMethod, dataRow) };

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForSkip(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, string skipReason)
            => new[] { new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), testMethod) };

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute)
            => new[] { new SkippableTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), testMethod) };

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForSkippedDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow, string skipReason)
            => new[] { new NamedSkippedDataRowTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), testMethod, skipReason, dataRow) };
    }

    public class SkippableTestCase : XunitTestCase
    {
        protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
            base.GetDisplayName(factAttribute, displayName).StripName();

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public SkippableTestCase() { }

        public SkippableTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, ITestMethod testMethod, object[] testMethodArguments = null)
            : base(diagnosticMessageSink, defaultMethodDisplay, testMethod, testMethodArguments)
        {
        }

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            var skipMessageBus = new SkippableMessageBus(messageBus);
            var result = await base.RunAsync(diagnosticMessageSink, skipMessageBus, constructorArguments, aggregator, cancellationTokenSource).ConfigureAwait(false);
            return result.Update(skipMessageBus);
        }
    }

    public class SkippableTheoryTestCase : XunitTheoryTestCase
    {
        protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
            base.GetDisplayName(factAttribute, displayName).StripName();

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public SkippableTheoryTestCase() { }

        public SkippableTheoryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, ITestMethod testMethod)
            : base(diagnosticMessageSink, defaultMethodDisplay, testMethod) { }

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            var skipMessageBus = new SkippableMessageBus(messageBus);
            var result = await base.RunAsync(diagnosticMessageSink, skipMessageBus, constructorArguments, aggregator, cancellationTokenSource).ConfigureAwait(false);
            return result.Update(skipMessageBus);
        }
    }

    public class NamedSkippedDataRowTestCase : XunitSkippedDataRowTestCase
    {
        protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
            base.GetDisplayName(factAttribute, displayName).StripName();

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public NamedSkippedDataRowTestCase() { }

        public NamedSkippedDataRowTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, ITestMethod testMethod, string skipReason, object[] testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, testMethod, skipReason, testMethodArguments) { }
    }

    public class SkippableMessageBus : IMessageBus
    {
        private readonly IMessageBus InnerBus;
        public SkippableMessageBus(IMessageBus innerBus) => InnerBus = innerBus;

        public int DynamicallySkippedTestCount { get; private set; }

        public void Dispose() { }

        public bool QueueMessage(IMessageSinkMessage message)
        {
            if (message is ITestFailed testFailed)
            {
                var exceptionType = testFailed.ExceptionTypes.FirstOrDefault();
                if (exceptionType == typeof(SkipTestException).FullName)
                {
                    DynamicallySkippedTestCount++;
                    return InnerBus.QueueMessage(new TestSkipped(testFailed.Test, testFailed.Messages.FirstOrDefault()));
                }
            }
            return InnerBus.QueueMessage(message);
        }
    }

    internal static class XUnitExtensions
    {
        internal static string StripName(this string name) =>
            name.Replace("StackExchange.Redis.Tests.", "");

        public static RunSummary Update(this RunSummary summary, SkippableMessageBus bus)
        {
            if (bus.DynamicallySkippedTestCount > 0)
            {
                summary.Failed -= bus.DynamicallySkippedTestCount;
                summary.Skipped += bus.DynamicallySkippedTestCount;
            }
            return summary;
        }
    }
}
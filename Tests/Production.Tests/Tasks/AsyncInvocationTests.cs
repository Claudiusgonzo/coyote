﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if BINARY_REWRITE
using System.Threading.Tasks;
#else
using Microsoft.Coyote.Tasks;
#endif
using Microsoft.Coyote.Specifications;
using Xunit;
using Xunit.Abstractions;

#if BINARY_REWRITE
namespace Microsoft.Coyote.BinaryRewriting.Tests.Tasks
#else
namespace Microsoft.Coyote.Production.Tests.Tasks
#endif
{
    public class AsyncInvocationTests : BaseProductionTest
    {
        public AsyncInvocationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static async Task<T> InvokeAsync<T>(T value)
        {
            await Task.CompletedTask;
            return value;
        }

        [Fact(Timeout = 5000)]
        public void TestExpectedIdInTaskWithAction()
        {
            this.TestWithError(async () =>
            {
                int result = await InvokeAsync<int>(3);
                Specification.Assert(result is 3, "Unexpected value {0}.", result);
                Specification.Assert(false, "Reached test assertion.");
            },
            configuration: GetConfiguration().WithTestingIterations(200),
            expectedError: "Reached test assertion.",
            replay: true);
        }

        [Fact(Timeout = 5000)]
        public void TestCompletedTask()
        {
            Task task = Task.CompletedTask;
            Assert.True(task.IsCompleted);
        }
    }
}

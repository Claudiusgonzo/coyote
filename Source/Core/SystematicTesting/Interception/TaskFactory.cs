﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.Runtime;

namespace Microsoft.Coyote.SystematicTesting.Interception
{
#pragma warning disable CA1822 // Mark members as static
#pragma warning disable CA1068 // CancellationToken parameters must come last
    /// <summary>
    /// Provides support for creating and scheduling controlled <see cref="Task"/> objects.
    /// </summary>
    /// <remarks>This type is intended for compiler use rather than use directly in code.</remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public class TaskFactory
    {
        // Note: we are only intercepting and modeling a very limited set of APIs to enable specific scenarios such
        // as ASP.NET rewriting. Most `TaskFactory` APIs are not supported by our modeling, and we do not currently
        // aim to support user applications with code that explicitly uses the `TaskFactory`.

        /// <summary>
        /// Cache of methods for optimizing invocation to the task controller, when
        /// the static type of the task generic argument is not available.
        /// </summary>
        private readonly ConcurrentDictionary<Type, MethodInfo> MethodCache = new ConcurrentDictionary<Type, MethodInfo>();

        /// <summary>
        /// The default task continuation options for this task factory.
        /// </summary>
        public TaskContinuationOptions ContinuationOptions => Task.Factory.ContinuationOptions;

        /// <summary>
        /// The default task cancellation token for this task factory.
        /// </summary>
        public CancellationToken CancellationToken => Task.Factory.CancellationToken;

        /// <summary>
        /// The default task creation options for this task factory.
        /// </summary>
        public TaskCreationOptions CreationOptions => Task.Factory.CreationOptions;

        /// <summary>
        /// The default task scheduler for this task factory.
        /// </summary>
        public TaskScheduler Scheduler => Task.Factory.Scheduler;

        /// <summary>
        /// Creates and starts a <see cref="Task"/>.
        /// </summary>
        public Task StartNew(Action action) => this.StartNew(action, CancellationToken.None);

        /// <summary>
        /// Creates and starts a <see cref="Task"/>.
        /// </summary>
        public Task StartNew(Action action, CancellationToken cancellationToken) => CoyoteRuntime.IsExecutionControlled ?
            ControlledRuntime.Current.TaskController.ScheduleAction(action, null, false, false, cancellationToken) :
            Task.Factory.StartNew(action, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task"/>.
        /// </summary>
        public Task StartNew(Action action, TaskCreationOptions creationOptions) =>
            this.StartNew(action, default, creationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task"/>.
        /// </summary>
        public Task StartNew(Action action, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.StartNew)} is not supported during systematic testing.") :
            Task.Factory.StartNew(action, cancellationToken, creationOptions, scheduler);

        /// <summary>
        /// Creates and starts a <see cref="Task"/>.
        /// </summary>
        public Task StartNew(Action<object> action, object state) =>
            this.StartNew(action, state, default, TaskCreationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task"/>.
        /// </summary>
        public Task StartNew(Action<object> action, object state, CancellationToken cancellationToken) =>
            this.StartNew(action, state, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task"/>.
        /// </summary>
        public Task StartNew(Action<object> action, object state, TaskCreationOptions creationOptions) =>
            this.StartNew(action, state, default, creationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task"/>.
        /// </summary>
        public Task StartNew(Action<object> action, object state, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.StartNew)} is not supported during systematic testing.") :
            Task.Factory.StartNew(action, state, cancellationToken, creationOptions, scheduler);

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        public Task<TResult> StartNew<TResult>(Func<TResult> function) => this.StartNew(function, CancellationToken.None);

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        public Task<TResult> StartNew<TResult>(Func<TResult> function, CancellationToken cancellationToken)
        {
            if (CoyoteRuntime.IsExecutionControlled)
            {
                Type resultType = typeof(TResult);
                if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    // TODO: we can optimize this further to avoid the cost of reflection and array allocations by doing
                    // binary rewriting to an instantiated override of this method, but that will make rewriting much more
                    // complex, which is not currently worth it as this is a non-typical method invocation.
                    MethodInfo method = this.MethodCache.GetOrAdd(resultType,
                        type => typeof(TaskController).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).
                        First(m => m.IsGenericMethodDefinition && m.Name is "ScheduleFunction").
                        MakeGenericMethod(type.GetGenericArguments()));
                    return (Task<TResult>)method.Invoke(ControlledRuntime.Current.TaskController, new object[] { function, null, cancellationToken });
                }
                else if (!resultType.IsGenericType && function is Func<Task> taskFunction)
                {
                    return ControlledRuntime.Current.TaskController.ScheduleFunction(taskFunction, null, cancellationToken) as Task<TResult>;
                }

                return ControlledRuntime.Current.TaskController.ScheduleFunction(function, null, cancellationToken);
            }

            return Task.Factory.StartNew(function, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default);
        }

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        public Task<TResult> StartNew<TResult>(Func<TResult> function, TaskCreationOptions creationOptions) =>
            this.StartNew(function, default, creationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        public Task<TResult> StartNew<TResult>(Func<TResult> function, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.StartNew)} is not supported during systematic testing.") :
            Task.Factory.StartNew(function, cancellationToken, creationOptions, scheduler);

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state) =>
            this.StartNew(function, state, default, TaskCreationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state, CancellationToken cancellationToken) =>
            this.StartNew(function, state, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state, TaskCreationOptions creationOptions) =>
            this.StartNew(function, state, default, creationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates and starts a <see cref="Task{TResult}"/>.
        /// </summary>
        public Task<TResult> StartNew<TResult>(Func<object, TResult> function, object state, CancellationToken cancellationToken,
            TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.StartNew)} is not supported during systematic testing.") :
            Task.Factory.StartNew(function, state, cancellationToken, creationOptions, scheduler);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction) =>
            this.ContinueWhenAll(tasks, continuationAction, default, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, CancellationToken cancellationToken) =>
            this.ContinueWhenAll(tasks, continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, TaskContinuationOptions continuationOptions) =>
            this.ContinueWhenAll(tasks, continuationAction, default, continuationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.ContinueWhenAll)} is not supported during systematic testing.") :
            Task.Factory.ContinueWhenAll(tasks, continuationAction, cancellationToken, continuationOptions, scheduler);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction) =>
            this.ContinueWhenAll(tasks, continuationAction, default, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction,
            CancellationToken cancellationToken) =>
            this.ContinueWhenAll(tasks, continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction,
            TaskContinuationOptions continuationOptions) =>
            this.ContinueWhenAll(tasks, continuationAction, default, continuationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.ContinueWhenAll)} is not supported during systematic testing.") :
            Task.Factory.ContinueWhenAll(tasks, continuationAction, cancellationToken, continuationOptions, scheduler);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction) =>
            this.ContinueWhenAll(tasks, continuationFunction, default, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction,
            CancellationToken cancellationToken) =>
            this.ContinueWhenAll(tasks, continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction,
            TaskContinuationOptions continuationOptions) =>
            this.ContinueWhenAll(tasks, continuationFunction, default, continuationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.ContinueWhenAll)} is not supported during systematic testing.") :
            Task.Factory.ContinueWhenAll(tasks, continuationFunction, cancellationToken, continuationOptions, scheduler);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>[], TResult> continuationFunction) =>
            this.ContinueWhenAll(tasks, continuationFunction, default, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken) =>
            this.ContinueWhenAll(tasks, continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            this.ContinueWhenAll(tasks, continuationFunction, default, continuationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that starts when a set of specified tasks has completed.
        /// </summary>
        public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.ContinueWhenAll)} is not supported during systematic testing.") :
            Task.Factory.ContinueWhenAll(tasks, continuationFunction, cancellationToken, continuationOptions, scheduler);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction) =>
            this.ContinueWhenAny(tasks, continuationAction, default, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, CancellationToken cancellationToken) =>
            this.ContinueWhenAny(tasks, continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, TaskContinuationOptions continuationOptions) =>
            this.ContinueWhenAny(tasks, continuationAction, default, continuationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.ContinueWhenAny)} is not supported during systematic testing.") :
            Task.Factory.ContinueWhenAny(tasks, continuationAction, cancellationToken, continuationOptions, scheduler);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction) =>
            this.ContinueWhenAny(tasks, continuationAction, default, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction,
            CancellationToken cancellationToken) =>
            this.ContinueWhenAny(tasks, continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction,
            TaskContinuationOptions continuationOptions) =>
            this.ContinueWhenAny(tasks, continuationAction, default, continuationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.ContinueWhenAny)} is not supported during systematic testing.") :
            Task.Factory.ContinueWhenAny(tasks, continuationAction, cancellationToken, continuationOptions, scheduler);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction) =>
            this.ContinueWhenAny(tasks, continuationFunction, default, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction,
            CancellationToken cancellationToken) =>
            this.ContinueWhenAny(tasks, continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction,
            TaskContinuationOptions continuationOptions) =>
            this.ContinueWhenAny(tasks, continuationFunction, default, continuationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction,
            CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.ContinueWhenAny)} is not supported during systematic testing.") :
            Task.Factory.ContinueWhenAny(tasks, continuationFunction, cancellationToken, continuationOptions, scheduler);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>, TResult> continuationFunction) =>
            this.ContinueWhenAny(tasks, continuationFunction, default, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken) =>
            this.ContinueWhenAny(tasks, continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
            this.ContinueWhenAny(tasks, continuationFunction, default, continuationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a continuation task that will be started upon the completion of any task in the provided set.
        /// </summary>
        public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks,
            Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.ContinueWhenAny)} is not supported during systematic testing.") :
            Task.Factory.ContinueWhenAny(tasks, continuationFunction, cancellationToken, continuationOptions, scheduler);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task FromAsync(Func<AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object state) =>
            this.FromAsync(beginMethod, endMethod, state, TaskCreationOptions.None);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task FromAsync(Func<AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object state,
            TaskCreationOptions creationOptions) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(beginMethod, endMethod, state, creationOptions);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, object state) =>
            this.FromAsync(beginMethod, endMethod, state, TaskCreationOptions.None);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, object state, TaskCreationOptions creationOptions) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(beginMethod, endMethod, state, creationOptions);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task FromAsync<TArg1>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod,
            Action<IAsyncResult> endMethod, TArg1 arg1, object state) =>
            this.FromAsync(beginMethod, endMethod, arg1, state, TaskCreationOptions.None);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task FromAsync<TArg1>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod,
            TArg1 arg1, object state, TaskCreationOptions creationOptions) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(beginMethod, endMethod, arg1, state, creationOptions);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod,
            Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, object state) =>
            this.FromAsync(beginMethod, endMethod, arg1, arg2, state, TaskCreationOptions.None);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod,
            Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, object state, TaskCreationOptions creationOptions) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(beginMethod, endMethod, arg1, arg2, state, creationOptions);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod,
            Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state) =>
            this.FromAsync(beginMethod, endMethod, arg1, arg2, arg3, state, TaskCreationOptions.None);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod,
            Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state, TaskCreationOptions creationOptions) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(beginMethod, endMethod, arg1, arg2, arg3, state, creationOptions);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task<TResult> FromAsync<TArg1, TResult>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object state) =>
            this.FromAsync(beginMethod, endMethod, arg1, state, TaskCreationOptions.None);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task<TResult> FromAsync<TArg1, TResult>(Func<TArg1, AsyncCallback, object, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object state, TaskCreationOptions creationOptions) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(beginMethod, endMethod, arg1, state, creationOptions);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task<TResult> FromAsync<TArg1, TArg2, TResult>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object state) =>
            this.FromAsync(beginMethod, endMethod, arg1, arg2, state, TaskCreationOptions.None);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task<TResult> FromAsync<TArg1, TArg2, TResult>(Func<TArg1, TArg2, AsyncCallback, object, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object state, TaskCreationOptions creationOptions) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(beginMethod, endMethod, arg1, arg2, state, creationOptions);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task<TResult> FromAsync<TArg1, TArg2, TArg3, TResult>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state) =>
            this.FromAsync(beginMethod, endMethod, arg1, arg2, arg3, state, TaskCreationOptions.None);

        /// <summary>
        /// Creates a task that represents a pair of begin and end methods that conform
        /// to the Asynchronous Programming Model pattern.
        /// </summary>
        public Task<TResult> FromAsync<TArg1, TArg2, TArg3, TResult>(Func<TArg1, TArg2, TArg3, AsyncCallback, object, IAsyncResult> beginMethod,
            Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object state, TaskCreationOptions creationOptions) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(beginMethod, endMethod, arg1, arg2, arg3, state, creationOptions);

        /// <summary>
        /// Creates a task that executes an end method action when a specified <see cref="IAsyncResult"/> completes.
        /// </summary>
        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod) =>
            this.FromAsync(asyncResult, endMethod, TaskCreationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a task that executes an end method action when a specified <see cref="IAsyncResult"/> completes.
        /// </summary>
        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions creationOptions) =>
            this.FromAsync(asyncResult, endMethod, creationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a task that executes an end method action when a specified <see cref="IAsyncResult"/> completes.
        /// </summary>
        public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions creationOptions,
            TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(asyncResult, endMethod, creationOptions, scheduler);

        /// <summary>
        /// Creates a task that executes an end method action when a specified <see cref="IAsyncResult"/> completes.
        /// </summary>
        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod) =>
            this.FromAsync(asyncResult, endMethod, TaskCreationOptions.None, TaskScheduler.Default);

        /// <summary>
        /// Creates a task that executes an end method action when a specified <see cref="IAsyncResult"/> completes.
        /// </summary>
        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod,
            TaskCreationOptions creationOptions) =>
            this.FromAsync(asyncResult, endMethod, creationOptions, TaskScheduler.Default);

        /// <summary>
        /// Creates a task that executes an end method action when a specified <see cref="IAsyncResult"/> completes.
        /// </summary>
        public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod,
            TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
            CoyoteRuntime.IsExecutionControlled ?
            throw new NotSupportedException($"{nameof(Task.Factory.FromAsync)} is not supported during systematic testing.") :
            Task.Factory.FromAsync(asyncResult, endMethod, creationOptions, scheduler);
    }
#pragma warning restore CA1068 // CancellationToken parameters must come last
#pragma warning restore CA1822 // Mark members as static
}

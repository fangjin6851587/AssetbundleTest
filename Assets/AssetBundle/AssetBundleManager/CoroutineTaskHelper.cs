﻿#if NET_4_6
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetBundles
{

    public static class CoroutineTaskHelper
    {
        /// <summary>
        /// Translate a coroutine to Task and run. It needs a Mono Behavior to locate Unity thread context.
        /// </summary>
        /// <param name="monoBehavior">The Mono Behavior that managing coroutines</param>
        /// <param name="coroutine">the coroutine that needs to run </param>
        /// <returns>Task that can be await</returns>
        public static async Task StartCoroutineAsync(this MonoBehaviour monoBehavior, IEnumerator coroutine)
        {
            var tcs = new TaskCompletionSource<object>();
            monoBehavior
                .StartCoroutine(
                    emptyCoroutine(
                        coroutine,
                        tcs));
            await tcs.Task;
        }

        /// <summary>
        /// Translate a YieldInstruction to Task and run. It needs a Mono Behavior to locate Unity thread context.
        /// </summary>
        /// <param name="monoBehavior">The Mono Behavior that managing coroutines</param>
        /// <param name="yieldInstruction"></param>
        /// <returns>Task that can be await</returns>
        public static async Task StartCoroutineAsync(this MonoBehaviour monoBehavior, YieldInstruction yieldInstruction)
        {
            var tcs = new TaskCompletionSource<object>();
            monoBehavior
                .StartCoroutine(
                    emptyCoroutine(
                        yieldInstruction,
                        tcs));
            await tcs.Task;
        }

        /// <summary>
        /// Wrap a Task as a Coroutine.
        /// </summary>
        /// <param name="task">The target task.</param>
        /// <returns>Wrapped Coroutine</returns>
        public static CoroutineWithTask<System.Object> AsCoroutine(this Task task)
        {
            return new CoroutineWithTask<object>(task);
        }

        /// <summary>
        /// Wrap a Task as a Coroutine.
        /// </summary>
        /// <param name="task">The target task.</param>
        /// <returns>Wrapped Coroutine</returns>
        public static CoroutineWithTask<T> AsCoroutine<T>(this Task<T> task)
        {
            return new CoroutineWithTask<T>(task);
        }

        private static IEnumerator emptyCoroutine(YieldInstruction coro, TaskCompletionSource<object> completion)
        {
            yield return coro;
            completion.TrySetResult(null);
        }

        private static IEnumerator emptyCoroutine(IEnumerator coro, TaskCompletionSource<object> completion)
        {
            yield return coro;
            completion.TrySetResult(null);
        }

        /// <summary>
        /// Wrapped Task, behaves like a coroutine
        /// </summary>
        public struct CoroutineWithTask<T> : IEnumerator
        {

            private IEnumerator _coreCoroutine;

            /// <summary>
            /// Constructor for Task with a return value;
            /// </summary>
            /// <param name="coreTask">Task that need wrap</param>
            public CoroutineWithTask(Task<T> coreTask)
            {
                WrappedTask = Task.Run(async () =>
                {
                    await coreTask;
                    return coreTask.Result;
                });
                _coreCoroutine = new WaitUntil(() => coreTask.IsCompleted || coreTask.IsFaulted || coreTask.IsCanceled);
            }

            /// <summary>
            /// Constructor for Task without a return value;
            /// </summary>
            /// <param name="coreTask">Task that need wrap</param>
            public CoroutineWithTask(Task coreTask)
            {
                WrappedTask = Task.Run(async () =>
                {
                    await coreTask;
                    return default(T);
                });
                _coreCoroutine = new WaitUntil(() => coreTask.IsCompleted || coreTask.IsFaulted || coreTask.IsCanceled);
            }

            /// <summary>
            /// The task have wrapped in this coroutine.
            /// </summary>
            public Task<T> WrappedTask { get; private set; }


            /// <summary>
            /// Task result, if it have.  Calling this property will wait execution synchronously.
            /// </summary>
            public T Result { get { return WrappedTask.Result; } }


            /// <summary>
            /// Gets the current element in the collection.
            /// </summary>
            public object Current => _coreCoroutine.Current;

            /// <summary>
            /// Advances the enumerator to the next element of the collection.
            /// </summary>
            /// <returns>
            /// true if the enumerator was successfully advanced to the next element; false if
            /// the enumerator has passed the end of the collection.
            /// </returns>
            public bool MoveNext()
            {
                return _coreCoroutine.MoveNext();
            }

            /// <summary>
            /// Sets the enumerator to its initial position, which is before the first element
            /// in the collection.
            /// </summary>
            public void Reset()
            {
                _coreCoroutine.Reset();
            }
        }

    }
}
#endif


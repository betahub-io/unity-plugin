using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace BetaHub.Tests
{
    // Drives coroutines step by step so tests control interleaving.
    public class ManualCoroutineRunner : ICoroutineRunner
    {
        private const int DefaultMaxSteps = 1000;
        private readonly List<Stack<IEnumerator>> _routines = new List<Stack<IEnumerator>>();

        public int ActiveCount => _routines.Count;

        public void Run(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            _routines.Add(stack);
        }

        public void Step()
        {
            foreach (var stack in new List<Stack<IEnumerator>>(_routines))
            {
                var top = stack.Peek();
                if (top.MoveNext())
                {
                    if (top.Current is IEnumerator inner)
                    {
                        stack.Push(inner);
                    }
                }
                else
                {
                    stack.Pop();
                    if (stack.Count == 0)
                    {
                        _routines.Remove(stack);
                    }
                }
            }
        }

        public void RunUntilIdle(int maxSteps = DefaultMaxSteps)
        {
            for (int i = 0; i < maxSteps && _routines.Count > 0; i++)
            {
                Step();
            }
            Assert.AreEqual(0, _routines.Count, "Coroutines did not finish within step budget");
        }
    }
}

using System.Collections;

namespace BetaHub
{
    /// <summary>
    /// Abstracts "who executes this coroutine" so the submission state machine stays plain C#.
    /// In the game this is a MonoBehaviour; in tests it is a manual stepper.
    /// </summary>
    public interface ICoroutineRunner
    {
        void Run(IEnumerator routine);
    }
}

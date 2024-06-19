using UnityEngine;
using System.Collections;
using System;

// Adapted from https://jacksondunstan.com/articles/3718
public static class BH_CoroutineUtils
{
    // Calls a coroutine but if there's an error in that coroutine, it'll log the error & return
    // instead of just killing the caller.
    //
    // Call it like this:
    // 
    // yield return CoroutineUtils.Try(MyCoroutine());
    //
    public static IEnumerator Try(IEnumerator enumerator)
    {
        while (true)
        {
            object current;
            try
            {
                if (enumerator.MoveNext() == false)
                {
                    break;
                }
                current = enumerator.Current;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                yield break;
            }
            yield return current;
        }
    }

    // Use this function if you want to start a coroutine and add your own error handling behavior.
    //
    // For example:
    //
    // CoroutineUtils.StartThrowingCoroutine(
    //     this,
    //     MyCoroutine(),
    //     (ex) => {
    //          if (ex == null)
    //              Debug.Log("coroutine completed successfully!");
    //          else
    //              Debug.Log("Houson, we have a problem: " + ex);
    //     }
    // );
    //
    public static Coroutine StartThrowingCoroutine(
        MonoBehaviour monoBehaviour,
        IEnumerator enumerator,
        Action<Exception> done)
    {
        return monoBehaviour.StartCoroutine(RunThrowingIterator(enumerator, done));
    }

    public static IEnumerator RunThrowingIterator(
        IEnumerator enumerator,
        Action<Exception> done)
    {
        while (true)
        {
            object current;
            try
            {
                if (enumerator.MoveNext() == false)
                {
                    break;
                }
                current = enumerator.Current;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                done?.Invoke(ex);
                yield break;
            }
            yield return current;
        }
        done?.Invoke(null);
    }
}
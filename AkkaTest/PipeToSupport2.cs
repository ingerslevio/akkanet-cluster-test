using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace AkkaTest
{
    /// <summary>
    /// Creates the PipeTo pattern for automatically sending the results of completed tasks
    ///             into the inbox of a designated Actor
    /// 
    /// </summary>
    public static class PipeToSupport2
    {
        /// <summary>
        /// Pipes the output of a Task directly to the <see cref="!:recipient"/>'s mailbox once
        ///             the task completes
        /// 
        /// </summary>
        public static Task PipeTo2<T>(this Task<T> taskToPipe, ICanTell recipient, IActorRef sender = null, Func<T, object> success = null, Func<Exception, object> failure = null)
        {
            sender = sender ?? ActorRefs.NoSender;
            return taskToPipe.ContinueWith((Action<Task<T>>)(tresult =>
            {
                if (tresult.IsCanceled || tresult.IsFaulted)
                {
                    recipient.Tell(failure != null
                        ? failure((Exception) tresult.Exception)
                        : new Status.Failure((Exception) tresult.Exception), sender);
                }
                else
                {
                    if (!tresult.IsCompleted)
                        return;

                    recipient.Tell(success != null 
                        ? success(tresult.Result) 
                        : tresult.Result, sender);
                }
            }), TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
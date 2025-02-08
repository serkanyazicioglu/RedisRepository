using System;

namespace Nhea.Data.Repository.RedisRepository
{
    public static class RedisRepositoryErrorManager
    {
        public static event UnhandledExceptionEventHandler ErrorOccuredEvent;

        public static void LogException(object sender, Exception ex)
        {
            if (RedisRepositoryErrorManager.ErrorOccuredEvent != null)
            {
                var receivers = RedisRepositoryErrorManager.ErrorOccuredEvent.GetInvocationList();
                foreach (UnhandledExceptionEventHandler receiver in receivers)
                {
                    receiver.Invoke(sender, new UnhandledExceptionEventArgs(ex, false));
                }
            }
        }
    }
}

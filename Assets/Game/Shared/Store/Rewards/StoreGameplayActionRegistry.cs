namespace Game.Shared.Store
{
    public interface IFailOfferContinueHandler
    {
        bool TryCaptureFailedSession(out string sessionToken);
        bool CanContinueFailedSession(string sessionToken);
        bool TryContinueFailedSession(string sessionToken);
    }

    public static class StoreGameplayActionRegistry
    {
        private static IFailOfferContinueHandler failOfferHandler;

        public static void Register(IFailOfferContinueHandler handler)
        {
            if (handler != null)
            {
                failOfferHandler = handler;
            }
        }

        public static void Unregister(IFailOfferContinueHandler handler)
        {
            if (ReferenceEquals(failOfferHandler, handler))
            {
                failOfferHandler = null;
            }
        }

        public static bool TryCaptureFailedSession(out string sessionToken)
        {
            if (failOfferHandler != null)
            {
                return failOfferHandler.TryCaptureFailedSession(out sessionToken);
            }

            sessionToken = string.Empty;
            return false;
        }

        public static bool CanContinueFailedSession(string sessionToken)
        {
            return failOfferHandler != null &&
                   failOfferHandler.CanContinueFailedSession(sessionToken);
        }

        public static bool TryContinueFailedSession(string sessionToken)
        {
            return failOfferHandler != null &&
                   failOfferHandler.TryContinueFailedSession(sessionToken);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            failOfferHandler = null;
        }
    }
}

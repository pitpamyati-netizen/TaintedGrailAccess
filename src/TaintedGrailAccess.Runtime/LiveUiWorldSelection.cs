using System;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>Resolves live UI ownership; an additive scene may be ready under a loading overlay.</summary>
    public static class LiveUiWorldSelection
    {
        public static object Resolve(Func<object> visibleLoadingWorld, Func<object> readySceneWorld, Func<object> readyTitleWorld)
        {
            // Providers validate readiness/disposal/visibility now. Nothing is retained.
            return visibleLoadingWorld() ?? readySceneWorld() ?? readyTitleWorld();
        }
    }
}

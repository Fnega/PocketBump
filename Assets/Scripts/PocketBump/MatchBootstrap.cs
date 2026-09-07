using UnityEngine;

namespace PocketBump
{
    public sealed class MatchBootstrap : MonoBehaviour
    {
        void Awake()
        {
            if (FindObjectOfType<MatchDirector>() != null) return;
            MatchDirector.Build(GameSession.Ensure().CopySettings(), true);
        }
    }
}

using UnityEngine;

namespace PocketBump
{
    public sealed class ArenaSceneController : MonoBehaviour
    {
        [SerializeField] MatchDirector _director;
        public MatchDirector Director => _director;

        void Awake()
        {
            MatchSettings settings = GameSession.Ensure().CopySettings();
            if (_director == null) _director = FindObjectOfType<MatchDirector>();
            if (_director == null)
                _director = MatchDirector.Build(settings, true);
            else
                _director.InitializeAuthored(settings, true);
        }

        public void SetDirector(MatchDirector director)
        {
            _director = director;
        }
    }
}

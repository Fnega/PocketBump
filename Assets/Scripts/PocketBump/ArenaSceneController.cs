using UnityEngine;

// 把编辑器中布置好的 Arena 场景接入运行时比赛。
// 同时支持预置和动态创建 MatchDirector，便于正式场景与测试场景复用。
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

using UnityEngine;

namespace PocketBump
{
    // Marker for incremental editor authoring. No runtime rebuilding or recoloring.
    public sealed class DashAuthoredScene : MonoBehaviour
    {
        public const int CurrentVersion = 1;
        [SerializeField] int _version = CurrentVersion;
        [SerializeField] string _sourceBackup;
        public int Version => _version;
        public string SourceBackup => _sourceBackup;
        public void RecordBackup(string path) { _sourceBackup = path; }
    }
}

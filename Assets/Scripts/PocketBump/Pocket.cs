using System;
using UnityEngine;

namespace PocketBump
{
    public enum PocketKind
    {
        MarbleGoal,
        Dunk
    }

    public sealed class Pocket : MonoBehaviour
    {
        public PocketKind Kind;
        public int OwnerIndex = -1;

        public event Action<Pocket, Collider2D> Entered;

        void OnTriggerEnter2D(Collider2D other)
        {
            Entered?.Invoke(this, other);
        }
    }
}

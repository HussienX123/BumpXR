using UnityEngine;

namespace BumpXR.Core
{
    /// <summary>Raised fist targets a bump can land on (Demo Buddies implement this). SDK-free.</summary>
    public interface IFistTargets
    {
        int Count { get; }
        bool IsActive(int index);
        Vector3 Position(int index);
    }

    /// <summary>"Bump his hand" rules: recipient = nearest active fist within HitRadius of the bump contact point.</summary>
    public static class FistTargeting
    {
        public const float TargetRadius = 0.08f;
        public const float HitRadius = 0.12f;
        /// <summary>Wrist to fist centre along the approach (the user's fist centre sits ~7 cm ahead of the tracked wrist).</summary>
        public const float WristToKnuckles = 0.07f;

        public static Vector3 ContactPoint(BumpEvent e) => e.Position + e.Direction * WristToKnuckles;

        /// <summary>Nearest active target to <paramref name="point"/> within <paramref name="maxDistance"/>; -1 when none.</summary>
        public static int Nearest(IFistTargets targets, Vector3 point, float maxDistance = float.PositiveInfinity)
        {
            if (targets == null) return -1;
            int best = -1;
            float bestSqr = maxDistance * maxDistance;
            if (float.IsPositiveInfinity(maxDistance)) bestSqr = float.PositiveInfinity;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets.IsActive(i)) continue;
                float d = (targets.Position(i) - point).sqrMagnitude;
                if (d <= bestSqr) { bestSqr = d; best = i; }
            }

            return best;
        }

        public static int FindRecipient(IFistTargets targets, Vector3 contactPoint) => Nearest(targets, contactPoint, HitRadius);
    }
}
using System;
using UnityEngine;

namespace BumpXR.Core
{
    public struct BumpEvent
    {
        public Vector3 Position;
        public float PeakSpeed;
        public bool IsLeft;
        public float Time;
        /// <summary>Unit approach direction at the stop (zero when unknown, e.g. forced bumps).</summary>
        public Vector3 Direction;
    }

    /// <summary>Raises Bumped when a fist-bump gesture is recognized. UI and transfer code only depend on this.</summary>
    public interface IBumpDetector
    {
        event Action<BumpEvent> Bumped;

        /// <summary>Short human-readable detector state for the Dev overlay.</summary>
        string StateLabel { get; }

        /// <summary>Latest smoothed forward speeds in m/s for the Dev overlay.</summary>
        float LeftSpeed { get; }
        float RightSpeed { get; }

        /// <summary>Seconds of cooldown left (0 when ready).</summary>
        float CooldownLeft { get; }
    }
}


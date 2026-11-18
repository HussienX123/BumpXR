using UnityEngine;

namespace BumpXR.Core
{
    public enum HairStyle { Short, Bun, Cap, Curly }

    public enum BuddyExpression { Neutral, Smile, Wow, Think }

    /// <summary>Data for one procedural Demo Buddy (SDK-free). Three distinct buddies plus a tutorial coach.</summary>
    public sealed class BuddyProfile
    {
        public string Name;
        public Color Skin;
        public Color Outfit;
        public Color OutfitTrim;
        public Color HairColor;
        public HairStyle Hair;
        public float Scale = 1f;

        public static readonly BuddyProfile[] Buddies =
        {
            new BuddyProfile
            {
                Name = "Alex", Skin = new Color(0.96f, 0.78f, 0.64f), Outfit = new Color(0.20f, 0.55f, 0.95f),
                OutfitTrim = new Color(0.95f, 0.97f, 1f), HairColor = new Color(0.25f, 0.15f, 0.08f), Hair = HairStyle.Short,
                Scale = 1.0f
            },
            new BuddyProfile
            {
                Name = "Sam", Skin = new Color(0.55f, 0.36f, 0.24f), Outfit = new Color(0.95f, 0.45f, 0.25f),
                OutfitTrim = new Color(1f, 0.92f, 0.75f), HairColor = new Color(0.08f, 0.06f, 0.05f), Hair = HairStyle.Curly,
                Scale = 0.96f
            },
            new BuddyProfile
            {
                Name = "Riley", Skin = new Color(0.86f, 0.64f, 0.48f), Outfit = new Color(0.55f, 0.82f, 0.45f),
                OutfitTrim = new Color(0.20f, 0.35f, 0.18f), HairColor = new Color(0.90f, 0.62f, 0.18f), Hair = HairStyle.Bun,
                Scale = 1.03f
            }
        };

        public static readonly BuddyProfile Coach = new BuddyProfile
        {
            Name = "Coach", Skin = new Color(0.78f, 0.58f, 0.42f), Outfit = new Color(0.18f, 0.90f, 1f),
            OutfitTrim = new Color(0.05f, 0.10f, 0.20f), HairColor = new Color(0.35f, 0.35f, 0.4f), Hair = HairStyle.Cap,
            Scale = 1.0f
        };

        public static int IndexOfName(string name)
        {
            for (int i = 0; i < Buddies.Length; i++)
                if (string.Equals(Buddies[i].Name, name, System.StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}


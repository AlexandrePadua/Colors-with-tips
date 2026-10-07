using System;

namespace Pitacor.Core.Protocol
{
    /// <summary>Avatar as part indices. Counts are provisional until the avatar art exists; index 0 is always valid.</summary>
    [Serializable]
    public class AvatarSpec
    {
        public const int BodyCount = 12;
        public const int EyesCount = 12;
        public const int MouthCount = 12;
        /// <summary>Accessory 0 means "none".</summary>
        public const int AccessoryCount = 12;
        public const int ColorCount = 16;

        public int Body { get; set; }
        public int Eyes { get; set; }
        public int Mouth { get; set; }
        public int Accessory { get; set; }
        public int ColorIndex { get; set; }

        public AvatarSpec Clone() => (AvatarSpec)MemberwiseClone();

        public bool IsValid() =>
            InRange(Body, BodyCount) && InRange(Eyes, EyesCount) && InRange(Mouth, MouthCount)
            && InRange(Accessory, AccessoryCount) && InRange(ColorIndex, ColorCount);

        /// <summary>Returns a copy with every out-of-range index reset to 0. Null yields the default avatar.</summary>
        public static AvatarSpec Sanitize(AvatarSpec avatar)
        {
            if (avatar == null) return new AvatarSpec();
            return new AvatarSpec
            {
                Body = InRange(avatar.Body, BodyCount) ? avatar.Body : 0,
                Eyes = InRange(avatar.Eyes, EyesCount) ? avatar.Eyes : 0,
                Mouth = InRange(avatar.Mouth, MouthCount) ? avatar.Mouth : 0,
                Accessory = InRange(avatar.Accessory, AccessoryCount) ? avatar.Accessory : 0,
                ColorIndex = InRange(avatar.ColorIndex, ColorCount) ? avatar.ColorIndex : 0,
            };
        }

        private static bool InRange(int value, int count) => value >= 0 && value < count;
    }
}

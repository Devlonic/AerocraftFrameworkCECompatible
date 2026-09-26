using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    [StaticConstructorOnStartup]
    public static class MYDE_TexButton
    {
        public static readonly Texture2D True = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/True");
        public static readonly Texture2D False = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/False");
        public static readonly Texture2D Up = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/Up");
        public static readonly Texture2D Down = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/Down");
        public static readonly Texture2D Left = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/Left");
        public static readonly Texture2D Right = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/Right");
        public static readonly Texture2D UR = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/UR");
        public static readonly Texture2D UL = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/UL");
        public static readonly Texture2D DR = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/DR");
        public static readonly Texture2D DL = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/DL");
        public static readonly Texture2D DownByDraft = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ITab/DownByDraft");
        public static readonly Texture2D ShowRadiusfRange = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/ShowRadiusfRange");
        public static readonly Texture2D GoBack = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/GoBack");
        public static readonly Texture2D SelectPawnAndLetItReplace = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/SelectPawnAndLetItReplace");
        public static readonly Texture2D Auto = ContentFinder<Texture2D>.Get("AerocraftFramework/Icon/Auto");
        public static readonly Texture2D FullBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.35f, 0.35f, 0.2f));
        public static readonly Texture2D EmptyBar = SolidColorMaterials.NewSolidColorTexture(Color.black);
        public static readonly Texture2D AmmoFullBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.4f, 0.2f));
        public static readonly Texture2D AmmoLowBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.55f, 0.2f, 0.15f));

        /// <summary>Combat Extended's reload icon when available, otherwise the mod's own "up" arrow.</summary>
        public static readonly Texture2D Reload = ContentFinder<Texture2D>.Get("UI/Buttons/Reload", reportFailure: false) ?? Up;

        private static readonly Dictionary<string, Texture2D> iconCache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Texture of an icon path from a def, cached. Textures can only be loaded on the main thread (saves are
        /// loaded on another one): there it returns null and the caller asks again later.
        /// </summary>
        public static Texture2D IconOrDefault(string path)
        {
            if (path.NullOrEmpty())
            {
                return BaseContent.BadTex;
            }
            if (iconCache.TryGetValue(path, out Texture2D cached))
            {
                return cached;
            }
            if (!UnityData.IsInMainThread)
            {
                return null;
            }
            Texture2D texture = ContentFinder<Texture2D>.Get(path, reportFailure: false) ?? BaseContent.BadTex;
            iconCache[path] = texture;
            return texture;
        }
    }
}

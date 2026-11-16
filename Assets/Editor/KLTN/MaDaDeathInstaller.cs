#if UNITY_EDITOR
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEngine;

namespace KLTN.Game.Editor
{
    /// <summary>Imports the original atlas and creates the tunable death profile.</summary>
    public static class MaDaDeathInstaller
    {
        private const string Folder = "Assets/Art/Processed/VFX/MaDa/";

        [MenuItem("Tools/KLTN/Install Ma Da Death VFX")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            var texture = (TextureImporter)AssetImporter.GetAtPath(
                Folder + "WaterSplash.png"
            );
            texture.textureType = TextureImporterType.Default;
            texture.alphaSource = TextureImporterAlphaSource.FromInput;
            texture.alphaIsTransparency = true;
            texture.mipmapEnabled = false;
            texture.npotScale = TextureImporterNPOTScale.None;
            texture.maxTextureSize = 2048;
            texture.textureCompression = TextureImporterCompression.Uncompressed;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SaveAndReimport();

            const string path = "Assets/Resources/VFX/MaDaDeath.asset";
            var profile = AssetDatabase.LoadAssetAtPath<MaDaDeathProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<MaDaDeathProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(
                Folder + "WaterSplash.png"
            );
            profile.splashSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(
                Folder + "WaterSplash.mp3"
            );
            EditorUtility.SetDirty(profile);
            CardContentInstaller.Install();
            AssetDatabase.SaveAssets();
            Debug.Log("Ma Da death assets installed.");
        }
    }
}
#endif

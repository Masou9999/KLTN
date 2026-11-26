using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KLTN.Editor
{
    /// <summary>
    /// Unity copies the whole StreamingAssets folder into every build, and on WebGL anyone can
    /// download those files by URL. After a WebGL build this keeps only the files the browser
    /// client needs and deletes the rest (desktop OAuth secret, google-services file, examples).
    /// The project's Assets/StreamingAssets folder is never touched.
    /// </summary>
    public sealed class WebGLStreamingAssetsFilter : IPostprocessBuildWithReport
    {
        // Allowlist: a new file added to StreamingAssets stays out of WebGL until listed here.
        private static readonly HashSet<string> AllowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "firebase-auth-config.json",
        };

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
            {
                return;
            }

            string streamingAssets = Path.Combine(report.summary.outputPath, "StreamingAssets");

            if (!Directory.Exists(streamingAssets))
            {
                Debug.LogWarning($"[WebGLStreamingAssets] Không thấy {streamingAssets}; bỏ qua.");
                return;
            }

            foreach (string file in Directory.GetFiles(streamingAssets, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(streamingAssets.Length + 1).Replace('\\', '/');

                if (AllowedFiles.Contains(relative))
                {
                    continue;
                }

                File.Delete(file);
                Debug.Log($"[WebGLStreamingAssets] Đã xoá khỏi bản WebGL: StreamingAssets/{relative}");
            }

            foreach (string directory in Directory.GetDirectories(streamingAssets, "*", SearchOption.AllDirectories))
            {
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                {
                    Directory.Delete(directory);
                }
            }

            WarnIfConfigIncomplete(Path.Combine(streamingAssets, "firebase-auth-config.json"));
        }

        private static void WarnIfConfigIncomplete(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError(
                    "[WebGLStreamingAssets] Bản WebGL thiếu StreamingAssets/firebase-auth-config.json; đăng nhập sẽ lỗi.");
                return;
            }

            string json = File.ReadAllText(path);

            if (!json.Contains("\"projectId\""))
            {
                Debug.LogWarning(
                    "[WebGLStreamingAssets] firebase-auth-config.json chưa có \"projectId\"; "
                        + "Firestore trên WebGL sẽ dùng project dự phòng.");
            }
        }
    }
}

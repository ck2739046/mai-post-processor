using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using System;
using System.IO;
using CriMana;
using MAI2.Util;
using Manager;
using Monitor;
using Monitor.Game;
using UnityEngine.Video;
using Process;
using System.Threading.Tasks; 

[assembly: MelonInfo(typeof(Better_JacketAsMovie.Main), "Better_JacketAsMovie", "1.0.0", "Simon273")]
[assembly: MelonGame("sega-interactive", "Sinmai")]

namespace Better_JacketAsMovie
{
    public class Main : MelonMod
    {
        private static bool is_initialized = false;
        private static readonly string PostProcessorDir = "upscayl";
        private static MovieInfo movieInfo = new MovieInfo();
        private static uint[] bgaSize = [0, 0];



        [HarmonyPostfix]
        [HarmonyPatch(typeof(DataManager), "LoadMusicBase")]
        public static void LoadMusicPostfix(List<string> ____targetDirs)
        {
            LoadLocalImages.Initialize(____targetDirs);
        }

        public override void OnInitializeMelon()
        {
            Initialize();
            if (is_initialized)
                HarmonyInstance.PatchAll(typeof(Main));
        }

        private static void Initialize()
        {
            // check if post-processor folder exists
            var path = FileSystem.ResolvePath(PostProcessorDir);
            is_initialized = Directory.Exists(path);
            if (!is_initialized)
                MelonLogger.Warning($"Post-processor folder not found: {path}");
            else
                MelonLogger.Msg($"Load Success.");
        }

        private class MovieInfo
        {
            public enum MovieType
            {
                None,
                SourceMovie,
                Mp4Movie,
                Jacket,
                JacketProcessing
            }

            public MovieType Type { get; set; } = MovieType.None;
            public string Mp4Path { get; set; } = "";
            public Texture2D JacketTexture { get; set; }
            public string MusicId { get; set; } = "";
            public bool IsValid
            {
                get
                {
                    if (string.IsNullOrEmpty(MusicId)) return false;
                    return Type switch
                    {
                        MovieType.None => false,
                        MovieType.SourceMovie => true,
                        MovieType.Mp4Movie => !string.IsNullOrEmpty(Mp4Path),
                        MovieType.Jacket => JacketTexture != null,
                        MovieType.JacketProcessing => JacketTexture != null,
                        _ => false
                    };
                }
            }
        }






        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrackStartProcess), "OnStart")]
        public static async void GetMovie()
        {
            try
            {
                movieInfo = new MovieInfo(); // reset
                var music = Singleton<DataManager>.Instance.GetMusic(GameManager.SelectMusicID[0]);
                if (music is null) return;
                var musicID = $"{music.movieName.id:000000}";
                movieInfo.MusicId = musicID;

                // 尝试从game或LocalAssets获取jacket
                var jacket = LoadLocalImages.GetJacketTexture2D(music.movieName.id);
                if (jacket is null)
                {
                    var filename = $"Jacket/UI_Jacket_{musicID}.png";
                    jacket = AssetManager.Instance().GetJacketTexture2D(filename);
                    if (jacket is null)
                    {
                        MelonLogger.Msg($"[MovieLoader] No jacket for {musicID}");
                        return;
                    }
                }

                movieInfo.JacketTexture = jacket;
                movieInfo.Type = MovieInfo.MovieType.JacketProcessing; // 标记为开始后处理

                // 异步调用后处理函数
                jacket = await JacketPostProcess(jacket);
                if (jacket is null)
                {
                    MelonLogger.Msg($"[MovieLoader] post-process return null for {musicID}");
                    return;
                }
                movieInfo.Type = MovieInfo.MovieType.Jacket; // 后处理完成
                movieInfo.JacketTexture = jacket;

            }
            catch (System.Exception e) { MelonLogger.Msg($"[MovieLoader] GetMovie() error: {e}"); }
        }

        private static async Task<Texture2D> JacketPostProcess(Texture2D jacket)
        {
            try
            {
                var PostProcessorDir_r = FileSystem.ResolvePath(PostProcessorDir);
                if (!Directory.Exists(PostProcessorDir_r))
                {
                    MelonLogger.Msg($"[MovieLoader] Directory not found: {PostProcessorDir_r}");
                    return null;
                }
                var inputPath = Path.Combine(PostProcessorDir_r, "input.png");
                if (File.Exists(inputPath)) File.Delete(inputPath);
                var outputPath = Path.Combine(PostProcessorDir_r, "output.png");
                if (File.Exists(outputPath)) File.Delete(outputPath);

                // Output jacket as input.png for post-processing
                // Use render texture becuase jacket is not readable
                // Create temp render texture
                var renderTexture = RenderTexture.GetTemporary(jacket.width, jacket.height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                // Copy jacket to render texture
                Graphics.Blit(jacket, renderTexture);
                var jacket_copy = new Texture2D(jacket.width, jacket.height, TextureFormat.RGBA32, false);
                jacket_copy.ReadPixels(new Rect(0, 0, jacket.width, jacket.height), 0, 0);
                jacket_copy.Apply();
                // Restore previous render texture
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                // Save to disk
                File.WriteAllBytes(inputPath, jacket_copy.EncodeToPNG());
                UnityEngine.Object.Destroy(jacket_copy);
                if (!File.Exists(inputPath))
                {
                    MelonLogger.Msg($"[MovieLoader] failed to save input.png: {inputPath}");
                    return null;
                }

                // Check post-processor bat exists
                var bat_path = Path.Combine(PostProcessorDir_r, "run.bat");
                if (!File.Exists(bat_path))
                {
                    MelonLogger.Msg($"[MovieLoader] post-process bat not found: {bat_path}");
                    return null;
                }

                // Run post-processor asynchronously
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c run.bat",
                    WorkingDirectory = PostProcessorDir_r,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    CreateNoWindow = true
                };
                using var process = new System.Diagnostics.Process { StartInfo = startInfo };
                process.Start();

                // Wait for process exit with 4.5 second timeout
                var exitTask = Task.Run(() => process.WaitForExit());
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(4.5));

                if (await Task.WhenAny(exitTask, timeoutTask) == timeoutTask)
                {
                    // Timeout occurred
                    try
                    {
                        if (!process.HasExited) process.Kill();
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Msg($"[MovieLoader] failed to kill post-process: {e.Message}");
                    }
                    MelonLogger.Msg($"[MovieLoader] post-process timeout");
                    return null;
                }

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    // Load processed texture
                    var processedTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    processedTexture.LoadImage(File.ReadAllBytes(outputPath));
                    jacket = processedTexture;
                }
                else
                {
                    MelonLogger.Msg($"[MovieLoader] post-process failed, ExitCode: {process.ExitCode}");
                    return null;
                }

                // Clean up
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
                return jacket;

            }
            catch (System.Exception e)
            {
                MelonLogger.Msg($"[MovieLoader] post-process error: {e}");
                return null;
            }
        }


        private static bool _isReplaced = false;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameCtrl), "Initialize")]
        public static void LoadLocalBgaAwake(GameObject ____movieMaskObj, int ___monitorIndex)
        {
            _isReplaced = false;

            if (!movieInfo.IsValid) return;

            string mp4Path = "";
            bool mp4Exists = false;
            Texture2D jacket = null;

            switch (movieInfo.Type)
            {
                case MovieInfo.MovieType.None:
                    return;
                case MovieInfo.MovieType.SourceMovie:
                    return;
                case MovieInfo.MovieType.Mp4Movie:
                    mp4Path = movieInfo.Mp4Path;
                    mp4Exists = File.Exists(mp4Path);
                    break;
                case MovieInfo.MovieType.Jacket:
                    jacket = movieInfo.JacketTexture;
                    break;
                case MovieInfo.MovieType.JacketProcessing:
                    MelonLogger.Msg($"[MovieLoader] {movieInfo.MusicId} Post-process failed " +
                                    "or time out, using jacket as fallback");
                    jacket = movieInfo.JacketTexture;
                    break;
            }

            if (!mp4Exists && jacket is null)
            {
                MelonLogger.Msg($"[MovieLoader] No jacket or bga for {movieInfo.MusicId}");
                return;
            }

            _isReplaced = true;

            var movie = ____movieMaskObj.transform.Find("Movie");

            var sprite = movie.GetComponent<SpriteRenderer>();
            
            sprite.sprite = Sprite.Create(jacket, new Rect(0, 0, jacket.width, jacket.height), new Vector2(0.5f, 0.5f));
            sprite.material = new Material(Shader.Find("Sprites/Default"));
            bgaSize = [1080, 1080];
            
        }
        


        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameMonitor), "SetMovieMaterial")]
        public static bool SetMovieMaterial(Material material, int ___monitorIndex)
        {
            return !_isReplaced;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MovieController), "GetMovieHeight")]
        public static void GetMovieHeightPostfix(ref uint __result)
        {
            if (bgaSize[0] > 0) {
                __result = bgaSize[0];
                bgaSize[0] = 0;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MovieController), "GetMovieWidth")]
        public static void GetMovieWidthPostfix(ref uint __result)
        {
            if (bgaSize[1] > 0) {
                __result = bgaSize[1];
                bgaSize[1] = 0;
            }
        }
    }

    public static class FileSystem
    {
        public static string ResolvePath(string path)
        {
            var varExpanded = Environment.ExpandEnvironmentVariables(path);
            return Path.IsPathRooted(varExpanded)
                    ? varExpanded
                    : Path.Combine(Environment.CurrentDirectory, varExpanded);
        }
    }

    public class LoadLocalImages
    {
        private static readonly string imageAssetsDir = "LocalAssets";
        private static readonly string[] imageExts = { ".jpg", ".png", ".jpeg" };
        private static readonly Dictionary<string, string> jacketPaths = [];
        private static readonly Dictionary<string, string> localAssetsContents = [];

        public static void Initialize(List<string> targetDirs)
        {
            foreach (var aDir in targetDirs)
            {
                if (Directory.Exists(Path.Combine(aDir, @"AssetBundleImages\jacket")))
                    foreach (var file in Directory.GetFiles(Path.Combine(aDir, @"AssetBundleImages\jacket")))
                    {
                        if (!imageExts.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                        var idStr = Path.GetFileName(file).Substring("ui_jacket_".Length, 6);
                        jacketPaths[idStr] = file;
                    }
            }

            var resolvedDir = FileSystem.ResolvePath(imageAssetsDir);
            if (Directory.Exists(resolvedDir))
                foreach (var laFile in Directory.EnumerateFiles(resolvedDir))
                {
                    if (!imageExts.Contains(Path.GetExtension(laFile).ToLowerInvariant())) continue;
                    localAssetsContents[Path.GetFileNameWithoutExtension(laFile).ToLowerInvariant()] = laFile;
                }
        }

        private static string GetJacketPath(string id)
        {
            return localAssetsContents.TryGetValue(id, out var laPath) ? laPath : jacketPaths.GetValueOrDefault(id);
        }

        public static Texture2D GetJacketTexture2D(string id)
        {
            var path = GetJacketPath(id);
            if (path == null)
            {
                return null;
            }

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }
        
        public static Texture2D GetJacketTexture2D(int id)
        {
            return GetJacketTexture2D($"{id:000000}");
        }
    }
}

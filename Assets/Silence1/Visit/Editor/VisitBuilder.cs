using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace Silence1.Editor
{
    // Builds the six-times-of-day visit from the assets made in silence1-film/visit (see LOG.md there).
    // Slot order is the clock order (Step and PageUp/PageDown follow it); the first screen and first visit are found by label.
    public static class VisitBuilder
    {
        const string Root = "Assets/Silence1/Visit/";
        const string SceneFile = "Assets/Scenes/Silence1Visit.unity";
        const string DemoScene = "Assets/Scenes/Silence1Demo.unity";
        const string PlayerPath = "Build/Visit/SILENCE1 Visit.exe";

        static VisitCue Cue(float from, float to, string text) => new() { from = from, to = to, text = text };

        // Timecodes are measured from the composited clips (frame motion inside the glass and the clip audio).
        static VisitSlot[] Slots() => new[]
        {
            Slot("0200", "02:00", "배달 스쿠터", 2f, 3.5f, 8.0f, false,
                Cue(0, 1.2f, "냉장고가 낮게 도는 소리"),
                Cue(1.2f, 5.0f, "골목 안쪽에서 다가오는 스쿠터 엔진 소리"),
                Cue(5.0f, 7.4f, "배달 스쿠터가 창 앞을 지나가는 소리"),
                Cue(7.4f, 9.6f, "스쿠터 소리가 멀어진다"),
                Cue(9.6f, 9999, "냉장고가 낮게 도는 소리")),
            Slot("0630", "06:30", "첫 버스", 6.5f, 4.5f, 9.0f, false,
                Cue(0, 2.3f, "이른 아침 새소리"),
                Cue(2.3f, 5.6f, "첫 버스가 다가오는 엔진 소리"),
                Cue(5.6f, 7.6f, "버스가 멈추는 소리 · 에어브레이크"),
                Cue(7.6f, 9.0f, "버스 문이 열리는 소리"),
                Cue(9.0f, 18f, "버스 공회전 소리"),
                Cue(18f, 9999, "실내의 낮은 소음")),
            // Weekday slots (2026-11-21, silence1-film/visit/LOG.md): the times people pass this window.
            Slot("1400", "14:00", "초등학생 하굣길", 14f, 5.0f, 10.5f, false,
                Cue(0, 2.0f, "먼 도로 소리와 새소리"),
                Cue(2.0f, 6.0f, "가방 멘 아이 둘이 걸어오는 소리"),
                Cue(6.0f, 7.0f, "“같이 가!” — 앞선 아이가 뒤를 보고 부른다"),
                Cue(7.0f, 10.5f, "뒤따라 뛰는 운동화 발소리"),
                Cue(10.5f, 9999, "냉장고가 낮게 도는 소리")),
            Slot("1640", "16:40", "중학생 하굣길", 16f + 40f / 60f, 3.5f, 9.0f, false,
                Cue(0, 2.0f, "늦은 오후 거리의 낮은 소음"),
                Cue(2.0f, 4.2f, "교복 입은 학생 셋이 걸어오는 소리"),
                Cue(4.2f, 7.0f, "둘이 주고받는 대화 — “집에 가?” “응, 가자.”"),
                Cue(7.0f, 7.9f, "함께 웃는 소리"),
                Cue(7.9f, 10.0f, "모퉁이를 돌며 잦아드는 말소리와 발소리"),
                Cue(10.0f, 9999, "늦은 오후 거리의 낮은 소음")),
            Slot("1900", "19:00", "직장인 귀갓길", 19f, 2.3f, 15.0f, false,
                Cue(0, 2.8f, "정류장에 선 버스의 공회전 소리"),
                Cue(2.8f, 4.5f, "버스 문이 열리는 소리 · 내리는 사람의 발걸음"),
                Cue(4.5f, 6.6f, "인도로 흩어지는 발걸음"),
                Cue(6.6f, 7.3f, "버스 문이 닫히는 소리"),
                Cue(7.3f, 13.3f, "버스가 출발해 멀어지는 소리"),
                Cue(13.3f, 9999, "버스가 떠난 뒤 남은 발소리")),
            Slot("2300", "23:00", "가게 문 닫는 시간", 23f, 3.8f, 8.0f, true,
                Cue(0, 1.0f, "밤거리의 낮은 소음"),
                Cue(1.0f, 5.1f, "길 건너 가게 앞, 두 사람의 말소리와 웃음"),
                Cue(5.1f, 6.0f, "위층에서 물건 떨어지는 소리"),
                Cue(6.0f, 8.0f, "길 건너 웃음소리"),
                Cue(8.0f, 10.5f, "두 사람이 걸어가며 멀어지는 소리"),
                Cue(10.5f, 9999, "실내의 낮은 소음")),
            // 주변 환경 체험 (2026-11-21): same room and window, three busy places, ~10 s clips that loop. They come after the
            // times; the dial, PageUp/PageDown and the drag only ever see the times. Timecodes from review/env (traffic_*.json,
            // the train and airliner sheets) on the composited clips.
            Place("hwy", "고가도로", "고가도로 앞", 2.5f, 6.0f,
                Cue(0, 2.9f, "고가도로를 달리는 차들의 끊이지 않는 타이어 소리"),
                Cue(2.9f, 5.1f, "화물차의 낮은 엔진음이 지나간다"),
                Cue(5.1f, 9999, "고가도로를 달리는 차들의 끊이지 않는 타이어 소리")),
            Place("rail", "철도", "철도 주변", 2.5f, 9.6f,
                Cue(0, 1.0f, "양방향으로 달리는 차 소리"),
                Cue(1.0f, 3.0f, "전동차가 다가오는 소리"),
                Cue(3.0f, 8.5f, "전동차가 지나가는 소리 · 철길 바퀴 소리"),
                Cue(8.5f, 9.6f, "마지막 칸이 빠져나간다"),
                Cue(9.6f, 9999, "다시 들리는 도로의 차 소리")),
            Place("air", "활주로", "활주로 주변", 5.0f, 10.0f,
                Cue(0, 4.0f, "간선도로의 차 소리 · 멀리서 커지는 제트엔진 소리"),
                Cue(4.0f, 8.5f, "여객기가 활주로에서 속도를 올린다"),
                Cue(8.5f, 9999, "여객기가 기수를 든다 — 이륙 직전")),
        };

        static VisitSlot Place(string id, string label, string title, float replay, float end, params VisitCue[] cues)
        {
            var s = Slot(id, label, title, 0, replay, end, false, cues);
            s.place = true;
            return s;
        }

        static VisitSlot Slot(string id, string label, string title, float hour, float replay, float end, bool thud, params VisitCue[] cues) => new()
        {
            id = id, label = label, title = title, hour = hour, replayFrom = replay, eventEnd = end, cues = cues,
            clip = Load<VideoClip>($"Video/visit_{id}.mp4"),
            still = Load<Texture2D>($"Stills/still_{id}.png"),
            exterior = Load<AudioClip>($"Audio/ext_{id}.wav"),
            exteriorBed = Load<AudioClip>($"Audio/bed_{id}.wav"),
            interior = Load<AudioClip>($"Audio/room_{id}.wav"),
            structure = thud ? Load<AudioClip>($"Audio/struct_{id}.wav") : null,
        };

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(Root + path);
            if (asset == null) throw new FileNotFoundException(Root + path);
            return asset;
        }

        static void Configure()
        {
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "Stills" }))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "Audio" }))
            {
                var ai = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                var s = ai.defaultSampleSettings;
                // Uncompressed so the scheduled start lands on the exact sample.
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.PCM;
                s.preloadAudioData = true;
                ai.defaultSampleSettings = s;
                ai.forceToMono = false;
                ai.SaveAndReimport();
            }
        }

        [MenuItem("SILENCE1/Create visit scene")]
        public static void CreateScene()
        {
            Configure();
            var previous = SceneManager.GetActiveScene();
            // Additive keeps whatever the user has open; Unity refuses it next to an unsaved untitled scene.
            bool untitled = string.IsNullOrEmpty(previous.path);
            if (untitled && previous.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Visit scene not created: unsaved untitled scene kept.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(20, 18, 20, 255);
            camera.cullingMask = 0;
            camera.gameObject.AddComponent<AudioListener>();
            var visit = new GameObject("Night visit").AddComponent<NightVisit>();
            visit.slots = Slots();
            visit.regular = Load<Font>("Fonts/IBMPlexSansKR-Regular.ttf");
            visit.bold = Load<Font>("Resources/VisitFont.ttf");
            visit.firstVisitSlot = Array.FindIndex(visit.slots, s => s.label == "02:00");
            visit.startSlot = Array.FindIndex(visit.slots, s => s.label == "14:00");
            EditorSceneManager.SaveScene(scene, SceneFile);
            if (!untitled)
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            // Append only; the demo stays first and the web build keeps its own scene list.
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == SceneFile)) list.Add(new EditorBuildSettingsScene(SceneFile, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("SILENCE1 visit scene saved at " + SceneFile);
        }

        [MenuItem("SILENCE1/Build Windows visit")]
        public static void BuildWindows() => Build(PlayerPath);

        // Same player in its own folder, used only to record the share video (-visitShowcase); Build/Visit stays as shipped.
        public static void BuildShowcase() => Build("Build/VisitShowcase/SILENCE1 Visit.exe");

        static void Build(string playerPath)
        {
            CreateScene();
            // Player settings shared with the web build are restored after this build.
            var mode = PlayerSettings.fullScreenMode;
            int w = PlayerSettings.defaultScreenWidth, h = PlayerSettings.defaultScreenHeight;
            bool resizable = PlayerSettings.resizableWindow, background = PlayerSettings.runInBackground;
            var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var restoreQuality = UseWebQualityForStandalone(quality);
            try
            {
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.runInBackground = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    // The visit opens first; the open-window demo is one button away and comes back here.
                    scenes = new[] { SceneFile, DemoScene },
                    locationPathName = playerPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
                if (report.summary.result != BuildResult.Succeeded || !File.Exists(playerPath))
                    throw new InvalidOperationException("Windows visit build: " + report.summary.result);
                Debug.Log("SILENCE1 visit player built at " + Path.GetFullPath(playerPath));
            }
            finally
            {
                PlayerSettings.fullScreenMode = mode;
                PlayerSettings.defaultScreenWidth = w;
                PlayerSettings.defaultScreenHeight = h;
                PlayerSettings.resizableWindow = resizable;
                PlayerSettings.runInBackground = background;
                restoreQuality();
                AssetDatabase.SaveAssets();
            }
        }

        // The PC quality level's renderer has an SSAO feature whose resources get stripped from Windows players,
        // so URP never starts and the player stays black. The web build already runs on "Mobile" (no renderer
        // features); the Windows visit build uses that same level, and the project settings are put back afterwards.
        static Action UseWebQualityForStandalone(SerializedObject so)
        {
            var levels = so.FindProperty("m_QualitySettings");
            int mobile = -1;
            for (int i = 0; i < levels.arraySize; i++)
                if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == "Mobile") mobile = i;
            var defaults = so.FindProperty("m_PerPlatformDefaultQuality");
            SerializedProperty standalone = null;
            for (int i = 0; i < defaults.arraySize; i++)
                if (defaults.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "Standalone")
                    standalone = defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second");
            if (mobile < 0 || standalone == null) return () => { };
            var excluded = levels.GetArrayElementAtIndex(mobile).FindPropertyRelative("excludedTargetPlatforms");
            var originalExcluded = Enumerable.Range(0, excluded.arraySize).Select(i => excluded.GetArrayElementAtIndex(i).stringValue).ToArray();
            int originalDefault = standalone.intValue;
            void Write(string[] platforms, int level)
            {
                excluded.arraySize = platforms.Length;
                for (int i = 0; i < platforms.Length; i++) excluded.GetArrayElementAtIndex(i).stringValue = platforms[i];
                standalone.intValue = level;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Write(originalExcluded.Where(p => p != "Standalone").ToArray(), mobile);
            return () => Write(originalExcluded, originalDefault);
        }
    }
}

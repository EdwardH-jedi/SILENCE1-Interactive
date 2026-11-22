using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Silence1
{
    // Adds a way into the visit from the existing open-window demo without editing that scene.
    public sealed class VisitEntry : MonoBehaviour
    {
        const string DemoScene = "Silence1Demo", VisitScene = "Silence1Visit";
        public static string TestDir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded += (scene, _) => Attach(scene);
            Attach(SceneManager.GetActiveScene());
        }

        static void Attach(Scene scene)
        {
            // The web build ships the demo alone; only offer the visit where it was built in.
            if (scene.name != DemoScene || !Application.CanStreamedLevelBeLoaded(VisitScene)) return;
            if (FindAnyObjectByType<VisitEntry>()) return;
            new GameObject("Visit entry").AddComponent<VisitEntry>();
        }

        void Start()
        {
            // The demo camera letterboxes narrow screens and never clears outside its rect, which would keep
            // the previous scene's last frame there. Clear the whole screen first in the demo's paper colour.
            var clear = new GameObject("Letterbox clear").AddComponent<Camera>();
            clear.transform.SetParent(transform, false);
            clear.clearFlags = CameraClearFlags.SolidColor;
            clear.backgroundColor = new Color32(246, 244, 238, 255);
            clear.cullingMask = 0;
            clear.depth = -100;
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule))
                    .GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var canvasGo = new GameObject("Visit entry UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            var r = new GameObject("이 집, 새벽 두 시에도", typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(canvasGo.transform, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(1, 1);
            r.anchoredPosition = new Vector2(-28, -28);
            r.sizeDelta = new Vector2(460, 64);
            var img = r.gameObject.AddComponent<Image>();
            img.color = new Color32(20, 18, 20, 230);
            var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => SceneManager.LoadScene(VisitScene));
            var label = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
            label.SetParent(r, false);
            label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one; label.offsetMin = label.offsetMax = Vector2.zero;
            var text = label.gameObject.AddComponent<Text>();
            text.font = Resources.Load<Font>("VisitFont");
            text.text = "이 집, 새벽 두 시에도 보실래요? →";
            text.fontSize = 24;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color32(252, 251, 247, 255);
            text.raycastTarget = false;
            if (TestDir != null) StartCoroutine(FinishTest());
        }

        IEnumerator FinishTest()
        {
            yield return new WaitForSeconds(2.5f);
            yield return new WaitForEndOfFrame();
            string tag = Screen.width < Screen.height ? "portrait_" : "";
            ScreenCapture.CaptureScreenshot(Path.Combine(TestDir, tag + "09_demo_entry.png"));
            File.AppendAllText(Path.Combine(TestDir, "visit_test.log"), $"demo scene loaded with entry button\n");
            yield return new WaitForSeconds(1f);
            // Go back into the visit through the button itself; NightVisit finishes the test there.
            var button = GetComponentInChildren<Button>();
            yield return NightVisit.Click(button);
            yield return new WaitForSeconds(4f);
            Application.Quit();
        }
    }
}

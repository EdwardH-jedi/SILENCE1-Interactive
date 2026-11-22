using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Silence1
{
    [Serializable]
    public sealed class VisitCue { public float from, to; public string text; }

    [Serializable]
    public sealed class VisitSlot
    {
        public string id;              // file key: "0200" … for the times, "hwy" / "rail" / "air" for the places
        public string label;
        public string title;           // scene name shown next to the time, e.g. "초등학생 하굣길"
        public bool place;             // 주변 환경 place: after the times, not on the dial, loops at the clip end
        public float hour;
        public VideoClip clip;
        public Texture2D still;        // the clip's first frame; drag preview and transition plate
        public AudioClip exterior;     // airborne outdoor event, sample 0 = clip frame 0
        public AudioClip exteriorBed;  // airborne outdoor bed, loops
        public AudioClip interior;     // fridge and room tone, loops; the window switch never touches it
        public AudioClip structure;    // structure-borne one-shot, sample 0 = clip frame 0; optional
        public float replayFrom, eventEnd;
        public VisitCue[] cues;
    }

    // One house at four times of day. Both windows are closed; only the airborne exterior layer changes.
    public sealed class NightVisit : MonoBehaviour
    {
        // Concept mix, not a recording. SILENCE1 sits exactly 45 dB under the conventional window at every
        // frequency: same filter, gain ratio 10^(-45/20). The screen says it is staged next to the switch.
        public const float ConventionalGain = .42f, ConventionalCutoff = 2400f;
        public const float SilenceGain = .00236183f, SilenceCutoff = 2400f;
        const float SwitchSeconds = .12f, CrossfadeSeconds = .45f, SlotFadeSeconds = .5f;

        public VisitSlot[] slots;
        public Font regular, bold;
        public int startSlot, firstVisitSlot;
        public string openWindowScene = "Silence1Demo";

        static readonly Color Paper = new Color32(252, 251, 247, 255), Ink = new Color32(20, 18, 20, 255);
        const string Wordmark = "SILENCE<color=#df4a2b>1</color>";

        sealed class Track
        {
            public VideoPlayer video;
            public RenderTexture frame;
            public AudioSource ext, bed, room, thud;
            public AudioLowPassFilter extLp, bedLp;
            public float fade, fadeTarget;
            public bool live;
        }

        Track[] tracks;
        int current = -1, preview = -1;
        int timeCount, lastTime;   // slots[0..timeCount) are the dial times, the rest are places; lastTime = where "시간대" returns
        bool silence1, muted, captionsOn, visited, compareShown, dialShown, noteShown, videoPending, showingVideo, commitOverlay;
        float mix, overlayAlpha, overlayTarget, overlaySeconds = CrossfadeSeconds, noteUntil = -1;
        double playDsp;
        float playFrom;
        float soloExterior = 1, soloInside = 1;   // measurement solo only (-visitMeasure); always 1 in the experience

        RectTransform canvasRect, photoRect, band, intro, compare, dialGroup, actions, captionRect, menuRect, sceneRect, placeRow;
        Text sceneText, placeCaption, timeText;
        Button timeButton;
        Button[] placeButtons;
        Text[] placeTexts;
        Image background, bandImage;
        RawImage photo, overlay;
        Text question, caption, clock, note, disclaimer, conventionalText, silenceText, replayText, muteText, captionText, menuText, visitText;
        Button visitButton, conventionalButton, silenceButton, replayButton, muteButton, captionButton, menuButton;
        Button[] dialButtons;
        Text[] dialTexts;
        TimeDial dial;
        Image ring, handle;
        readonly List<Selectable> tabOrder = new();
        Vector2 laidOut;
        bool portrait;

        public bool Silence1Selected => silence1;
        public int CurrentSlot => current;
        public float ClipTime => current < 0 ? 0 : playFrom + (float)(AudioSettings.dspTime - playDsp);

        void Start()
        {
            timeCount = Array.FindIndex(slots, s => s.place);
            if (timeCount < 0) timeCount = slots.Length;
            lastTime = firstVisitSlot;
            BuildTracks();
            BuildUI();
            photo.texture = slots[startSlot].still;
            dial.Show(slots[startSlot].hour);
            clock.text = slots[startSlot].label;
            Refresh();
            EventSystem.current.SetSelectedGameObject(visitButton.gameObject);
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-visitTest"), m = Array.IndexOf(args, "-visitMeasure"), r = Array.IndexOf(args, "-visitShowcase");
            if (VisitEntry.TestDir != null) StartCoroutine(ReEntered(VisitEntry.TestDir));
            else if (i >= 0 && i + 1 < args.Length) StartCoroutine(SelfTest(args[i + 1]));
            else if (m >= 0 && m + 1 < args.Length) StartCoroutine(Measure(args[m + 1]));
            else if (r >= 0 && r + 1 < args.Length) StartCoroutine(Showcase(args[r + 1]));
        }

        void BuildTracks()
        {
            tracks = new Track[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                var root = new GameObject("Slot " + s.label);
                root.transform.SetParent(transform, false);
                var t = tracks[i] = new Track { frame = new RenderTexture(1920, 1080, 0) { name = s.label } };
                t.video = root.AddComponent<VideoPlayer>();
                t.video.playOnAwake = false;
                t.video.isLooping = false;
                t.video.waitForFirstFrame = true;
                t.video.skipOnDrop = true;
                t.video.timeUpdateMode = VideoTimeUpdateMode.DSPTime;
                t.video.audioOutputMode = VideoAudioOutputMode.None;
                t.video.renderMode = VideoRenderMode.RenderTexture;
                t.video.targetTexture = t.frame;
                t.video.clip = s.clip;
                t.video.Prepare();
                t.ext = Source(root, "Exterior event", s.exterior, false, out t.extLp);
                t.bed = Source(root, "Exterior bed", s.exteriorBed, true, out t.bedLp);
                t.room = Source(root, "Room", s.interior, true, out _);
                t.thud = Source(root, "Structure", s.structure, false, out _);
            }
        }

        static AudioSource Source(GameObject parent, string name, AudioClip clip, bool loop, out AudioLowPassFilter lowpass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = loop;
            a.playOnAwake = false;
            a.spatialBlend = 0;
            a.volume = 0;
            lowpass = name.StartsWith("Exterior", StringComparison.Ordinal) ? go.AddComponent<AudioLowPassFilter>() : null;
            return a;
        }

        // ---------- flow ----------

        public void Visit()
        {
            if (visited) return;
            visited = true;
            Go(firstVisitSlot, 0);
            Refresh();
        }

        public void Go(int i, float from = 0)
        {
            if (i < 0 || i >= slots.Length) return;
            for (int k = 0; k < tracks.Length; k++)
                if (k != i) tracks[k].fadeTarget = 0;
            bool sameSlot = i == current;
            current = i;
            preview = -1;
            // A place has no hour: the handle hides and the dial keeps the time it came from.
            if (!slots[i].place) { lastTime = i; dial.Show(slots[i].hour); }
            handle.enabled = !slots[i].place;
            clock.text = slots[i].label;
            ShowScene(i);
            if (!sameSlot)
            {
                Present(slots[i].still, CrossfadeSeconds, true);
                showingVideo = false;
            }
            Play(i, from, sameSlot ? .05f : CrossfadeSeconds);
            RefreshDial();
        }

        void ShowScene(int i) => sceneText.text = slots[i].place ? $"주변 환경 · {slots[i].title}"
            : string.IsNullOrEmpty(slots[i].title) ? slots[i].label : $"{slots[i].label} · {slots[i].title}";

        // Places loop: back to frame 0 through its still, window state kept. Times hold their last frame instead.
        void Restart()
        {
            Present(slots[current].still, .3f, true);
            showingVideo = false;
            Play(current, 0, .3f);
        }

        public void BackToTime()
        {
            if (current >= 0 && slots[current].place) Go(lastTime);
        }

        void Play(int i, float from, float delay)
        {
            var t = tracks[i];
            var s = slots[i];
            t.fadeTarget = 1;
            t.live = true;
            playFrom = from;
            playDsp = AudioSettings.dspTime + delay;
            Schedule(t.ext, from);
            Schedule(t.thud, from);
            if (!t.bed.isPlaying && t.bed.clip) t.bed.PlayScheduled(playDsp);
            if (!t.room.isPlaying && t.room.clip) t.room.PlayScheduled(playDsp);
            t.video.Pause();
            t.video.time = from;
            videoPending = true;
        }

        void Schedule(AudioSource a, float from)
        {
            a.Stop();
            if (!a.clip || from >= a.clip.length) return;
            a.timeSamples = Mathf.Clamp((int)(from * a.clip.frequency), 0, a.clip.samples - 1);
            a.PlayScheduled(playDsp);
        }

        public void Replay()
        {
            if (current < 0) return;
            Play(current, slots[current].replayFrom, .05f);
        }

        // Only the airborne exterior layer reads this. Video, room, structure and listener stay untouched.
        public void SetWindow(bool silence)
        {
            silence1 = silence;
            Refresh();
        }

        // PageUp/PageDown stay in their group: the times around the clock, or the places among themselves.
        public void Step(int direction)
        {
            if (current < 0) { Go(firstVisitSlot); return; }
            bool place = slots[current].place;
            int lo = place ? timeCount : 0, n = place ? slots.Length - timeCount : timeCount;
            Go(lo + ((current - lo + direction) % n + n) % n);
        }

        public void GoLabel(string label) => Go(Array.FindIndex(slots, s => s.label == label));

        public void ToggleMute()
        {
            muted = !muted;
            AudioListener.volume = muted ? 0 : 1;
            Refresh();
        }

        public void ToggleCaptions()
        {
            captionsOn = !captionsOn;
            Refresh();
        }

        public void Back()
        {
            AudioListener.volume = 1;
            if (Application.CanStreamedLevelBeLoaded(openWindowScene)) SceneManager.LoadScene(openWindowScene);
        }

        int Nearest(float hour)
        {
            int best = 0;
            float bestGap = float.MaxValue;
            for (int i = 0; i < timeCount; i++)
            {
                float gap = Mathf.Abs(Mathf.DeltaAngle(hour * 15, slots[i].hour * 15));
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        void DialDragged(float hour)
        {
            if (!visited) return;
            handle.enabled = true;   // hidden while a place plays
            clock.text = $"{(int)hour:00}:{(int)(hour % 1 * 60):00}";
            int n = Nearest(hour);
            if (n == preview) return;
            preview = n;
            Present(slots[n].still, .18f, false);
            ShowScene(n);
        }

        void DialReleased(float hour)
        {
            if (!visited) return;
            int n = Nearest(hour);
            if (n != current) { Go(n); return; }
            // Back on the slot that kept playing underneath the preview.
            preview = -1;
            dial.Show(slots[n].hour);
            clock.text = slots[n].label;
            ShowScene(n);
            photo.texture = showingVideo ? tracks[current].frame : slots[current].still;
            overlayTarget = 0;
            commitOverlay = false;
        }

        void Present(Texture tex, float seconds, bool commit)
        {
            if (overlayAlpha > 0) photo.texture = overlay.texture;   // freeze what is on screen
            overlay.texture = tex;
            overlayAlpha = 0;
            overlayTarget = 1;
            overlaySeconds = seconds;
            commitOverlay = commit;
        }

        // ---------- frame loop ----------

        void Update()
        {
            Layout();
            Keys();
            float dt = Time.unscaledDeltaTime;
            double dsp = AudioSettings.dspTime;

            mix = Mathf.MoveTowards(mix, silence1 ? 1 : 0, dt / SwitchSeconds);
            float gain = Mathf.Exp(Mathf.Lerp(Mathf.Log(ConventionalGain), Mathf.Log(SilenceGain), mix));
            float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(ConventionalCutoff), Mathf.Log(SilenceCutoff), mix));
            foreach (var t in tracks)
            {
                t.fade = Mathf.MoveTowards(t.fade, t.fadeTarget, dt / SlotFadeSeconds);
                t.ext.volume = t.bed.volume = gain * t.fade * soloExterior;
                t.extLp.cutoffFrequency = t.bedLp.cutoffFrequency = cutoff;
                t.room.volume = t.thud.volume = t.fade * soloInside;
                if (t.live && t.fade <= 0 && t.fadeTarget <= 0)
                {
                    t.live = false;
                    t.ext.Stop(); t.bed.Stop(); t.room.Stop(); t.thud.Stop();
                    t.video.Pause();
                }
            }

            overlayAlpha = Mathf.MoveTowards(overlayAlpha, overlayTarget, dt / overlaySeconds);
            overlay.color = new Color(1, 1, 1, overlayAlpha);
            if (commitOverlay && overlayAlpha >= 1)
            {
                photo.texture = overlay.texture;
                overlayAlpha = overlayTarget = 0;
                overlay.color = Color.clear;
                commitOverlay = false;
            }

            if (current >= 0)
            {
                var t = tracks[current];
                if (videoPending && dsp >= playDsp && t.video.isPrepared)
                {
                    t.video.time = playFrom + (dsp - playDsp);
                    t.video.Play();
                    videoPending = false;
                }
                if (!showingVideo && !videoPending && !commitOverlay && preview < 0 && t.video.isPlaying && t.video.frame > 0)
                {
                    photo.texture = t.frame;
                    showingVideo = true;
                }
                if (t.video.isPlaying && !videoPending)
                {
                    double expect = playFrom + (dsp - playDsp);
                    if (Math.Abs(t.video.time - expect) > .2 && expect < t.video.length - .3) t.video.time = expect;
                }
                UpdateCaption();
                if (slots[current].place && !videoPending && preview < 0 && ClipTime >= slots[current].clip.length - .1) Restart();
                if (visited && !compareShown && dsp >= playDsp + 1.0) { compareShown = true; noteUntil = Time.unscaledTime + 5.5f; Refresh(); }
                if (visited && !dialShown && current == firstVisitSlot && ClipTime >= slots[current].eventEnd) { dialShown = true; Refresh(); }
            }
            if (noteUntil > 0 && Time.unscaledTime > noteUntil) { noteUntil = -1; noteShown = true; Refresh(); }
        }

        void UpdateCaption()
        {
            string text = null;
            if (captionsOn && slots[current].cues != null)
            {
                float time = ClipTime;
                foreach (var c in slots[current].cues)
                    if (time >= c.from && time < c.to) { text = c.text; break; }
            }
            caption.text = text == null ? "" : "[ " + text + " ]";
            caption.enabled = text != null;
        }

        void Keys()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (k.escapeKey.wasPressedThisFrame) Back();
            if (k.tabKey.wasPressedThisFrame) Tab(k.shiftKey.isPressed ? -1 : 1);
            if (!visited) return;
            if (k.rKey.wasPressedThisFrame && dialShown) Replay();
            if (k.mKey.wasPressedThisFrame) ToggleMute();
            if (k.cKey.wasPressedThisFrame) ToggleCaptions();
            if (k.aKey.wasPressedThisFrame) SetWindow(false);
            if (k.sKey.wasPressedThisFrame) SetWindow(true);
            if (k.pageDownKey.wasPressedThisFrame && dialShown) Step(1);
            if (k.pageUpKey.wasPressedThisFrame && dialShown) Step(-1);
        }

        void Tab(int direction)
        {
            var live = tabOrder.FindAll(s => s.isActiveAndEnabled && s.interactable);
            if (live.Count == 0) return;
            var selected = EventSystem.current.currentSelectedGameObject;
            int at = live.FindIndex(s => s.gameObject == selected);
            int next = at < 0 ? 0 : (at + direction + live.Count) % live.Count;
            EventSystem.current.SetSelectedGameObject(live[next].gameObject);
        }

        // ---------- UI ----------

        void Refresh()
        {
            intro.gameObject.SetActive(!visited);
            compare.gameObject.SetActive(compareShown);
            dialGroup.gameObject.SetActive(dialShown);
            actions.gameObject.SetActive(dialShown);
            placeRow.gameObject.SetActive(dialShown);
            menuRect.gameObject.SetActive(visited);
            sceneRect.gameObject.SetActive(visited && !string.IsNullOrEmpty(sceneText.text));
            note.enabled = compareShown && !noteShown;
            Color text = portrait ? Ink : Paper, glyphOnFill = portrait ? Paper : Ink;
            Style(conventionalButton, conventionalText, !silence1, text, glyphOnFill);
            Style(silenceButton, silenceText, silence1, text, glyphOnFill);
            muteText.text = muted ? "소리 켜기" : "소리 끄기";
            captionText.text = captionsOn ? "자막 끄기" : "자막 켜기";
            RefreshDial();
            if (visited && EventSystem.current.currentSelectedGameObject == visitButton.gameObject)
                EventSystem.current.SetSelectedGameObject(compareShown ? (silence1 ? conventionalButton : silenceButton).gameObject : null);
        }

        void RefreshDial()
        {
            if (dialTexts == null) return;
            for (int i = 0; i < dialTexts.Length; i++)
            {
                dialTexts[i].font = i == current ? bold : regular;
                var c = portrait ? Ink : Paper;
                c.a = i == current ? 1 : .62f;
                dialTexts[i].color = c;
            }
            // The place selector sits on its own dark pill in both orientations.
            Style(timeButton, timeText, current >= 0 && !slots[current].place, Paper, Ink);
            for (int k = 0; k < placeButtons.Length; k++) Style(placeButtons[k], placeTexts[k], current == timeCount + k, Paper, Ink);
        }

        void Style(Button b, Text label, bool active, Color text, Color onFill)
        {
            var fill = text;
            fill.a = active ? 1 : .10f;
            SetColors(b, fill);
            label.color = active ? onFill : text;
        }

        static void SetColors(Button b, Color normal)
        {
            var c = b.colors;
            c.normalColor = normal;
            var lift = normal; lift.a = Mathf.Min(1, normal.a + .14f);
            c.highlightedColor = lift;
            c.selectedColor = lift;
            c.pressedColor = lift;
            c.colorMultiplier = 1;
            c.fadeDuration = .08f;
            b.colors = c;
        }

        void BuildUI()
        {
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var canvasGo = new GameObject("Visit UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            canvasRect = (RectTransform)canvasGo.transform;

            background = Stretch(Node("Background", canvasRect)).gameObject.AddComponent<Image>();
            background.raycastTarget = false;
            photoRect = Node("Photo", canvasRect);
            photo = photoRect.gameObject.AddComponent<RawImage>();
            photo.raycastTarget = false;
            overlay = Stretch(Node("Next photo", photoRect)).gameObject.AddComponent<RawImage>();
            overlay.raycastTarget = false;
            overlay.color = Color.clear;

            band = Node("Controls", canvasRect);
            bandImage = band.gameObject.AddComponent<Image>();
            bandImage.raycastTarget = false;

            captionRect = Node("Caption", canvasRect);
            caption = Label(captionRect, "", 30, false, TextAnchor.MiddleCenter);
            captionRect.sizeDelta = new Vector2(1400, 60);
            var shade = caption.gameObject.AddComponent<Shadow>();
            shade.effectColor = new Color(0, 0, 0, .8f);
            shade.effectDistance = new Vector2(0, -2);

            intro = Node("Question", band);
            question = Label(Node("Text", intro), "이 집, 새벽 두 시에도 보실래요?", 46, true, TextAnchor.MiddleCenter);
            question.rectTransform.sizeDelta = new Vector2(1500, 70);
            question.rectTransform.anchoredPosition = new Vector2(0, 52);
            visitButton = MakeButton(intro, "새벽 2시에 방문", new Vector2(400, 84), 32, Visit, out visitText, true);
            ((RectTransform)visitButton.transform).anchoredPosition = new Vector2(0, -48);

            compare = Node("Window switch", band);
            conventionalButton = MakeButton(compare, "기존 창호", new Vector2(250, 76), 30, () => SetWindow(false), out conventionalText, true);
            silenceButton = MakeButton(compare, Wordmark, new Vector2(250, 76), 30, () => SetWindow(true), out silenceText, true);
            ((RectTransform)conventionalButton.transform).anchoredPosition = new Vector2(-127, 18);
            ((RectTransform)silenceButton.transform).anchoredPosition = new Vector2(127, 18);
            disclaimer = Label(Node("Disclaimer", compare), "연출된 음향 비교입니다", 22, false, TextAnchor.MiddleCenter);
            disclaimer.rectTransform.sizeDelta = new Vector2(600, 34);
            disclaimer.rectTransform.anchoredPosition = new Vector2(0, -48);
            note = Label(Node("Both closed", compare), "두 창 모두 닫혀 있습니다", 26, true, TextAnchor.MiddleCenter);
            note.rectTransform.sizeDelta = new Vector2(600, 40);
            note.rectTransform.anchoredPosition = new Vector2(0, 86);

            BuildDial();

            actions = Node("Actions", band);
            replayButton = MakeButton(actions, "같은 순간 다시 듣기", new Vector2(372, 64), 26, Replay, out replayText, false);
            muteButton = MakeButton(actions, "소리 끄기", new Vector2(182, 56), 22, ToggleMute, out muteText, false);
            captionButton = MakeButton(actions, "자막 켜기", new Vector2(182, 56), 22, ToggleCaptions, out captionText, false);
            ((RectTransform)replayButton.transform).anchoredPosition = new Vector2(0, 40);
            ((RectTransform)muteButton.transform).anchoredPosition = new Vector2(-95, -34);
            ((RectTransform)captionButton.transform).anchoredPosition = new Vector2(95, -34);

            menuRect = Node("Menu", canvasRect);
            menuRect.anchorMin = menuRect.anchorMax = menuRect.pivot = new Vector2(0, 1);
            menuButton = MakeButton(menuRect, "← 열린 창 체험", new Vector2(250, 52), 22, Back, out menuText, false);
            var mr = (RectTransform)menuButton.transform;
            mr.anchorMin = mr.anchorMax = mr.pivot = new Vector2(0, 1);
            mr.anchoredPosition = Vector2.zero;
            menuRect.anchoredPosition = new Vector2(24, -24);

            sceneRect = Node("Scene name", canvasRect);
            sceneRect.anchorMin = sceneRect.anchorMax = sceneRect.pivot = new Vector2(1, 1);
            sceneRect.anchoredPosition = new Vector2(-24, -24);
            sceneRect.sizeDelta = new Vector2(400, 52);
            var sceneBack = sceneRect.gameObject.AddComponent<Image>();
            sceneBack.sprite = PillSprite();
            sceneBack.type = Image.Type.Sliced;
            sceneBack.color = new Color(Ink.r, Ink.g, Ink.b, .55f);
            sceneBack.raycastTarget = false;
            sceneText = Label(Stretch(Node("Text", sceneRect)), "", 22, false, TextAnchor.MiddleCenter);

            BuildPlaces();

            tabOrder.AddRange(new Selectable[] { visitButton, conventionalButton, silenceButton });
            tabOrder.AddRange(dialButtons);
            tabOrder.Add(timeButton);
            tabOrder.AddRange(placeButtons);
            tabOrder.AddRange(new Selectable[] { replayButton, muteButton, captionButton, menuButton });
        }

        void BuildDial()
        {
            dialGroup = Node("Time dial", band);
            var ringRect = Node("Ring", dialGroup);
            ringRect.sizeDelta = new Vector2(210, 210);
            ring = ringRect.gameObject.AddComponent<Image>();
            ring.sprite = RingSprite();
            dial = ringRect.gameObject.AddComponent<TimeDial>();
            dial.radius = 210 * 116f / 256f;
            dial.dragged = DialDragged;
            dial.released = DialReleased;
            var h = Node("Handle", ringRect);
            h.sizeDelta = new Vector2(26, 26);
            handle = h.gameObject.AddComponent<Image>();
            handle.sprite = DiscSprite();
            handle.raycastTarget = false;
            dial.handle = h;
            clock = Label(Node("Clock", ringRect), "14:00", 36, true, TextAnchor.MiddleCenter);
            clock.rectTransform.sizeDelta = new Vector2(160, 56);
            clock.raycastTarget = false;
            dialButtons = new Button[timeCount];
            dialTexts = new Text[timeCount];
            for (int i = 0; i < timeCount; i++)
            {
                int index = i;
                dialButtons[i] = MakeButton(ringRect, slots[i].label, new Vector2(104, 40), 24, () => Go(index), out dialTexts[i], false);
                ((RectTransform)dialButtons[i].transform).anchoredPosition = TimeDial.OnRing(slots[i].hour, 140);
                SetColors(dialButtons[i], new Color(1, 1, 1, 0));
            }
        }

        // 주변 환경 체험: one row, "시간대" (back to the last time) then one button per place.
        void BuildPlaces()
        {
            placeRow = Node("Places", canvasRect);
            var back = placeRow.gameObject.AddComponent<Image>();
            back.sprite = PillSprite();
            back.type = Image.Type.Sliced;
            back.color = new Color(Ink.r, Ink.g, Ink.b, .55f);
            back.raycastTarget = false;
            float x = 16;
            var cap = Node("Caption", placeRow);
            cap.anchorMin = cap.anchorMax = cap.pivot = new Vector2(0, .5f);
            cap.sizeDelta = new Vector2(150, 40);
            cap.anchoredPosition = new Vector2(x, 0);
            placeCaption = Label(cap, "주변 환경 체험", 21, true, TextAnchor.MiddleLeft);
            x += 150;
            Button Add(string label, float w, Action click, out Text text)
            {
                var b = MakeButton(placeRow, label, new Vector2(w, 40), 21, click, out text, false);
                var r = (RectTransform)b.transform;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, .5f);
                r.anchoredPosition = new Vector2(x, 0);
                x += w + 6;
                return b;
            }
            timeButton = Add("시간대", 100, BackToTime, out timeText);
            int count = slots.Length - timeCount;
            placeButtons = new Button[count];
            placeTexts = new Text[count];
            for (int k = 0; k < count; k++)
            {
                int index = timeCount + k;
                placeButtons[k] = Add(slots[index].title, 40 + 21 * slots[index].title.Length, () => Go(index), out placeTexts[k]);
            }
            placeRow.sizeDelta = new Vector2(x - 6 + 16, 52);
        }

        void Layout()
        {
            var size = canvasRect.rect.size;
            if (size == laidOut || size.x <= 0) return;
            laidOut = size;
            portrait = size.y > size.x * .95f;
            float w = size.x, h = size.y;
            photoRect.anchorMin = photoRect.anchorMax = photoRect.pivot = new Vector2(.5f, 1);
            band.anchorMin = new Vector2(0, 0);
            band.anchorMax = new Vector2(1, 0);
            band.pivot = new Vector2(.5f, 0);
            captionRect.anchorMin = captionRect.anchorMax = new Vector2(.5f, 0);
            float bandHeight;
            if (!portrait)
            {
                // Photo as large as the window allows; controls sit over the floor.
                float pw = Mathf.Min(w, h * 16f / 9f), ph = pw * 9f / 16f;
                photoRect.sizeDelta = new Vector2(pw, ph);
                photoRect.anchoredPosition = new Vector2(0, -(h - ph) / 2);
                bandHeight = 310;
                bandImage.color = new Color(Ink.r, Ink.g, Ink.b, .74f);
                background.color = Ink;
                intro.anchoredPosition = Vector2.zero;
                compare.anchoredPosition = new Vector2(-40, -6);
                dialGroup.anchoredPosition = new Vector2(-w / 2 + 220, -12);
                actions.anchoredPosition = new Vector2(w / 2 - 250, 0);
                captionRect.anchoredPosition = new Vector2(0, bandHeight + 44);
            }
            else
            {
                // Whole 16:9 frame on top, controls in the space below.
                float ph = w * 9f / 16f;
                photoRect.sizeDelta = new Vector2(w, ph);
                photoRect.anchoredPosition = Vector2.zero;
                bandHeight = h - ph;
                bandImage.color = Paper;
                background.color = Paper;
                float top = bandHeight / 2;
                intro.anchoredPosition = new Vector2(0, top - Mathf.Min(260, bandHeight * .3f));
                compare.anchoredPosition = new Vector2(0, top - 150);
                dialGroup.anchoredPosition = new Vector2(0, Mathf.Min(top - 450, 0));
                actions.anchoredPosition = new Vector2(0, -top + 140);
                captionRect.anchoredPosition = new Vector2(0, bandHeight + 40);
            }
            // Portrait has room below the photo; bigger touch targets there.
            var groupScale = Vector3.one * (portrait ? 1.3f : 1f);
            intro.localScale = compare.localScale = dialGroup.localScale = actions.localScale = groupScale;
            band.sizeDelta = new Vector2(0, bandHeight);
            band.anchoredPosition = Vector2.zero;
            if (!portrait)
            {
                // Top centre, between the menu button and the scene name.
                placeRow.SetParent(canvasRect, false);
                placeRow.anchorMin = placeRow.anchorMax = placeRow.pivot = new Vector2(.5f, 1);
                placeRow.anchoredPosition = new Vector2(0, -24);
                placeRow.localScale = Vector3.one;
            }
            else
            {
                // Below the photo, between the dial (±210 at 1.3×) and the action buttons (top +94 at 1.3×).
                placeRow.SetParent(band, false);
                placeRow.anchorMin = placeRow.anchorMax = placeRow.pivot = new Vector2(.5f, .5f);
                placeRow.anchoredPosition = new Vector2(0, (dialGroup.anchoredPosition.y - 210 + actions.anchoredPosition.y + 94) / 2);
                placeRow.localScale = groupScale;
            }
            Color text = portrait ? Ink : Paper;
            foreach (var t in canvasRect.GetComponentsInChildren<Text>(true))
                if (t != caption && t != menuText && t != sceneText && !t.transform.IsChildOf(placeRow)) t.color = text;
            placeCaption.color = Paper;
            sceneText.color = Paper;
            caption.color = Paper;
            handle.color = text;
            ring.color = text;
            question.fontSize = portrait ? 52 : 46;
            Style(visitButton, visitText, true, text, portrait ? Paper : Ink);
            Style(replayButton, replayText, false, text, text);
            Style(muteButton, muteText, false, text, text);
            Style(captionButton, captionText, false, text, text);
            SetColors(menuButton, new Color(Ink.r, Ink.g, Ink.b, .55f));
            menuText.color = Paper;
            Refresh();
        }

        // ---------- small builders ----------

        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RectTransform Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        Text Label(RectTransform r, string s, int size, bool heavy, TextAnchor anchor)
        {
            var t = r.gameObject.AddComponent<Text>();
            t.font = heavy ? bold : regular;
            t.text = s;
            t.fontSize = size;
            t.alignment = anchor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        Button MakeButton(Transform parent, string label, Vector2 size, int fontSize, Action click, out Text text, bool heavy)
        {
            var r = Node(label, parent);
            r.sizeDelta = size;
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = PillSprite();
            img.type = Image.Type.Sliced;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => click());
            text = Label(Stretch(Node("Label", r)), label, fontSize, heavy, TextAnchor.MiddleCenter);
            return b;
        }

        static Sprite pill, disc, ringSprite;

        static Sprite PillSprite()
        {
            if (pill) return pill;
            const int s = 64, rad = 18;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(rad - x - .5f, x + .5f - (s - rad))), dy = Mathf.Max(0, Mathf.Max(rad - y - .5f, y + .5f - (s - rad)));
                float a = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy) + .5f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return pill = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(rad, rad, rad, rad));
        }

        static Sprite DiscSprite()
        {
            if (disc) return disc;
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(s / 2f, s / 2f));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(s / 2f - 1 - d + .5f)));
            }
            tex.Apply();
            return disc = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(.5f, .5f));
        }

        static Sprite RingSprite()
        {
            if (ringSprite) return ringSprite;
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var c = new Vector2(s / 2f, s / 2f);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                var p = new Vector2(x + .5f, y + .5f) - c;
                float r = p.magnitude;
                float a = Mathf.Clamp01(1.3f - Mathf.Abs(r - 116));   // ring
                float hour = Mathf.Repeat(Mathf.Atan2(p.x, p.y) / (Mathf.PI * 2) * 24, 24);
                float off = Mathf.Abs(hour - Mathf.Round(hour)) * (Mathf.PI * 2 / 24) * r;   // arc distance to nearest hour
                bool major = Mathf.RoundToInt(hour) % 6 == 0;
                float inner = major ? 96 : 105;
                if (r > inner && r < 116) a = Mathf.Max(a, Mathf.Clamp01((major ? 1.6f : 1.1f) - off));
                tex.SetPixel(x, y, new Color(1, 1, 1, a * .9f));
            }
            tex.Apply();
            return ringSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(.5f, .5f));
        }

        // ---------- self-test (run the player with -visitTest <folder>) ----------

        // The brief's route: 14:00 listing → 02:00 first visit → 14:00 → 16:40 → 19:00 → 23:00 → across midnight → 06:30,
        // with A/B by mouse and keyboard, R/M/C/Esc keys, rapid switching and the dial drag across midnight.
        IEnumerator SelfTest(string dir)
        {
            Directory.CreateDirectory(dir);
            string log = Path.Combine(dir, "visit_test.log");
            string tag = Screen.width < Screen.height ? "portrait_" : "";
            File.WriteAllText(log, $"screen {Screen.width}x{Screen.height} slots {string.Join(",", Array.ConvertAll(slots, s => s.label))}\n");
            void Log(string s) => File.AppendAllText(log, $"[{Time.realtimeSinceStartup:0.00}] {s}\n");
            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, tag + name + ".png"));
                Log("shot " + name);
            }
            IEnumerator Until(float clipTime) { while (ClipTime < clipTime) yield return null; }
            int Index(string label) => Array.FindIndex(slots, s => s.label == label);
            float Mid(int i, string word)
            {
                var c = Array.Find(slots[i].cues, q => q.text.Contains(word));
                if (c == null) return (slots[i].replayFrom + slots[i].eventEnd) / 2;
                float end = c.to > slots[i].eventEnd ? Mathf.Max(slots[i].eventEnd, c.from + 1) : c.to;
                return (c.from + end) / 2;
            }
            float ExtTime(Track t) => t.ext.isPlaying ? t.ext.timeSamples / (float)t.ext.clip.frequency : -1;
            string State()
            {
                var t = tracks[current];
                return $"slot {slots[current].label} clip {ClipTime:0.000} video {t.video.time:0.000} ext {ExtTime(t):0.000} " +
                       $"extVol {t.ext.volume:0.000000} bedVol {t.bed.volume:0.000000} cutoff {t.extLp.cutoffFrequency:0} roomVol {t.room.volume:0.000} thudVol {t.thud.volume:0.000} silence1 {silence1} captions {captionsOn} listener {AudioListener.volume}";
            }
            string Others()
            {
                var parts = new List<string>();
                for (int k = 0; k < tracks.Length; k++)
                    if (k != current && (tracks[k].live || tracks[k].ext.isPlaying || tracks[k].bed.isPlaying || tracks[k].room.isPlaying))
                        parts.Add($"{slots[k].label}(fade {tracks[k].fade:0.00} live {tracks[k].live})");
                return parts.Count == 0 ? "none" : string.Join(" ", parts);
            }
            IEnumerator RoundTrip(string where)
            {
                var tr = tracks[current];
                yield return Press(Key.A); yield return new WaitForSeconds(.3f);
                Log($"{where} key A  " + State());
                float e0 = ExtTime(tr), r0 = tr.room.volume; double v0 = tr.video.time;
                yield return Press(Key.S); yield return new WaitForSeconds(.3f);
                Log($"{where} key S  " + State() + $" | ext advanced {ExtTime(tr) - e0:0.000}s video advanced {tr.video.time - v0:0.000}s room {r0:0.000}->{tr.room.volume:0.000}");
                yield return Press(Key.A); yield return new WaitForSeconds(.3f);
                Log($"{where} key A  " + State());
            }

            yield return new WaitForSeconds(2.5f);
            Log($"first screen {slots[startSlot].label} visited {visited}");
            yield return Shot("01_1400_listing");
            yield return Click(visitButton);
            Log($"clicked 새벽 2시에 방문 -> {slots[current].label} visited {visited}");
            var dawn = slots[firstVisitSlot];
            float mid = (dawn.replayFrom + dawn.eventEnd) / 2;
            yield return Until(mid - .9f);
            Log("A  " + State());
            yield return Shot("02_0200_conventional");
            var t0 = tracks[current];
            float ext0 = ExtTime(t0), room0 = t0.room.volume;
            double video0 = t0.video.time;
            yield return Click(silenceButton);
            yield return new WaitForSeconds(.3f);
            Log($"clicked SILENCE1 -> {silence1} | ext advanced {ExtTime(t0) - ext0:0.000}s video advanced {t0.video.time - video0:0.000}s room {room0:0.000}->{t0.room.volume:0.000}");
            Log("B  " + State());
            yield return Shot("03_0200_silence1");
            yield return RoundTrip("02:00");
            yield return Press(Key.S);
            yield return Until(dawn.eventEnd + .4f);
            Log("event over; dial shown " + dialShown);
            yield return Press(Key.C);
            yield return Press(Key.R);
            yield return new WaitForSeconds(.5f);
            Log("keys C, R -> " + State());
            yield return Until(mid);
            yield return Shot("04_0200_replay_silence1_captions");

            foreach (var (label, word, shot) in new[] { ("14:00", "같이 가", "05_1400_call"), ("16:40", "대화", "06_1640_talk"), ("19:00", "문", "07_1900_door") })
            {
                int i = Index(label);
                yield return Click(dialButtons[i]);
                yield return new WaitForSeconds(.1f);
                Log($"dial label {label} clicked -> {slots[current].label}; still sounding {Others()}");
                yield return Until(Mid(i, word) - .5f);
                Log($"{label} at '{word}' " + State());
                yield return Shot(shot);
                if (label != "14:00") yield return RoundTrip(label);
            }
            yield return Until(Mid(current, "남은 발소리"));
            Log("19:00 after the bus " + State());
            yield return Shot("07b_1900_footsteps_after_bus");

            // 주변 환경: each place from the selector with an A/B round trip at its key moment, then the loop at the
            // clip end, PageDown among the places, and "시간대" back to the time it came from.
            int fromTime = current;
            foreach (var (k, word) in new[] { (0, "화물차"), (1, "바퀴"), (2, "속도") })
            {
                int i = timeCount + k;
                yield return Click(placeButtons[k]);
                yield return new WaitForSeconds(.1f);
                Log($"place button {slots[i].title} -> {slots[current].title} (place {slots[current].place}, handle shown {handle.enabled}, clock {clock.text}); still sounding {Others()}");
                yield return Until(Mid(i, word) - .5f);
                Log($"{slots[i].id} at '{word}' " + State());
                yield return Shot($"07{(char)('c' + k)}_{slots[i].id}_event");
                yield return RoundTrip(slots[i].id);
            }
            float clipEnd = (float)slots[current].clip.length;
            while (ClipTime < clipEnd - .3f) yield return null;
            Log($"{slots[current].id} near the clip end " + State());
            yield return new WaitForSeconds(1f);
            Log($"{slots[current].id} after the clip end " + State() + $" ext playing {tracks[current].ext.isPlaying} video playing {tracks[current].video.isPlaying}");
            yield return Shot("07f_place_looped");
            yield return Press(Key.PageDown);
            yield return new WaitForSeconds(.6f);
            Log($"PageDown among places -> {slots[current].title}; still sounding {Others()}");
            yield return Click(timeButton);
            yield return new WaitForSeconds(.6f);
            Log($"시간대 button -> {slots[current].label} (came from {slots[fromTime].label}) handle shown {handle.enabled}; still sounding {Others()}");
            yield return Shot("07g_back_to_time");

            int late = Index("23:00");
            yield return Click(dialButtons[late]);
            float thudAt = Array.Find(slots[late].cues, c => c.text.Contains("위층")).from;
            yield return Press(Key.S);
            yield return Until(thudAt - .3f);
            Log("23 S1 " + State());
            yield return Press(Key.A);
            yield return new WaitForSeconds(.6f);
            Log("23 A  " + State() + " thud playing " + tracks[current].thud.isPlaying);
            yield return Shot("08_2300_thud_conventional");

            yield return Press(Key.PageDown);
            yield return new WaitForSeconds(.6f);
            Log($"PageDown across midnight -> {slots[current].label}; still sounding {Others()}");
            yield return Press(Key.PageUp);
            yield return new WaitForSeconds(.6f);
            Log($"PageUp back -> {slots[current].label}");
            yield return Click(dialButtons[Index("06:30")]);
            var bus = slots[current];
            yield return Until((bus.replayFrom + bus.eventEnd) / 2);
            Log("06 " + State());
            yield return Shot("09_0630_bus");

            for (int k = 0; k < 4; k++) yield return Press(Key.PageDown);
            Log($"rapid PageDown x4 -> {slots[current].label}; still sounding right away {Others()}");
            yield return new WaitForSeconds(.6f);
            Log($"0.6 s later still sounding {Others()}");
            yield return Press(Key.PageDown);   // 23:00 -> 02:00
            yield return new WaitForSeconds(1f);
            // Drag the dial from 02:00 back across midnight and let go at 23:00.
            yield return Pointer(RingScreen(2f), false);
            yield return Pointer(RingScreen(2f), true);
            foreach (float h in new[] { 1.6f, 1.1f, .6f, .1f, 23.6f, 23.1f, 23f }) yield return Pointer(RingScreen(h), true);
            yield return new WaitForSeconds(.3f);
            Log($"dragging: preview {(preview >= 0 ? slots[preview].label : "-")} clock {clock.text} current {slots[current].label}");
            yield return Shot("10_dial_drag_preview");
            yield return Pointer(RingScreen(23f), false);
            yield return new WaitForSeconds(.3f);
            Log("dial released -> " + slots[current].label);

            yield return Press(Key.M);
            Log("key M " + State());
            yield return Press(Key.M);
            Log("key M again " + State());
            Log("key Esc -> " + openWindowScene);
            VisitEntry.TestDir = dir;
            yield return Press(Key.Escape);
        }

        public static IEnumerator Press(Key key)
        {
            var kb = Keyboard.current;
            if (kb == null) yield break;
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            yield return null;
        }

        // ---------- measurement (run the player with -visitMeasure <folder>) ----------
        // Every slot, both windows, three layers (full mix / airborne exterior only / interior + structure only), plus a
        // repeat of the full conventional take for the capture floor. Same source, same start sample, same length for
        // A and B. The window state is settled before playback starts, so no switch ramp is in these takes.
        IEnumerator Measure(string dir)
        {
            Directory.CreateDirectory(dir);
            var tap = FindFirstObjectByType<AudioListener>().gameObject.AddComponent<VisitTap>();
            int rate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            string log = Path.Combine(dir, "takes.tsv");
            File.WriteAllText(log, $"# rate {rate} speakerMode {AudioSettings.speakerMode} dspBuffer {bufferLength}x{numBuffers} listenerVolume {AudioListener.volume} " +
                $"conventional {ConventionalGain}/{ConventionalCutoff}Hz silence1 {SilenceGain}/{SilenceCutoff}Hz unity {Application.unityVersion} slots {slots.Length}\n" +
                "file\tslot\twindow\tlayer\tplayDsp\tstartSample\tframes\textVol\tbedVol\troomVol\tthudVol\tcutoff\n");
            const float Pre = .1f;
            yield return new WaitForSeconds(1.5f);
            var takes = new List<(string layer, bool s1, string tag)> {
                ("full", false, "A"), ("full", true, "B"), ("ext", false, "A"), ("ext", true, "B"),
                ("inside", false, "A"), ("inside", true, "B"), ("full", false, "A2"), ("ext", true, "B2"), ("full", false, "switch") };
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                float seconds = Mathf.Max(12f, s.exterior ? s.exterior.length : 0, s.structure ? s.structure.length : 0) + .3f;
                foreach (var (layer, s1, tag) in takes)
                {
                    // Everything silent and stopped first, so the loops restart at sample 0 in every take.
                    foreach (var t in tracks) t.fadeTarget = 0;
                    while (Array.Exists(tracks, t => t.live)) yield return null;
                    yield return new WaitForSeconds(.2f);
                    soloExterior = layer == "inside" ? 0 : 1;
                    soloInside = layer == "ext" ? 0 : 1;
                    silence1 = s1;
                    mix = s1 ? 1 : 0;
                    current = -1;   // same new-slot path (.45 s lead) for every take
                    Go(i, 0);
                    double scheduled = playDsp;   // a place loops near 10 s inside the take and reschedules playDsp
                    long start = (long)Math.Round(playDsp * rate) - (long)Math.Round(Pre * rate);
                    tap.Arm(start, (int)Math.Round((seconds + Pre) * rate));
                    string flips = "";
                    if (tag == "switch")
                    {
                        // Transition take: conventional → SILENCE1 → conventional through the normal SetWindow path, mid-event.
                        float t1 = Mathf.Max(1f, (s.replayFrom + s.eventEnd) / 2 - .75f), t2 = t1 + 1.5f;
                        while (ClipTime < t1) yield return null;
                        SetWindow(true);
                        long f1 = (long)Math.Round(AudioSettings.dspTime * rate) - start;
                        while (ClipTime < t2) yield return null;
                        SetWindow(false);
                        long f2 = (long)Math.Round(AudioSettings.dspTime * rate) - start;
                        flips = $"\t# flip A->B at clip {t1:0.000}s capture frame {f1}; B->A at clip {t2:0.000}s capture frame {f2} (main-thread dspTime)";
                    }
                    while (!tap.Done) yield return null;
                    var tr = tracks[i];
                    string file = $"{s.id}_{tag}_{layer}.wav";
                    tap.Save(Path.Combine(dir, file));
                    File.AppendAllText(log, $"{file}\t{s.label}\t{(s1 ? "SILENCE1" : "conventional")}\t{layer}\t{scheduled:0.000000}\t{start}\t{(int)Math.Round((seconds + Pre) * rate)}\t" +
                        $"{tr.ext.volume:0.000000}\t{tr.bed.volume:0.000000}\t{tr.room.volume:0.000}\t{tr.thud.volume:0.000}\t{tr.extLp.cutoffFrequency:0}{flips}\n");
                }
            }
            soloExterior = soloInside = 1;
            File.AppendAllText(log, "# done\n");
            Application.Quit();
        }

        // ---------- showcase recording (run the player with -visitShowcase <folder>) ----------
        // A fixed route for the share video, recorded from inside the player: the listener's final mix (VisitTap, sample
        // exact) and the rendered screen (async GPU readback, JPEG on worker threads), every frame stamped with dspTime.
        // The route calls the UI's own methods, so no keyboard or mouse input is sent to Windows or to other windows.
        IEnumerator Showcase(string dir)
        {
            Directory.CreateDirectory(dir);
            var tap = FindFirstObjectByType<AudioListener>().gameObject.AddComponent<VisitTap>();
            int rate = AudioSettings.outputSampleRate;
            var marks = new List<string> { "dsp\trealtime\tevent" };
            void Mark(string what) => marks.Add($"{AudioSettings.dspTime:0.000000}\t{Time.realtimeSinceStartupAsDouble:0.000000}\t{what}");
            IEnumerator Clip(float t) { while (ClipTime < t) yield return null; }
            yield return new WaitForSeconds(2f);
            const float Seconds = 47f;
            long start = (long)Math.Round((AudioSettings.dspTime + .2) * rate);
            tap.Arm(start, (int)(Seconds * rate));
            bool recording = true, framesDone = false;
            StartCoroutine(RecordFrames(dir, () => recording, () => framesDone = true));
            while (AudioSettings.dspTime * rate < start) yield return null;
            Mark($"tap start sample {start} rate {rate}");
            yield return new WaitForSeconds(2.5f);
            Visit(); Mark("visit " + slots[current].label);
            yield return Clip(1.5f); ToggleCaptions(); Mark("captions on");
            yield return Clip(5.8f); SetWindow(true); Mark("SILENCE1");
            yield return Clip(8.2f); SetWindow(false); Mark("conventional");
            yield return Clip(10.2f);
            foreach (var (k, on, off, leave) in new[] { (0, 3.2f, 5.6f, 7.6f), (1, 4.0f, 6.5f, 9.75f), (2, 5.5f, 8.0f, 9.75f) })
            {
                Go(timeCount + k); Mark("place " + slots[current].id);
                yield return Clip(on); SetWindow(true); Mark("SILENCE1");
                yield return Clip(off); SetWindow(false); Mark("conventional");
                yield return Clip(leave);
            }
            BackToTime(); Mark("back to " + slots[current].label);
            yield return new WaitForSeconds(2.5f);
            Mark("end");
            recording = false;
            while (!tap.Done) yield return null;
            tap.Save(Path.Combine(dir, "showcase.wav"));
            File.WriteAllLines(Path.Combine(dir, "marks.tsv"), marks);
            while (!framesDone) yield return null;
            Application.Quit();
        }

        IEnumerator RecordFrames(string dir, Func<bool> on, Action done)
        {
            var shot = new RenderTexture(Screen.width, Screen.height, 0);
            var lines = new List<string> { $"frame\tdsp\trealtime\t# {Screen.width}x{Screen.height} uvStartsAtTop {SystemInfo.graphicsUVStartsAtTop}" };
            var jobs = new List<System.Threading.Tasks.Task>();
            int n = 0;
            double next = Time.realtimeSinceStartupAsDouble;
            while (on())
            {
                yield return new WaitForEndOfFrame();
                double now = Time.realtimeSinceStartupAsDouble;
                if (now < next) continue;
                next = Math.Max(next + 1.0 / 30, now);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(shot);
                int idx = n++, w = shot.width, h = shot.height;
                lines.Add($"{idx}\t{AudioSettings.dspTime:0.000000}\t{now:0.000000}");
                UnityEngine.Rendering.AsyncGPUReadback.Request(shot, 0, TextureFormat.RGBA32, req =>
                {
                    if (req.hasError) return;
                    var px = req.GetData<byte>().ToArray();
                    jobs.Add(System.Threading.Tasks.Task.Run(() => File.WriteAllBytes(Path.Combine(dir, $"f{idx:00000}.jpg"),
                        ImageConversion.EncodeArrayToJPG(px, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, 92))));
                });
            }
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            yield return null;
            foreach (var j in jobs) j.Wait();
            File.WriteAllLines(Path.Combine(dir, "frames.tsv"), lines);
            done();
        }

        // Real pointer input through the Input System, so clicks and drags go through the UI module.
        public static IEnumerator Pointer(Vector2 screen, bool down)
        {
            var mouse = Mouse.current;
            if (mouse == null) yield break;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left, down));
            yield return null;
            yield return null;
        }

        public static IEnumerator Click(Component target)
        {
            var p = RectTransformUtility.WorldToScreenPoint(null, target.transform.position);
            yield return Pointer(p, false);
            yield return Pointer(p, true);
            yield return Pointer(p, false);
        }

        Vector2 RingScreen(float hour) =>
            RectTransformUtility.WorldToScreenPoint(null, dial.transform.TransformPoint(TimeDial.OnRing(hour, dial.radius)));

        IEnumerator ReEntered(string dir)
        {
            yield return new WaitForSeconds(1.5f);
            yield return new WaitForEndOfFrame();
            string tag = Screen.width < Screen.height ? "portrait_" : "";
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, tag + "10_visit_reentered.png"));
            File.AppendAllText(Path.Combine(dir, "visit_test.log"), $"re-entered visit from the demo button (click); first screen slot {slots[startSlot].label}, visited {visited}\n");
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }
    }
}

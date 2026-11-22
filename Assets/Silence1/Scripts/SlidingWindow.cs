using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Silence1
{
    public sealed class SlidingWindow : MonoBehaviour
    {
        public Camera roomCamera;
        public WindowSection section;
        public Transform frontSash;
        public BoxCollider gripArea;
        public Transform crescent;
        public AudioSource alleyLow, alleyDetail, latch;
        public float openX, closedX;
        public float Closure { get; private set; } = .5f;

        public bool HasSeated { get; private set; }
        bool dragging, audioStarted;
        static readonly int SashShadow = Shader.PropertyToID("_SashShadowX");
        float grabRailX, grabClosure;
#if !UNITY_WEBGL || UNITY_EDITOR
        InputAction press, motion;
        InputControl heldPointer;
#endif
        Vector2Int screenSize;
        const float RoomAspect = 16f / 9f;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SilenceWindowChanged(float closure, int sound, float handleX, float handleY, int sectionView, int firstSeat);
#endif

        void Start()
        {
            section.Prepare(roomCamera);
            FitCamera();
            MoveTo(.5f);
        }

        void FitCamera()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size == screenSize || size.y == 0) return;
            screenSize = size;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            // Keep the original plate and both jambs visible, including narrow phones.
            float drawAspect = Mathf.Clamp(aspect, 1.14f, RoomAspect);
            float width = Mathf.Min(1, drawAspect / aspect);
            float height = Mathf.Min(1, aspect / drawAspect);
            roomCamera.rect = new Rect((1-width)/2, (1-height)/2, width, height);
            section.RefreshFraming();
            Report();
        }

        void Update()
        {
            FitCamera();
            if (section.UpdateLens(dragging)) Report();
#if !UNITY_WEBGL || UNITY_EDITOR
            var keys = Keyboard.current;
            if (keys != null && keys.vKey.wasPressedThisFrame) SetView(section.Active ? "room" : "section");
            if (keys != null && keys.leftArrowKey.wasPressedThisFrame) Nudge("left");
            if (keys != null && keys.rightArrowKey.wasPressedThisFrame) Nudge("right");
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        void OnEnable()
        {
            press = new InputAction(type: InputActionType.PassThrough, binding: "<Mouse>/leftButton");
            press.AddBinding("<Touchscreen>/touch*/press");
            motion = new InputAction(type: InputActionType.PassThrough, binding: "<Mouse>/position");
            motion.AddBinding("<Touchscreen>/touch*/position");
            press.performed += PressChanged;
            motion.performed += PointerMoved;
            press.Enable();
            motion.Enable();
        }

        void OnDisable()
        {
            press.Dispose();
            motion.Dispose();
            heldPointer = null;
            dragging = false;
        }

        void PressChanged(InputAction.CallbackContext input)
        {
            var pointer = input.control.parent;
            Vector2 position = pointer is TouchControl finger
                ? finger.position.ReadValue() : ((Mouse)pointer).position.ReadValue();
            if (input.ReadValue<float>() > .5f)
            {
                if (heldPointer != null) return;
                heldPointer = pointer;
                Begin(position);
            }
            else if (heldPointer == pointer)
            {
                Slide(position);
                dragging = false;
                heldPointer = null;
            }
        }

        void PointerMoved(InputAction.CallbackContext input)
        {
            if (heldPointer == input.control.parent) Slide(input.ReadValue<Vector2>());
        }
#endif

        void WakeSound()
        {
            if (audioStarted) return;
            audioStarted = true;
            double start = AudioSettings.dspTime + .05;
            alleyLow.PlayScheduled(start);
            alleyDetail.PlayScheduled(start);
            Report();
        }

        void Begin(Vector2 screen)
        {
            WakeSound();
            Physics.SyncTransforms();
            Ray ray = roomCamera.ScreenPointToRay(screen);
            var grip = section.Active ? section.grip : gripArea;
            if (!grip.Raycast(ray, out _, 20)) return;
            if (!RailPoint(screen, out float railX)) return;
            grabRailX = railX;
            grabClosure = Closure;
            dragging = true;
        }

        bool RailPoint(Vector2 screen, out float x)
        {
            var ray = roomCamera.ScreenPointToRay(screen);
            var plane = section.Active ? new Plane(frontSash.up, section.GripPoint()) : new Plane(frontSash.forward, frontSash.position);
            x = 0;
            if (!plane.Raycast(ray, out float distance)) return false;
            x = frontSash.parent.InverseTransformPoint(ray.GetPoint(distance)).x;
            return true;
        }

        void Slide(Vector2 screen)
        {
            if (!dragging || !RailPoint(screen, out float x)) return;
            MoveTo(grabClosure + (x - grabRailX) / (closedX - openX));
        }

        void MoveTo(float value)
        {
            value = Mathf.Clamp01(value);
            bool seated = value == 1 && Closure < 1;
            Closure = value;
            var p = frontSash.localPosition;
            p.x = Mathf.Lerp(openX, closedX, value);
            frontSash.localPosition = p;
            section.Seat(value);
            if (seated) HasSeated = true;
            Shader.SetGlobalFloat(SashShadow, crescent.position.x);
            crescent.localRotation = Quaternion.Euler(0,0,Mathf.Lerp(-12, 0, Mathf.InverseLerp(.96f, 1, value)));
            // The synchronized stems sum to the original street recording when open.
            // Attenuate its upper band faster as the seal approaches the jamb.
            alleyLow.volume = .9f * Mathf.Pow(10, -1.32f * value);
            alleyDetail.volume = .9f * Mathf.Pow(10, -2.85f * value);
            float openingDirection = .10f + .42f * Mathf.Sin(value * Mathf.PI);
            alleyLow.panStereo = openingDirection * .22f;
            alleyDetail.panStereo = openingDirection;
            if (seated && audioStarted) latch.PlayOneShot(latch.clip, .80f);
            Report();
        }

        void Report()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var handle = roomCamera.WorldToScreenPoint(section.Active ? section.GripPoint() : crescent.position);
            SilenceWindowChanged(Closure, audioStarted ? 1 : 0, handle.x / Screen.width, 1 - handle.y / Screen.height,
                section.Active ? 1 : 0, HasSeated && !dragging ? 1 : 0);
#endif
        }

        [UnityEngine.Scripting.Preserve]
        public void SetView(string view)
        {
            if (dragging) return;
            WakeSound();
            section.Show(view.StartsWith("section", System.StringComparison.Ordinal), view.EndsWith("-instant", System.StringComparison.Ordinal));
            Report();
        }

        // Browser pointer capture keeps the active finger attached outside the canvas.
        [UnityEngine.Scripting.Preserve]
        public void BrowserPointer(string message)
        {
            var fields = message.Split(',');
            if (fields[0] == "end") { dragging = false; Report(); return; }
            if (fields.Length != 3 ||
                !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return;
            var screen = new Vector2(x * Screen.width, y * Screen.height);
            if (fields[0] == "down") Begin(screen);
            else if (fields[0] == "move") Slide(screen);
        }

        [UnityEngine.Scripting.Preserve]
        public void Nudge(string direction)
        {
            if (dragging) return;
            WakeSound();
            MoveTo(Mathf.Round((Closure + (direction == "right" ? .05f : -.05f)) * 1000) / 1000);
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            dragging = false;
#if !UNITY_WEBGL || UNITY_EDITOR
            heldPointer = null;
#endif
        }
    }
}

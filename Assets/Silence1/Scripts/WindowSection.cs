using UnityEngine;

namespace Silence1
{
    // The section meshes live on the two original sashes. Only the camera and its mask change.
    public sealed class WindowSection : MonoBehaviour
    {
        public BoxCollider grip;
        public Transform seal;
        public Transform focus;
        public bool Active { get; private set; }

        Camera lens;
        Vector3 roomPosition;
        Quaternion roomRotation;
        float roomFov, roomNear, zoom;
        int roomMask;
        Color roomColor;
        const float ZoomDuration = .32f;

        public void Prepare(Camera camera)
        {
            lens = camera;
            roomPosition = lens.transform.position;
            roomRotation = lens.transform.rotation;
            roomFov = lens.fieldOfView;
            roomNear = lens.nearClipPlane;
            roomColor = lens.backgroundColor;
            roomMask = lens.cullingMask & ~(1 << grip.gameObject.layer);
            lens.cullingMask = roomMask;
        }

        public void Show(bool section, bool instant)
        {
            if (section == Active) return;
            Active = section;
            zoom = instant ? 0 : 1;
            lens.cullingMask = section ? 1 << grip.gameObject.layer : roomMask;
            lens.nearClipPlane = section ? .008f : roomNear;
            lens.orthographic = section;
            lens.backgroundColor = section ? new Color32(241, 238, 229, 255) : roomColor;
            if (section)
            {
                Quaternion angle = Quaternion.Euler(57, -6, 0);
                lens.transform.SetPositionAndRotation(focus.position - angle * Vector3.forward * .55f, angle);
            }
            else lens.transform.SetPositionAndRotation(roomPosition, roomRotation);
            RefreshFraming();
        }

        public void RefreshFraming()
        {
            if (Active)
                lens.orthographicSize = Mathf.Max(.081f, .163f / lens.aspect) * (1 + .22f * zoom * zoom);
            else lens.fieldOfView = roomFov * (1 - .1f * zoom * zoom);
        }

        public bool UpdateLens(bool dragging)
        {
            if (zoom <= 0 || dragging) return false;
            zoom = Mathf.Max(0, zoom - Time.unscaledDeltaTime / ZoomDuration);
            RefreshFraming();
            return true;
        }

        public Vector3 GripPoint()
        {
            var t = grip.transform;
            // Even at full travel a portion of the moving glass is visible in the cut.
            float x = Mathf.Clamp(t.InverseTransformPoint(focus.position).x, .012f, .99f);
            return t.TransformPoint(new Vector3(x, .060f, -.002f));
        }

        public void Seat(float closure)
        {
            float contact = Mathf.InverseLerp(.994f, 1, closure);
            seal.localScale = new Vector3(1 - .30f * contact, 1, 1 + .08f * contact);
        }
    }
}

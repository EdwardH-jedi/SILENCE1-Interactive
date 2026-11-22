using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Silence1
{
    // 24-hour ring: 00:00 at the top, clockwise, so angle spacing equals real time spacing.
    public sealed class TimeDial : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Action<float> dragged, released;
        public RectTransform handle;
        public float radius;
        float hour;

        public static Vector2 OnRing(float hour, float radius)
        {
            float a = hour / 24f * Mathf.PI * 2;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius;
        }

        public void Show(float h)
        {
            hour = h;
            handle.anchoredPosition = OnRing(hour, radius);
        }

        float HourAt(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var p);
            float a = Mathf.Atan2(p.x, p.y);
            if (a < 0) a += Mathf.PI * 2;
            return a / (Mathf.PI * 2) * 24f;
        }

        public void OnPointerDown(PointerEventData e) { Show(HourAt(e)); dragged?.Invoke(hour); }
        public void OnDrag(PointerEventData e) { Show(HourAt(e)); dragged?.Invoke(hour); }
        public void OnPointerUp(PointerEventData e) => released?.Invoke(hour);
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

namespace EditorTweaks.Features.NumericDrag
{
    internal sealed class EditorTweaksNumericDragMarker : MonoBehaviour
    {
        public System.Action? Commit;
        public System.Action? LiveApply;

        public static bool IsDragButton(PointerEventData eventData)
        {
            return eventData.button == PointerEventData.InputButton.Right;
        }

        public void CommitAfterDrag()
        {
            CommitNow();
        }

        public void CommitNow()
        {
            Commit?.Invoke();
        }

        public void ApplyLive()
        {
            LiveApply?.Invoke();
        }
    }
}

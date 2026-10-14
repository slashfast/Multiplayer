using UnityEngine;

namespace Multiplayer.Client.Util
{
    public static class MpInput
    {
        public static Vector3? downAt;
        public static bool dragging;

        public static bool Mouse2UpWithoutDrag { get; private set; }

        // RimWorld 1.7 (Waylith) maps only keyboard keys in Input.GetKey*(KeyCode): it always returns false
        // for KeyCode.Mouse0-6. Mouse buttons are read through GetMouseButton*, the same as Unity does for them.
        public static bool GetKey(KeyCode key) =>
            IsMouse(key) ? Input.GetMouseButton(key - KeyCode.Mouse0) : Input.GetKey(key);

        public static bool GetKeyDown(KeyCode key) =>
            IsMouse(key) ? Input.GetMouseButtonDown(key - KeyCode.Mouse0) : Input.GetKeyDown(key);

        public static bool GetKeyUp(KeyCode key) =>
            IsMouse(key) ? Input.GetMouseButtonUp(key - KeyCode.Mouse0) : Input.GetKeyUp(key);

        private static bool IsMouse(KeyCode key) => key is >= KeyCode.Mouse0 and <= KeyCode.Mouse6;

        public static void Update()
        {
            if (GetKeyDown(KeyCode.Mouse2))
                downAt = Input.mousePosition;

            if (downAt is { } v && (Input.mousePosition - v).sqrMagnitude > 20 && GetKey(KeyCode.Mouse2))
                dragging = true;

            Mouse2UpWithoutDrag = false;

            if (GetKeyUp(KeyCode.Mouse2))
            {
                if (!dragging)
                    Mouse2UpWithoutDrag = true;
                downAt = null;
                dragging = false;
            }
        }
    }
}

// Steamworks.NET only exists on these platforms; the same condition gates its own sources.
#if BETAHUB_STEAMWORKS && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
using System;
using Steamworks;
using UnityEngine;

namespace BetaHub
{
    /// <summary>
    /// Steam's floating keyboard for controller players on Steam Deck and in Big Picture, through
    /// Steamworks.NET. Add it to the <see cref="BugReportWidget"/>'s GameObject; your game initializes
    /// Steamworks and runs its callbacks as usual. Compiled when Steamworks.NET is installed as a package,
    /// or with the BETAHUB_STEAMWORKS scripting define.
    /// </summary>
    public class SteamTextEntry : MonoBehaviour, ITextEntryProvider
    {
        private Callback<FloatingGamepadTextInputDismissed_t> _dismissed;
        private Action<string> _done;
        private static bool _unavailableNoted;

        public bool Open(TextEntryRequest request, Action<string> done)
        {
            var mode = request.Email ? EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeEmail
                : request.Multiline ? EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeMultipleLines
                : EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeSingleLine;
            var field = request.FieldRect;
            try
            {
                _dismissed ??= Callback<FloatingGamepadTextInputDismissed_t>.Create(OnDismissed);
                // false outside Steam Deck and Big Picture: the field stays focused for a hardware keyboard
                if (!SteamUtils.ShowFloatingGamepadTextInput(mode, (int)field.x, (int)field.y, (int)field.width, (int)field.height)) return false;
            }
            catch (InvalidOperationException e)
            {
                if (!_unavailableNoted)
                {
                    _unavailableNoted = true;
                    Debug.LogWarning(UnityBetaHubLog.Prefix + "Steam's keyboard is unavailable: " + e.Message + " Initialize Steamworks before opening the bug report form.", this);
                }
                return false;
            }
            _done = done;
            return true;
        }

        public void Close()
        {
            if (_done == null) return;
            _done = null;
            SteamUtils.DismissFloatingGamepadTextInput();
        }

        // The floating keyboard typed straight into the focused field, so there is no text to hand back.
        private void OnDismissed(FloatingGamepadTextInputDismissed_t dismissed)
        {
            var done = _done;
            _done = null;
            done?.Invoke(null);
        }

        private void OnDestroy()
        {
            _dismissed?.Dispose();
        }
    }
}
#endif

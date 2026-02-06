using System.Windows.Input;

namespace InstaSearch.Commands
{
    internal class ShfitShiftIntercepter
    {
        private static DateTime _lastShiftTapUp = DateTime.MinValue;
        private static DateTime _shiftDownTime = DateTime.MinValue;
        private static bool _shiftWasAlone = true;

        // Max time between two shift taps to trigger search
        private static readonly TimeSpan _doubleTapThreshold = TimeSpan.FromMilliseconds(400);
        // Max duration for a single shift press to count as a "tap" (not a hold for typing)
        private static readonly TimeSpan _tapMaxDuration = TimeSpan.FromMilliseconds(150);

        public static async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            InputManager.Current.PreProcessInput += OnPreProcessInput;
        }

        public static void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            InputManager.Current.PreProcessInput -= OnPreProcessInput;
        }

        private static void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            // Fast path: bail early for non-keyboard input
            if (e.StagingItem.Input is not KeyEventArgs keyArgs)
            {
                return;
            }

            // Only process key down/up events
            var routedEvent = keyArgs.RoutedEvent;
            bool isDown = routedEvent == Keyboard.KeyDownEvent;
            if (!isDown && routedEvent != Keyboard.KeyUpEvent)
            {
                return;
            }

            // Get the actual key (handles Alt+key combinations where Key == Key.System)
            Key key = keyArgs.Key;
            if (key == Key.System)
            {
                key = keyArgs.SystemKey;
            }

            // Fast path for non-shift keys (99% of keystrokes)
            if (key != Key.LeftShift && key != Key.RightShift)
            {
                // Only check modifiers if we're actively tracking a shift sequence
                if (_shiftDownTime != DateTime.MinValue && isDown)
                {
                    // Another key pressed while shift held - invalidate
                    _shiftWasAlone = false;
                    _shiftDownTime = DateTime.MinValue;
                }
                else if (!_shiftWasAlone && !isDown && Keyboard.Modifiers == ModifierKeys.None)
                {
                    // All keys released - reset for next sequence
                    _shiftWasAlone = true;
                }

                return;
            }

            HandleShiftKey(keyArgs, isDown);
        }

        private static void HandleShiftKey(KeyEventArgs e, bool isDown)
        {
            DateTime now = DateTime.UtcNow;

            if (isDown)
            {
                // Only start tracking if this is a fresh press (not key repeat)
                if (_shiftDownTime == DateTime.MinValue)
                {
                    _shiftDownTime = now;
                    // Shift going down; only valid if no other modifiers are held
                    _shiftWasAlone = (Keyboard.Modifiers & ~ModifierKeys.Shift) == 0;
                }
            }
            else
            {
                bool wasQuickTap = _shiftDownTime != DateTime.MinValue &&
                                   (now - _shiftDownTime) < _tapMaxDuration;

                if (_shiftWasAlone && wasQuickTap)
                {
                    // This was a quick, clean shift tap
                    if (now - _lastShiftTapUp < _doubleTapThreshold)
                    {
                        // Double-tap detected!
                        _lastShiftTapUp = DateTime.MinValue;
                        VS.Commands.ExecuteAsync(PackageGuids.InstaSearch, PackageIds.SearchCommand).FireAndForget();
                    }
                    else
                    {
                        _lastShiftTapUp = now;
                    }
                }
                else
                {
                    // Shift was held too long or used with other keys - reset
                    _lastShiftTapUp = DateTime.MinValue;
                }

                // Reset for next press
                _shiftDownTime = DateTime.MinValue;
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    _shiftWasAlone = true;
                }
            }
        }
    }
}

using System.Windows.Input;

namespace InstaSearch.Commands
{
    internal class ShfitShiftIntercepter
    {
        private static DateTime _lastShiftUp = DateTime.MinValue;
        private static bool _shiftWasAlone = true;
        private static readonly TimeSpan _threshold = TimeSpan.FromMilliseconds(400);

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

            var routedEvent = e.StagingItem.Input.RoutedEvent;
            if (routedEvent != Keyboard.KeyDownEvent && routedEvent != Keyboard.KeyUpEvent)
            {
                return;
            }

            // Early exit: only process Shift keys and track when other keys break the sequence
            Key key = keyArgs.Key == Key.System ? keyArgs.SystemKey : keyArgs.Key;
            if (key != Key.LeftShift && key != Key.RightShift)
            {
                // Only do work if we're tracking a potential double-shift
                if (_lastShiftUp != DateTime.MinValue)
                {
                    _shiftWasAlone = false;
                    _lastShiftUp = DateTime.MinValue;
                }
                else if (!_shiftWasAlone)
                {
                    // Reset on key up when all modifiers released
                    if (keyArgs.IsUp && Keyboard.Modifiers == ModifierKeys.None)
                    {
                        _shiftWasAlone = true;
                    }
                }

                return;
            }

            HandleShiftKey(keyArgs);
        }

        private static void HandleShiftKey(KeyEventArgs e)
        {
            // At this point we know it's a Shift key (Left or Right)
            if (e.IsDown)
            {
                // Shift going down; only valid if no other modifiers are held
                if ((Keyboard.Modifiers & ~ModifierKeys.Shift) != 0)
                {
                    _shiftWasAlone = false;
                }
            }
            else if (e.IsUp && _shiftWasAlone)
            {
                DateTime now = DateTime.UtcNow;

                if (now - _lastShiftUp < _threshold)
                {
                    _lastShiftUp = DateTime.MinValue;
                    VS.Commands.ExecuteAsync(PackageGuids.InstaSearch, PackageIds.SearchCommand).FireAndForget();
                }
                else
                {
                    _lastShiftUp = now;
                }
            }

            // Reset the "alone" flag when all keys are released
            if (e.IsUp && Keyboard.Modifiers == ModifierKeys.None)
            {
                _shiftWasAlone = true;
            }
        }
    }
}

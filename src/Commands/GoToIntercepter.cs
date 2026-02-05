using System.Windows.Input;
using InstaSearch.Options;

namespace InstaSearch.Commands
{
    internal class GoToIntercepter
    {
        public static async Task InitializeAsync()
        {
            await VS.Commands.InterceptAsync("Edit.GoToAll", () =>
            {
                // Take over Go To All if the option is enabled and ctrl is pressed, indicating the use of shortuc and not a direct command invocation from the menu.
                if (General.Instance.TakeOverGoToAll && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    VS.Commands.ExecuteAsync(PackageGuids.InstaSearch, PackageIds.SearchCommand).FireAndForget();
                    return CommandProgression.Stop;
                }

                return CommandProgression.Continue;
            });
        }
    }
}

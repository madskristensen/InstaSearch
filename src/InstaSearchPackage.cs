global using System;
global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using Task = System.Threading.Tasks.Task;
using System.Runtime.InteropServices;
using System.Threading;
using InstaSearch.Commands;
using InstaSearch.Options;

namespace InstaSearch
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), "Environment", Vsix.Name, 0, 0, true, SupportsProfiles = true, ProvidesLocalizedCategoryName = false)]
    [Guid(PackageGuids.InstaSearchString)]
    public sealed class InstaSearchPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.RegisterCommandsAsync();
            await GoToIntercepter.InitializeAsync();
        }
    }
}
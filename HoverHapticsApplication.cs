using Loupedeck;

namespace HoverHaptics;

public class HoverHapticsApplication : ClientApplication
{
    protected override string GetProcessName() => "";
    protected override string GetBundleName() => "";
    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}

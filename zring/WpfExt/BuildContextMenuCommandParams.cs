using Zring.Dto;

namespace Zring.WpfExt;

/// <summary>
/// <see cref="AppButton.BuildContextMenuCommand"/> parameters
/// </summary>
internal class BuildContextMenuCommandParams
{

    /// <summary>
    /// Application button
    /// </summary>
    public AppButton Button { get; }
    /// <summary>
    /// Application button information
    /// </summary>
    public ButtonInfo ButtonInfo { get; }

    /// <summary>
    /// CTOR
    /// </summary>
    /// <param name="button">Application button</param>
    /// <param name="buttonInfo">Application button information</param>
    public BuildContextMenuCommandParams(AppButton button, ButtonInfo buttonInfo)
    {
        Button = button;
        ButtonInfo = buttonInfo;
    }
}
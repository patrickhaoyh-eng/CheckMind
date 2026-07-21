namespace CheckMind.App.Core;

internal static class FormalFailureReasonCodes
{
    public const string UniqueChunksEmpty = "UNIQUE_CHUNKS_EMPTY";
    public const string FinalCompareScreenshotMissing = "FINAL_COMPARE_SCREENSHOT_MISSING";

    public const string ChannelParametersUniqueChunksEmpty = "CHANNEL_PARAMETERS_UNIQUE_CHUNKS_EMPTY";
    public const string ChannelParametersFinalCompareMissing = "CHANNEL_PARAMETERS_FINAL_COMPARE_MISSING";
    public const string ControlPanelFinalCompareMissing = "CONTROL_PANEL_FINAL_COMPARE_MISSING";

    public const string FinalCompareSetIncomplete = "FINAL_COMPARE_SET_INCOMPLETE";
    public const string ChildWindowNotClosed = "CHILD_WINDOW_NOT_CLOSED";
    public const string NotReturnedToParent = "NOT_RETURNED_TO_PARENT";
    public const string WindowError = "WINDOW_ERROR";
    public const string WindowNotOpened = "WINDOW_NOT_OPENED";

    public const string CountMismatch = "COUNT_MISMATCH";
    public const string RowFlowNotVerified = "ROW_FLOW_NOT_VERIFIED";

    public static string GetDisplayMessage(string objectKey, string reasonCode)
    {
        return (objectKey, reasonCode) switch
        {
            ("channelSetup", UniqueChunksEmpty) => "Channel Setup 未形成有效去重块。",
            ("channelSetup", FinalCompareScreenshotMissing) => "Channel Setup 缺少最终核验截图。",

            ("sineSetup", ChannelParametersUniqueChunksEmpty) => "Sine Setup 的 Channel Parameters Table 未形成有效去重块。",
            ("sineSetup", ChannelParametersFinalCompareMissing) => "Sine Setup 的 Channel Parameters Table 缺少最终核验拼接图。",
            ("sineSetup", ControlPanelFinalCompareMissing) => "Sine Setup 的 Control Panel 缺少最终核验截图。",

            ("advancedControlSetup", FinalCompareSetIncomplete) => "Advanced Control Setup 的正式核验截图集不完整。",
            ("advancedControlSetup", ChildWindowNotClosed) => "Advanced Control Setup 子窗口未正常关闭。",
            ("advancedControlSetup", NotReturnedToParent) => "Advanced Control Setup 关闭后未返回父窗口。",
            ("advancedControlSetup", WindowError) => "Advanced Control Setup 存在窗口级错误。",

            ("notchProfiles", CountMismatch) => "Notch Profiles 请求数量与实际完成数量不一致。",
            ("notchProfiles", RowFlowNotVerified) => "至少一条 Notch Profile 子窗口未完成正式流程核验。",

            ("profileEditor", WindowNotOpened) => "Profile Editor 未真正打开。",
            ("profileEditor", UniqueChunksEmpty) => "Profile Editor 未形成有效去重块。",
            ("profileEditor", FinalCompareScreenshotMissing) => "Profile Editor 缺少最终核验截图。",
            ("profileEditor", ChildWindowNotClosed) => "Profile Editor 子窗口未正常关闭。",
            ("profileEditor", NotReturnedToParent) => "Profile Editor 关闭后未返回父窗口。",
            ("profileEditor", WindowError) => "Profile Editor 存在窗口级错误。",

            (_, UniqueChunksEmpty) => "未形成有效去重块。",
            (_, FinalCompareScreenshotMissing) => "缺少最终核验截图。",
            (_, ChildWindowNotClosed) => "子窗口未正常关闭。",
            (_, NotReturnedToParent) => "关闭后未返回父窗口。",
            (_, WindowError) => "存在窗口级错误。",

            _ => $"未定义失败短码展示文案：{reasonCode}"
        };
    }
}

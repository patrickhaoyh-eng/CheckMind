using System.Text;
using System.Text.Json;
using System.IO;

namespace CheckMind.App.Core;

public sealed class RunResultsStore
{
    public RunResults Load(string resultsPath)
    {
        var json = File.ReadAllText(resultsPath, Encoding.UTF8);
        var result = JsonSerializer.Deserialize(json, RunResultsJsonContext.Default.RunResults);
        return result ?? throw new InvalidOperationException("Unable to parse results.json");
    }

    public void Save(string resultsPath, RunResults results)
    {
        var json = JsonSerializer.Serialize(results, RunResultsJsonContext.Default.RunResults);
        File.WriteAllText(resultsPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void Initialize(RunContext run, TaskCreateRequest? taskRequest)
    {
        var results = new RunResults(
            RunId: run.RunId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            TaskRequest: taskRequest,
            TaskResult: null,
            Alerts: Array.Empty<TaskAlert>(),
            FormalVerificationSummary: null
        );

        Save(run.ResultsPath, results);
    }

    public void SaveTestlabResult(RunContext run, TaskCreateRequest? taskRequest, TestlabRunResult testlabResult)
    {
        var alerts = new List<TaskAlert>();
        TaskExecutionResult taskResult;

        if (testlabResult.NotchProfileCountMismatch is { } mismatch)
        {
            alerts.Add(new TaskAlert(
                Code: CheckMindTaskResultCodes.NotchProfileCountMismatch,
                Severity: "warning",
                Message: mismatch.UserMessage
            ));

            taskResult = new TaskExecutionResult(
                Status: "completed_with_warning",
                CompletedNotchProfileCount: mismatch.CompletedCount,
                RequestedNotchProfileCount: mismatch.RequestedCount,
                ResultCode: CheckMindTaskResultCodes.NotchProfileCountMismatch,
                Message: mismatch.UserMessage,
                FailedRow: mismatch.FailedRowIndex
            );
        }
        else
        {
            taskResult = new TaskExecutionResult(
                Status: "completed",
                CompletedNotchProfileCount: testlabResult.NotchProfileScans?.Count,
                RequestedNotchProfileCount: taskRequest?.NotchProfileCount
            );
        }

        var formalVerificationSummary = BuildFormalVerificationSummary(testlabResult);

        var results = new RunResults(
            RunId: run.RunId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            TaskRequest: taskRequest,
            TaskResult: taskResult,
            Alerts: alerts,
            FormalVerificationSummary: formalVerificationSummary
        );

        Save(run.ResultsPath, results);
    }

    private static TaskFormalVerificationSummary? BuildFormalVerificationSummary(TestlabRunResult testlabResult)
    {
        if (string.IsNullOrWhiteSpace(testlabResult.FinalCompareDirectory))
        {
            return null;
        }

        var channelSetup = BuildChannelSetupSummary(testlabResult.ChannelSetupFormalResult);
        var sineSetup = BuildSineSetupSummary(testlabResult.SineSetupFormalResult);
        var advancedControlSetup = BuildAdvancedControlSetupSummary(testlabResult.AdvancedControlSetupFormalResult);
        var notchProfiles = BuildNotchProfilesSummary(testlabResult.NotchProfilesFormalResult);
        var profileEditor = BuildProfileEditorSummary(testlabResult.ProfileEditorFormalResult);
        var failedObjects = new List<string>();
        var failedReasons = new List<TaskFormalFailureReason>();

        CollectFailedObject(failedObjects, "channelSetup", channelSetup?.Verified);
        CollectFailedObject(failedObjects, "sineSetup", sineSetup?.Verified);
        CollectFailedObject(failedObjects, "advancedControlSetup", advancedControlSetup?.Verified);
        CollectFailedObject(failedObjects, "notchProfiles", notchProfiles?.Verified);
        CollectFailedObject(failedObjects, "profileEditor", profileEditor?.Verified);
        CollectFailedReasons(failedReasons, "channelSetup", BuildChannelSetupReasonCodes(testlabResult.ChannelSetupFormalResult));
        CollectFailedReasons(failedReasons, "sineSetup", BuildSineSetupReasonCodes(testlabResult.SineSetupFormalResult));
        CollectFailedReasons(failedReasons, "advancedControlSetup", BuildAdvancedControlSetupReasonCodes(testlabResult.AdvancedControlSetupFormalResult));
        CollectFailedReasons(failedReasons, "notchProfiles", BuildNotchProfilesReasonCodes(testlabResult.NotchProfilesFormalResult));
        CollectFailedReasons(failedReasons, "profileEditor", BuildProfileEditorReasonCodes(testlabResult.ProfileEditorFormalResult));

        return new TaskFormalVerificationSummary(
            AllVerified: failedObjects.Count == 0,
            FailedObjects: failedObjects,
            FailedReasons: failedReasons,
            FinalCompareDirectory: testlabResult.FinalCompareDirectory,
            ChannelSetup: channelSetup,
            SineSetup: sineSetup,
            AdvancedControlSetup: advancedControlSetup,
            NotchProfiles: notchProfiles,
            ProfileEditor: profileEditor
        );
    }

    private static void CollectFailedObject(List<string> failedObjects, string objectKey, bool? verified)
    {
        if (verified is false)
        {
            failedObjects.Add(objectKey);
        }
    }

    private static void CollectFailedReasons(List<TaskFormalFailureReason> failedReasons, string objectKey, IReadOnlyList<string> reasonCodes)
    {
        if (reasonCodes.Count > 0)
        {
            var reasonMessages = new List<string>(reasonCodes.Count);
            foreach (var reasonCode in reasonCodes)
            {
                reasonMessages.Add(FormalFailureReasonCodes.GetDisplayMessage(objectKey, reasonCode));
            }

            failedReasons.Add(new TaskFormalFailureReason(objectKey, reasonCodes, reasonMessages));
        }
    }

    private static ChannelSetupVerificationSummary? BuildChannelSetupSummary(TestlabChannelSetupFormalResult? result)
    {
        return result is null
            ? null
            : new ChannelSetupVerificationSummary(
                Verified: result.FlowVerified,
                UniqueChunkCount: result.UniqueChunkCount,
                FinalCompareScreenshotPath: result.FinalCompareScreenshotPath
            );
    }

    private static SineSetupVerificationSummary? BuildSineSetupSummary(TestlabSineSetupFormalResult? result)
    {
        return result is null
            ? null
            : new SineSetupVerificationSummary(
                Verified: result.FlowVerified,
                ChannelParametersUniqueChunkCount: result.ChannelParametersUniqueChunkCount,
                ChannelParametersFinalCompareScreenshotPath: result.ChannelParametersFinalCompareScreenshotPath,
                ControlPanelFinalCompareScreenshotPath: result.ControlPanelFinalCompareScreenshotPath
            );
    }

    private static AdvancedControlSetupVerificationSummary? BuildAdvancedControlSetupSummary(TestlabAdvancedControlSetupFormalResult? result)
    {
        return result is null
            ? null
            : new AdvancedControlSetupVerificationSummary(
                Verified: result.FlowVerified,
                MeasurementsFinalCompareScreenshotPath: result.MeasurementsFinalCompareScreenshotPath,
                SafetyFinalCompareScreenshotPath: result.SafetyFinalCompareScreenshotPath,
                ThroughputRecordingFinalCompareScreenshotPath: result.ThroughputRecordingFinalCompareScreenshotPath,
                ChildWindowClosed: result.ChildWindowClosed,
                ReturnedToParent: result.ReturnedToParent
            );
    }

    private static NotchProfilesVerificationSummary? BuildNotchProfilesSummary(TestlabNotchProfilesFormalResult? result)
    {
        if (result is null)
        {
            return null;
        }

        var rows = new List<NotchProfileRowVerificationSummary>();
        foreach (var row in result.Rows)
        {
            rows.Add(new NotchProfileRowVerificationSummary(
                TargetRowIndex: row.TargetRowIndex,
                Verified: row.FlowVerified,
                UniqueChunkCount: row.UniqueChunkCount,
                FinalCompareScreenshotPath: row.FinalCompareScreenshotPath
            ));
        }

        return new NotchProfilesVerificationSummary(
            Verified: result.FlowVerified && result.CountMatched,
            CountMatched: result.CountMatched,
            RequestedRowCount: result.RequestedRowCount,
            CompletedRowCount: result.CompletedRowCount,
            FailedRowIndex: result.FailedRowIndex,
            Rows: rows
        );
    }

    private static ProfileEditorVerificationSummary? BuildProfileEditorSummary(TestlabProfileEditorFormalResult? result)
    {
        return result is null
            ? null
            : new ProfileEditorVerificationSummary(
                Verified: result.FlowVerified,
                UniqueChunkCount: result.UniqueChunkCount,
                FinalCompareScreenshotPath: result.FinalCompareScreenshotPath,
                ChildWindowClosed: result.ChildWindowClosed,
                ReturnedToParent: result.ReturnedToParent
            );
    }

    private static IReadOnlyList<string> BuildChannelSetupReasonCodes(TestlabChannelSetupFormalResult? result)
    {
        if (result is null || result.FlowVerified)
        {
            return Array.Empty<string>();
        }

        var reasons = new List<string>();
        if (result.UniqueChunkCount <= 0)
        {
            reasons.Add(FormalFailureReasonCodes.UniqueChunksEmpty);
        }

        if (string.IsNullOrWhiteSpace(result.FinalCompareScreenshotPath))
        {
            reasons.Add(FormalFailureReasonCodes.FinalCompareScreenshotMissing);
        }

        return reasons;
    }

    private static IReadOnlyList<string> BuildSineSetupReasonCodes(TestlabSineSetupFormalResult? result)
    {
        if (result is null || result.FlowVerified)
        {
            return Array.Empty<string>();
        }

        var reasons = new List<string>();
        if (result.ChannelParametersUniqueChunkCount <= 0)
        {
            reasons.Add(FormalFailureReasonCodes.ChannelParametersUniqueChunksEmpty);
        }

        if (string.IsNullOrWhiteSpace(result.ChannelParametersFinalCompareScreenshotPath))
        {
            reasons.Add(FormalFailureReasonCodes.ChannelParametersFinalCompareMissing);
        }

        if (string.IsNullOrWhiteSpace(result.ControlPanelFinalCompareScreenshotPath))
        {
            reasons.Add(FormalFailureReasonCodes.ControlPanelFinalCompareMissing);
        }

        return reasons;
    }

    private static IReadOnlyList<string> BuildAdvancedControlSetupReasonCodes(TestlabAdvancedControlSetupFormalResult? result)
    {
        if (result is null || result.FlowVerified)
        {
            return Array.Empty<string>();
        }

        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(result.MeasurementsFinalCompareScreenshotPath) ||
            string.IsNullOrWhiteSpace(result.SafetyFinalCompareScreenshotPath) ||
            string.IsNullOrWhiteSpace(result.ThroughputRecordingFinalCompareScreenshotPath))
        {
            reasons.Add(FormalFailureReasonCodes.FinalCompareSetIncomplete);
        }

        if (!result.ChildWindowClosed)
        {
            reasons.Add(FormalFailureReasonCodes.ChildWindowNotClosed);
        }

        if (!result.ReturnedToParent)
        {
            reasons.Add(FormalFailureReasonCodes.NotReturnedToParent);
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            reasons.Add(FormalFailureReasonCodes.WindowError);
        }

        return reasons;
    }

    private static IReadOnlyList<string> BuildNotchProfilesReasonCodes(TestlabNotchProfilesFormalResult? result)
    {
        if (result is null)
        {
            return Array.Empty<string>();
        }

        var reasons = new List<string>();
        if (!result.CountMatched)
        {
            reasons.Add(FormalFailureReasonCodes.CountMismatch);
        }

        if (result.Rows.Any(static row => !row.FlowVerified))
        {
            reasons.Add(FormalFailureReasonCodes.RowFlowNotVerified);
        }

        if (reasons.Count == 0)
        {
            return Array.Empty<string>();
        }

        return reasons;
    }

    private static IReadOnlyList<string> BuildProfileEditorReasonCodes(TestlabProfileEditorFormalResult? result)
    {
        if (result is null || result.FlowVerified)
        {
            return Array.Empty<string>();
        }

        var reasons = new List<string>();
        if (!result.WindowOpened)
        {
            reasons.Add(FormalFailureReasonCodes.WindowNotOpened);
        }

        if (result.UniqueChunkCount <= 0)
        {
            reasons.Add(FormalFailureReasonCodes.UniqueChunksEmpty);
        }

        if (string.IsNullOrWhiteSpace(result.FinalCompareScreenshotPath))
        {
            reasons.Add(FormalFailureReasonCodes.FinalCompareScreenshotMissing);
        }

        if (!result.ChildWindowClosed)
        {
            reasons.Add(FormalFailureReasonCodes.ChildWindowNotClosed);
        }

        if (!result.ReturnedToParent)
        {
            reasons.Add(FormalFailureReasonCodes.NotReturnedToParent);
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            reasons.Add(FormalFailureReasonCodes.WindowError);
        }

        return reasons;
    }
}

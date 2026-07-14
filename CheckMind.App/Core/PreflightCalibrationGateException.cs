namespace CheckMind.App.Core;

public sealed class PreflightCalibrationGateException : Exception
{
    public string ReportPath { get; }

    public PreflightCalibrationGateException(string message, string reportPath)
        : base(message)
    {
        ReportPath = reportPath;
    }
}


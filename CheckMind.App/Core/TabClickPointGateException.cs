namespace CheckMind.App.Core;

public sealed class TabClickPointGateException : Exception
{
    public string ReportPath { get; }

    public TabClickPointGateException(string message, string reportPath)
        : base(message)
    {
        ReportPath = reportPath;
    }
}


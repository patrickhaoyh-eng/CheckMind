namespace CheckMind.App.Core;

public sealed class WorkstationNotCompliantException : Exception
{
    public string ReportPath { get; }

    public WorkstationNotCompliantException(string message, string reportPath)
        : base(message)
    {
        ReportPath = reportPath;
    }
}

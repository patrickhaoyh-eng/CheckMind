namespace CheckMind.App.Core;

public sealed class TestlabWindowStateException : InvalidOperationException
{
    public TestlabWindowStateException(string message)
        : base(message)
    {
    }
}

